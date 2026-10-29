import unittest, tempfile, subprocess, pathlib, os
SCRIPT=pathlib.Path(__file__).resolve().parents[2] / 'Tools/OperatorUpdate/update-and-run.sh'
class UpdateTest(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory(prefix='race update '); self.root=pathlib.Path(self.temp.name)
  self.remote=self.root/'remote.git'; self.repo=self.root/'project space'; self.logs=self.root/'request'; self.bin=self.root/'bin'; self.bin.mkdir()
  self.git('init','--bare',str(self.remote),cwd=self.root)
  self.git('clone',str(self.remote),str(self.repo),cwd=self.root)
  self.git('config','user.email','test@example.com'); self.git('config','user.name','Test')
  self.git('switch','-c','main')
  (self.repo/'.gitignore').write_text('Builds/\n')
  plugin=self.repo/'Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib'; plugin.parent.mkdir(parents=True); plugin.write_text('original plugin')
  native=self.repo/'Native~/RaceAudio';native.mkdir(parents=True)
  (native/'build-macos.sh').write_text('echo generated > Assets/Plugins/RaceAudio/macOS/libRaceAudio.dylib\n')
  self.git('add','.');self.git('commit','-m','initial');self.git('push','-u','origin','main')
  self.unity=self.bin/'Unity fake';self.unity.write_text('#!/bin/sh\nmkdir -p "$RACING_OPERATOR_BUILD"\necho built > "$RACING_OPERATOR_BUILD/result"\n');self.unity.chmod(0o755)
  launcher=self.bin/'open';launcher.write_text('#!/bin/sh\nprintf "%s" "$1" > "'+str(self.root/'launched')+'"\n');launcher.chmod(0o755)
 def tearDown(self):self.temp.cleanup()
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
  self.assertTrue((self.repo/'latest.txt').exists());self.assertTrue((self.logs/'succeeded').exists());self.assertTrue((self.root/'launched').exists())
  self.assertEqual(self.git('status','--porcelain'),'')
 def test_dirty_repo_keeps_game_open(self):
  (self.repo/'uncommitted.txt').write_text('change');self.assertNotEqual(self.run_update(),0);self.assertFalse((self.logs/'ready').exists())
 def test_local_main_ahead_keeps_game_open(self):
  (self.repo/'local.txt').write_text('local');self.git('add','.');self.git('commit','-m','local');self.assertNotEqual(self.run_update(),0);self.assertFalse((self.logs/'ready').exists())
 def test_missing_unity_keeps_game_open(self):
  self.unity.unlink();self.assertNotEqual(self.run_update(),0);self.assertFalse((self.logs/'ready').exists())
 def test_network_failure_keeps_game_open(self):
  self.git('remote','set-url','origin',str(self.root/'missing.git'));self.assertNotEqual(self.run_update(),0);self.assertFalse((self.logs/'ready').exists())
 def test_build_failure_does_not_launch(self):
  self.unity.write_text('#!/bin/sh\nexit 7\n');self.assertNotEqual(self.run_update(),0);self.assertFalse((self.root/'launched').exists());self.assertTrue((self.logs/'failed').exists());self.assertEqual(self.git('status','--porcelain'),'')
 def test_check_does_not_switch_or_build(self):
  self.git('switch','-c','feature');self.assertEqual(self.run_update(check=True),0);self.assertEqual(self.git('branch','--show-current'),'feature');self.assertFalse((self.logs/'ready').exists())
if __name__=='__main__':unittest.main(verbosity=2)
