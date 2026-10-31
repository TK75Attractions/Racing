import unittest, tempfile, subprocess, pathlib, os, time, signal
SCRIPT=pathlib.Path(__file__).resolve().parents[2] / 'Tools/OperatorUpdate/update-and-run.sh'
class UpdateTest(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory(prefix='race update '); self.root=pathlib.Path(self.temp.name)
  self.remote=self.root/'remote.git'; self.repo=self.root/'project space'; self.logs=self.root/'request'; self.bin=self.root/'bin'; self.bin.mkdir()
  self.git('init','--bare',str(self.remote),cwd=self.root)
  self.git('clone',str(self.remote),str(self.repo),cwd=self.root)
  self.git('config','user.email','test@example.com'); self.git('config','user.name','Test')
  self.git('switch','-c','main')
  (self.repo/'.gitignore').write_text('Builds/\nLogs/\n')
  plugin=self.repo/'Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib'; plugin.parent.mkdir(parents=True); plugin.write_text('original plugin')
  native=self.repo/'Native~/RaceAudio';native.mkdir(parents=True)
  (native/'build-macos.sh').write_text('echo generated > Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib\n')
  self.git('add','.');self.git('commit','-m','initial');self.git('push','-u','origin','main')
  self.build_output='mkdir -p "$RACING_OPERATOR_BUILD/Contents/MacOS"\nprintf "#!/bin/sh\\n" > "$RACING_OPERATOR_BUILD/Contents/MacOS/Race"\nchmod +x "$RACING_OPERATOR_BUILD/Contents/MacOS/Race"\necho built > "$RACING_OPERATOR_BUILD/result"\n'
  self.unity=self.bin/'Unity fake';self.unity.write_text('#!/bin/sh\n'+self.build_output);self.unity.chmod(0o755)
  # Discovery must never see or close the operator's real Race during regression tests.
  self.real_pgrep=subprocess.check_output(['which','pgrep'],text=True).strip()
  probe=self.bin/'pgrep';probe.write_text('#!/bin/sh\nif [ "$1" = -P ]; then exec "'+self.real_pgrep+'" "$@"; fi\nexit 1\n');probe.chmod(0o755)
  network=self.bin/'curl';network.write_text('#!/bin/sh\nexit 7\n');network.chmod(0o755)
  launcher=self.bin/'open';launcher.write_text('#!/bin/sh\nif [ \"$1\" = -n ]; then shift; fi\nprintf "%s" "$1" > "'+str(self.root/'launched')+'"\n');launcher.chmod(0o755)
 def tearDown(self):
  for process in getattr(self,'processes',[]):
   if process.poll() is None:
    process.terminate()
    try:process.wait(timeout=8)
    except subprocess.TimeoutExpired:process.kill();process.wait()
  self.temp.cleanup()
 def git(self,*args,cwd=None):return subprocess.check_output(['git',*args],cwd=cwd or self.repo,stderr=subprocess.DEVNULL,text=True).strip()
 def run_update(self,check=False):
  args=['bash',str(SCRIPT),str(self.repo),str(self.unity),'99999999',str(self.logs)]
  if check:args+=['--check']
  env=dict(os.environ,PATH=str(self.bin)+':'+os.environ['PATH'])
  return subprocess.run(args,env=env,capture_output=True).returncode
 def test_full_pipeline_fast_forwards_and_restores_plugin(self):
  writer=self.root/'writer';self.git('clone','-b','main',str(self.remote),str(writer),cwd=self.root)
  self.git('config','user.email','test@example.com',cwd=writer);self.git('config','user.name','Test',cwd=writer)
  (writer/'latest.txt').write_text('latest');self.git('add','.',cwd=writer);self.git('commit','-m','latest',cwd=writer);self.git('push',cwd=writer)
  self.assertEqual(self.run_update(),0,(self.logs/'update.log').read_text())
  self.assertFalse((self.repo/'latest.txt').exists(),'working checkout stays intact');self.assertTrue((self.logs/'succeeded').exists());self.assertTrue((self.root/'launched').exists())
  self.assertEqual(self.git('status','--porcelain'),'')
 def test_dirty_repo_builds_without_touching_work(self):
  self.git('switch','-c','working-feature')
  (self.repo/'uncommitted.txt').write_text('change')
  plugin=self.repo/'Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib';plugin.write_text('unsaved plugin edit')
  before=self.git('status','--porcelain')
  self.assertEqual(self.run_update(),0,(self.logs/'update.log').read_text())
  self.assertEqual(self.git('branch','--show-current'),'working-feature')
  self.assertEqual(self.git('status','--porcelain'),before)
  self.assertEqual(plugin.read_text(),'unsaved plugin edit')
  self.assertEqual((self.repo/'uncommitted.txt').read_text(),'change')
  self.assertEqual(len(self.git('worktree','list','--porcelain').split('worktree ')),2)
 def test_local_main_ahead_does_not_block_or_get_reset(self):
  (self.repo/'local.txt').write_text('local');self.git('add','.');self.git('commit','-m','local');head=self.git('rev-parse','HEAD');self.assertEqual(self.run_update(),0);self.assertEqual(self.git('rev-parse','HEAD'),head)
 def test_missing_unity_keeps_game_open(self):
  self.unity.unlink();self.assertNotEqual(self.run_update(),0);self.assertFalse((self.logs/'ready').exists())
 def test_insufficient_space_keeps_game_open(self):
  fake=self.bin/'df';fake.write_text('#!/bin/sh\nprintf "Filesystem 1024-blocks Used Available Capacity Mounted\\nmock 100 99 1 99%% /\\n"\n');fake.chmod(0o755)
  self.assertNotEqual(self.run_update(),0);self.assertFalse((self.logs/'ready').exists());self.assertIn('空き容量',(self.logs/'status.txt').read_text())
 def test_network_failure_keeps_game_open(self):
  self.git('remote','set-url','origin',str(self.root/'missing.git'));self.assertNotEqual(self.run_update(),0);self.assertFalse((self.logs/'ready').exists())
 def test_build_failure_does_not_launch(self):
  self.unity.write_text('#!/bin/sh\nexit 7\n');self.assertNotEqual(self.run_update(),0);self.assertFalse((self.root/'launched').exists());self.assertTrue((self.logs/'failed').exists());self.assertEqual(self.git('status','--porcelain'),'')
 def test_cleanup_removes_owned_leftovers_after_git_unregisters_worktree(self):
  real_git=subprocess.check_output(['which','git'],text=True).strip()
  wrapper=self.bin/'git'
  wrapper.write_text('#!/bin/bash\nif [[ "$*" == *" worktree remove --force "* ]] && [ ! -f "'+str(self.root/'cleanup-injected')+'" ]; then\n "'+real_git+'" "$@"\n target="${@: -1}"\n mkdir -p "$target/Library"\n echo late > "$target/Library/late-file"\n touch "'+str(self.root/'cleanup-injected')+'"\n exit 1\nfi\nexec "'+real_git+'" "$@"\n')
  wrapper.chmod(0o755)
  real_rm=subprocess.check_output(['which','rm'],text=True).strip()
  remover=self.bin/'rm'
  remover.write_text('#!/bin/bash\nif [[ "$*" == *"/Builds/OperatorSource/"* ]] && [ ! -f "'+str(self.root/'remove-retry')+'" ]; then\n touch "'+str(self.root/'remove-retry')+'"\n exit 1\nfi\nexec "'+real_rm+'" "$@"\n')
  remover.chmod(0o755)
  self.assertEqual(self.run_update(),0,(self.logs/'update.log').read_text())
  self.assertTrue((self.root/'remove-retry').exists())
  self.assertTrue((self.logs/'succeeded').exists())
  self.assertEqual(list((self.repo/'Builds/OperatorSource').iterdir()),[])
  self.assertFalse((self.repo/'Logs/OperatorUpdate.lock').exists())
  self.assertEqual(len(self.git('worktree','list','--porcelain').split('worktree ')),2)
 def test_check_does_not_switch_or_build(self):
  self.git('switch','-c','feature');self.assertEqual(self.run_update(check=True),0);self.assertEqual(self.git('branch','--show-current'),'feature');self.assertFalse((self.logs/'ready').exists())
 def test_offline_defaults_resolve_unity_and_do_not_wait_for_pid_zero(self):
  env=dict(os.environ,PATH=str(self.bin)+':'+os.environ['PATH'],RACING_UNITY_PATH=str(self.unity))
  result=subprocess.run(['bash',str(SCRIPT),str(self.repo),'','0',str(self.logs)],env=env,timeout=10)
  self.assertEqual(result.returncode,0);self.assertTrue((self.logs/'succeeded').exists())
 def test_lock_rejects_second_update_without_removing_first_lock(self):
  lock=self.repo/'Logs/OperatorUpdate.lock';lock.mkdir(parents=True);(lock/'owner.pid').write_text('123')
  self.assertNotEqual(self.run_update(),0);self.assertEqual((lock/'owner.pid').read_text(),'123')
  self.assertFalse((self.logs/'ready').exists())
 def test_built_source_is_fetched_main_and_then_cleaned(self):
  self.git('switch','-c','feature')
  (self.repo/'feature.txt').write_text('feature');self.git('add','.');self.git('commit','-m','feature')
  self.unity.write_text('#!/bin/bash\nwhile [ "$1" != "-projectPath" ]; do shift; done\ncd "$2"\ntest ! -f feature.txt || exit 9\ntest "$(git rev-parse HEAD)" = "$(cat "'+str(self.repo)+'"/../request/commit.txt)" || exit 8\n'+self.build_output)
  self.assertEqual(self.run_update(),0,(self.logs/'update.log').read_text())
  self.assertEqual(self.git('branch','--show-current'),'feature')
  self.assertEqual(list((self.repo/'Builds/OperatorSource').iterdir()),[])
 def start_update(self):
  env=dict(os.environ,PATH=str(self.bin)+':'+os.environ['PATH'])
  process=subprocess.Popen(['bash',str(SCRIPT),str(self.repo),str(self.unity),'0',str(self.logs)],env=env)
  self.processes=getattr(self,'processes',[])+[process]
  return process
 def wait_file(self,path,timeout=8):
  deadline=time.monotonic()+timeout
  while time.monotonic()<deadline:
   if path.exists():return
   time.sleep(.02)
  self.fail('Timed out waiting for '+str(path))
 def existing_race(self):
  game=self.root/'existing Race.app/Contents/MacOS/Race';game.parent.mkdir(parents=True)
  game.write_text('#!/bin/bash\ntrap \'echo stopped > "'+str(self.root/'old-stopped')+'"; exit 0\' TERM\necho started > "'+str(self.root/'old-started')+'"\nwhile true; do sleep .1; done\n');game.chmod(0o755)
  player=subprocess.Popen([str(game)])
  self.processes=getattr(self,'processes',[])+[player]
  self.wait_file(self.root/'old-started')
  probe=self.bin/'pgrep';probe.write_text('#!/bin/sh\nif [ "$1" = -P ]; then exec "'+self.real_pgrep+'" "$@"; fi\nprintf "%s\\n" '+str(player.pid)+'\n');probe.chmod(0o755)
  return player
 def test_existing_race_survives_build_and_stops_only_before_launch(self):
  player=self.existing_race()
  self.unity.write_text('#!/bin/bash\necho building > "'+str(self.root/'building')+'"\nwhile [ ! -f "'+str(self.root/'finish-build')+'" ]; do sleep .1; done\n'+self.build_output)
  launcher=self.bin/'open';launcher.write_text('#!/bin/sh\ntest -f "'+str(self.root/'old-stopped')+'" || exit 9\necho launched > "'+str(self.root/'launched')+'"\n');launcher.chmod(0o755)
  update=self.start_update();self.wait_file(self.root/'building')
  self.assertIsNone(player.poll());self.assertFalse((self.logs/'ready').exists())
  (self.root/'finish-build').touch()
  self.assertEqual(update.wait(timeout=15),0,(self.logs/'update.log').read_text())
  self.assertTrue((self.root/'old-stopped').exists());self.assertTrue((self.root/'launched').exists())
 def test_built_app_waits_for_match_permission_without_stopping_existing_race(self):
  player=self.existing_race();self.logs.mkdir(parents=True)
  (self.logs/'handoff.wait').write_text('relay')
  update=self.start_update();self.wait_file(self.logs/'built')
  self.assertIsNone(player.poll());self.assertFalse((self.logs/'decision').exists())
  self.assertEqual((self.logs/'phase.txt').read_text().strip(),'waiting-race')
  (self.logs/'launch.allowed').touch();self.wait_file(self.logs/'ready')
  self.assertIsNone(player.poll(),'Race remains until the guarded handoff completes')
  (self.logs/'released').touch()
  self.assertEqual(update.wait(timeout=15),0,(self.logs/'update.log').read_text())
  self.assertTrue((self.root/'old-stopped').exists());self.assertTrue((self.root/'launched').exists())
 def test_cancel_after_build_while_waiting_for_match_keeps_existing_race(self):
  player=self.existing_race();self.logs.mkdir(parents=True)
  (self.logs/'handoff.wait').write_text('relay')
  update=self.start_update();self.wait_file(self.logs/'built')
  (self.logs/'decision').write_text('cancel\n')
  self.assertEqual(update.wait(timeout=12),130,(self.logs/'update.log').read_text())
  self.assertIsNone(player.poll());self.assertFalse((self.logs/'ready').exists())
  self.assertTrue((self.logs/'cancelled').exists());self.assertFalse((self.root/'launched').exists())
 def test_unity_library_cache_survives_cleanup_and_is_reused_by_a_new_checkout(self):
  self.unity.write_text('#!/bin/sh\nmkdir -p Library\nif [ -f Library/probe ]; then echo warm > "'+str(self.root/'cache-mode')+'"; else echo cold > "'+str(self.root/'cache-mode')+'"; fi\necho imported > Library/probe\n'+self.build_output)
  self.assertEqual(self.run_update(),0)
  self.assertEqual((self.root/'cache-mode').read_text().strip(),'cold')
  cache=self.repo/'Builds/OperatorCache/macOS-unknown/Library/probe'
  self.assertTrue(cache.exists());self.assertEqual(list((self.repo/'Builds/OperatorSource').iterdir()),[])
  original_library=self.repo/'Library';original_library.mkdir();(original_library/'user-editor').write_text('untouched')
  self.logs=self.root/'second request'
  self.assertEqual(self.run_update(),0)
  self.assertEqual((self.root/'cache-mode').read_text().strip(),'warm')
  self.assertEqual((self.logs/'cache.txt').read_text().strip(),'reused')
  self.assertEqual((original_library/'user-editor').read_text(),'untouched')
  self.assertTrue(cache.exists())
 def main_cache(self,version='6000.3.9f1'):
  settings=self.repo/'ProjectSettings';settings.mkdir(exist_ok=True)
  (settings/'ProjectVersion.txt').write_text('m_EditorVersion: '+version+'\n')
  self.git('add','ProjectSettings');self.git('commit','-m','Unity version');self.git('push')
  library=self.repo/'Library';library.mkdir()
  (library/'EditorInstance.json').write_text('{"version":"'+version+'","process_id":12345}')
  (library/'ArtifactDB').write_text('imported artifact database')
  (library/'SourceAssetDB').write_text('imported source database')
  (library/'Artifacts').mkdir();(library/'Artifacts'/'heavy-fbx').write_text('imported school model')
  for name in ['ilpp.pid','ArtifactDB-lock','SourceAssetDB-lock','ShaderCache.db','ShaderCache.db-wal']:(library/name).write_text('live Editor data')
  return library
 def test_first_build_seeds_from_main_library_without_changing_the_working_editor(self):
  library=self.main_cache();before={str(p.relative_to(library)):p.read_bytes() for p in library.rglob('*') if p.is_file()}
  self.unity.write_text('#!/bin/sh\ntest -f Library/Artifacts/heavy-fbx || exit 21\ntest ! -f Library/ilpp.pid || exit 22\ntest ! -f Library/ArtifactDB-lock || exit 23\ntest ! -f Library/ShaderCache.db || exit 24\necho updated-by-build > Library/ArtifactDB\n'+self.build_output)
  self.git('switch','-c','working-feature');(self.repo/'uncommitted.txt').write_text('keep')
  self.assertEqual(self.run_update(),0,(self.logs/'update.log').read_text())
  self.assertEqual((self.logs/'cache.txt').read_text().strip(),'seeded')
  self.assertEqual(before,{str(p.relative_to(library)):p.read_bytes() for p in library.rglob('*') if p.is_file()})
  self.assertEqual(self.git('branch','--show-current'),'working-feature');self.assertEqual((self.repo/'uncommitted.txt').read_text(),'keep')
  self.assertEqual((self.repo/'Builds/OperatorCache/macOS-6000.3.9f1/Library/ArtifactDB').read_text().strip(),'updated-by-build')
  self.logs=self.root/'next request';self.assertEqual(self.run_update(),0)
  self.assertEqual((self.logs/'cache.txt').read_text().strip(),'reused','Dedicated cache takes precedence over main seeding')
 def test_main_library_from_a_different_unity_version_is_not_seeded(self):
  library=self.main_cache();(library/'EditorInstance.json').write_text('{"version":"6000.2.0f1"}')
  self.assertEqual(self.run_update(),0)
  self.assertEqual((self.logs/'cache.txt').read_text().strip(),'cold')
 def test_invalid_main_editor_metadata_falls_back_without_failing_the_build(self):
  library=self.main_cache();(library/'EditorInstance.json').write_text('broken json')
  self.assertEqual(self.run_update(),0);self.assertEqual((self.logs/'cache.txt').read_text().strip(),'cold')
 def test_link_in_main_library_is_not_shared_with_a_build(self):
  library=self.main_cache();external=self.root/'external';external.mkdir();(external/'keep').write_text('keep')
  (library/'linked').symlink_to(external,target_is_directory=True)
  self.assertEqual(self.run_update(),0);self.assertEqual((self.logs/'cache.txt').read_text().strip(),'cold')
  self.assertEqual((external/'keep').read_text(),'keep')
 def test_editor_database_change_during_copy_discards_the_seed(self):
  library=self.main_cache();real_cp=subprocess.check_output(['which','cp'],text=True).strip()
  copier=self.bin/'cp';copier.write_text('#!/bin/sh\n"'+real_cp+'" "$@" || exit $?\necho changed-during-copy >> "'+str(library/'ArtifactDB')+'"\n');copier.chmod(0o755)
  self.unity.write_text('#!/bin/sh\ntest ! -e Library/Artifacts/heavy-fbx || exit 25\n'+self.build_output)
  self.assertEqual(self.run_update(),0,(self.logs/'update.log').read_text());self.assertEqual((self.logs/'cache.txt').read_text().strip(),'cold')
  self.assertIn('コピー中にメインプロジェクトが更新された',(self.logs/'update.log').read_text())
 def test_cancellation_during_seed_stops_copy_without_saving_partial_cache(self):
  library=self.main_cache();copier=self.bin/'cp'
  copier.write_text('#!/bin/bash\nmkdir -p "$3";echo partial > "$3/ArtifactDB"\necho started > "'+str(self.root/'copy-started')+'"\nwhile true; do sleep .1; done\n');copier.chmod(0o755)
  update=self.start_update();self.wait_file(self.root/'copy-started');(self.logs/'decision').write_text('cancel\n')
  self.assertEqual(update.wait(timeout=12),130)
  self.assertEqual((library/'ArtifactDB').read_text(),'imported artifact database')
  self.assertFalse((self.repo/'Builds/OperatorCache/macOS-6000.3.9f1/Library').exists())
  self.assertEqual(list((self.repo/'Builds/OperatorSource').iterdir()),[])
 def test_cancelled_import_keeps_cache_for_the_next_build(self):
  self.unity.write_text('#!/bin/bash\nmkdir -p Library;echo partial > Library/probe\necho importing > "'+str(self.root/'cache-started')+'"\nwhile true; do sleep .1; done\n')
  update=self.start_update();self.wait_file(self.root/'cache-started')
  (self.logs/'decision').write_text('cancel\n')
  self.assertEqual(update.wait(timeout=12),130)
  self.assertEqual((self.repo/'Builds/OperatorCache/macOS-unknown/Library/probe').read_text().strip(),'partial')
  self.assertFalse((self.repo/'Logs/OperatorUpdate.lock').exists())
 def test_cache_owned_by_another_project_is_not_reused_or_deleted(self):
  cache=self.repo/'Builds/OperatorCache/macOS-unknown';cache.mkdir(parents=True)
  (cache/'owner.txt').write_text('/another/project');(cache/'keep').write_text('safe')
  self.assertNotEqual(self.run_update(),0)
  self.assertEqual((cache/'keep').read_text(),'safe')
  self.assertFalse((self.logs/'build.log').exists())
 def test_linked_cache_cannot_reuse_the_working_editors_library(self):
  library=self.repo/'Library';library.mkdir();(library/'editor').write_text('unchanged')
  cache=self.repo/'Builds/OperatorCache/macOS-unknown';cache.mkdir(parents=True)
  (cache/'owner.txt').write_text(str(self.repo));(cache/'Library').symlink_to(library,target_is_directory=True)
  self.assertNotEqual(self.run_update(),0)
  self.assertEqual((library/'editor').read_text(),'unchanged')
  self.assertTrue((cache/'Library').is_symlink())
 def test_cancel_during_build_stops_owned_children_keeps_race_and_cleans_lock(self):
  player=self.existing_race()
  self.unity.write_text('#!/bin/bash\necho $$ > "'+str(self.root/'build-pid')+'"\nsleep 120 &\necho $! > "'+str(self.root/'compiler-pid')+'"\nwait\n'+self.build_output)
  update=self.start_update();self.wait_file(self.root/'compiler-pid')
  (self.logs/'decision').write_text('cancel\n')
  self.assertEqual(update.wait(timeout=12),130,(self.logs/'update.log').read_text())
  self.assertIsNone(player.poll());self.assertFalse((self.root/'launched').exists())
  self.assertTrue((self.logs/'cancelled').exists());self.assertFalse((self.logs/'failed').exists())
  self.assertFalse((self.repo/'Logs/OperatorUpdate.lock').exists())
  self.assertEqual(list((self.repo/'Builds/OperatorSource').iterdir()),[])
  for name in ['build-pid','compiler-pid']:
   pid=int((self.root/name).read_text())
   state=subprocess.run(['ps','-p',str(pid),'-o','stat='],text=True,capture_output=True).stdout.strip()
   self.assertTrue(not state or state.startswith('Z'),name+' must be stopped')
 def test_failed_build_keeps_existing_race_alive(self):
  player=self.existing_race();self.unity.write_text('#!/bin/sh\nexit 7\n')
  self.assertNotEqual(self.run_update(),0);self.assertIsNone(player.poll());self.assertFalse((self.logs/'ready').exists())
 def test_missing_executable_is_not_a_successful_build_and_keeps_race(self):
  player=self.existing_race();self.unity.write_text('#!/bin/sh\nmkdir -p "$RACING_OPERATOR_BUILD"\n')
  self.assertNotEqual(self.run_update(),0);self.assertIsNone(player.poll());self.assertFalse((self.logs/'ready').exists())
 def test_atomic_cancel_file_blocks_launch_even_before_its_content_is_flushed(self):
  self.unity.write_text('#!/bin/bash\necho building > "'+str(self.root/'building')+'"\nsleep 120\n'+self.build_output)
  update=self.start_update();self.wait_file(self.root/'building')
  (self.logs/'decision').touch()
  self.assertEqual(update.wait(timeout=12),130,(self.logs/'update.log').read_text())
  self.assertTrue((self.logs/'cancelled').exists());self.assertFalse((self.root/'launched').exists())
if __name__=='__main__':unittest.main(verbosity=2)
