"""Run the production request guards on a private test port; never touch the live game."""
import json, pathlib, shlex, socket, subprocess, tempfile, time, unittest, urllib.request, urllib.error, uuid
ROOT = pathlib.Path(__file__).resolve().parents[2]
class BridgeUpdateTest(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.temp=tempfile.TemporaryDirectory(prefix='operator bridge ');folder=pathlib.Path(cls.temp.name)
  with socket.socket() as probe:
   probe.bind(('127.0.0.1',0));cls.port=probe.getsockname()[1]
  cls.base=f'http://127.0.0.1:{cls.port}'
  source=folder/'RaceDashboardBridge.cs'
  source.write_text((ROOT/'Assets/Managers/RaceDashboardBridge.cs').read_text().replace(':8765',f':{cls.port}'))
  script=(ROOT/'Tools/DashboardRelay/test.sh').read_text();start=script.index('mcs -langversion:latest -define:UNITY_STANDALONE_OSX');end=script.index('\nmcs ',start+1)
  args=shlex.split(script[start:end].replace('\\\n',''))
  cls.exe=folder/'BridgeHarness.exe'
  args=[str(source) if arg=='Assets/Managers/RaceDashboardBridge.cs' else '-out:'+str(cls.exe) if arg.startswith('-out:') else arg for arg in args]
  subprocess.run(args,cwd=ROOT,check=True)
 @classmethod
 def tearDownClass(cls):cls.temp.cleanup()
 def setUp(self):
  self.bridge=subprocess.Popen(['mono',str(self.exe),str(ROOT/'Assets/StreamingAssets')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
  while 'BRIDGE_READY' not in self.bridge.stdout.readline():
   if self.bridge.poll() is not None:raise RuntimeError(self.bridge.stderr.read())
 def tearDown(self):
  self.bridge.communicate('quit\n',timeout=5)
 def request(self,path,body=None):
  request=urllib.request.Request(self.base+path,data=json.dumps(body).encode() if body is not None else None,headers={'Origin':self.base,'Content-Type':'application/json'})
  try:
   with urllib.request.urlopen(request,timeout=3) as response:return json.load(response)
  except urllib.error.HTTPError as error:return json.load(error)
 def command(self,snapshot,command,args=[]):
  return dict(id=str(uuid.uuid4()),targetSession=snapshot['sessionId'],expectedState=snapshot['state'],stateRevision=snapshot['stateRevision'],observedAt=snapshot['serverTime'],settingsRevision=snapshot['settingsRevision'],command=command,args=args)
 def test_update_survives_unrelated_settings_and_phase_changes_but_keeps_session_and_age_guards(self):
  old=self.request('/api/status')
  self.assertTrue(self.request('/api/action/settings',self.command(old,'volume',['engine','0.42']))['ok'])
  self.assertTrue(self.request('/api/action/settings',self.command(old,'updateAndRun'))['ok'])
  self.bridge.stdin.write('state:Game\n');self.bridge.stdin.flush()
  for _ in range(100):
   if self.request('/api/status')['state']=='Game':break
   time.sleep(.01)
  update=self.command(old,'updateAndRun')
  self.assertTrue(self.request('/api/action/settings',update)['ok'])
  self.assertTrue(self.request('/api/action/settings',update)['ok'],'same ID returns original result')
  self.assertEqual(self.request('/api/action/settings',self.command(old,'volume',['engine','0.5']))['reason'],'state_changed')
  wrong=self.command(old,'updateAndRun');wrong['targetSession']='other-session'
  self.assertFalse(self.request('/api/action/settings',wrong)['ok'])
  expired=self.command(old,'updateAndRun');expired['observedAt']-=20000
  self.assertFalse(self.request('/api/action/settings',expired)['ok'])
 def test_launch_handoff_requires_a_verified_request_and_current_session(self):
  snapshot=self.request('/api/status')
  self.assertFalse(self.request('/api/action/settings',self.command(snapshot,'releaseForUpdate',['unverified']))['ok'])
  self.assertTrue(self.request('/api/action/settings',self.command(snapshot,'releaseForUpdate',['verified-job']))['ok'])
  wrong=self.command(snapshot,'releaseForUpdate',['verified-job']);wrong['targetSession']='other-session'
  self.assertFalse(self.request('/api/action/settings',wrong)['ok'])
 def test_window_reposition_is_versioned_and_available_during_race_and_maintenance(self):
  self.bridge.stdin.write('state:Game\n');self.bridge.stdin.flush()
  for _ in range(100):
   snapshot=self.request('/api/status')
   if snapshot['state']=='Game':break
   time.sleep(.01)
  self.assertTrue(snapshot['settings']['windowRepositionAvailable'])
  command=self.command(snapshot,'repositionWindows')
  self.assertTrue(self.request('/api/action/settings',command)['ok'])
  self.assertTrue(self.request('/api/action/settings',command)['ok'],'a repeated ID returns the original result')
  self.assertFalse(self.request('/api/action/settings',self.command(snapshot,'repositionWindows',['unexpected']))['ok'])
  self.assertTrue(self.request('/api/action/settings',self.command(snapshot,'maintenance',['true']))['ok'])
  self.assertFalse(self.request('/api/action/settings',self.command(snapshot,'repositionWindows'))['ok'],'stale settings are refused')
  snapshot=self.request('/api/status')
  self.assertTrue(self.request('/api/action/settings',self.command(snapshot,'repositionWindows'))['ok'])
  wrong=self.command(snapshot,'repositionWindows');wrong['targetSession']='other-session'
  self.assertFalse(self.request('/api/action/settings',wrong)['ok'])
 def test_quick_mode_selection_is_validated_and_queued_while_rules_are_locked(self):
  snapshot=self.request('/api/status')
  self.assertTrue(snapshot['settings']['raceModeAvailable'])
  self.assertTrue(self.request('/api/action/settings',self.command(snapshot,'raceMode',['Quick']))['ok'])
  snapshot=self.request('/api/status')
  self.assertEqual(snapshot['settings']['activePreset'],'Quick')
  self.assertTrue(self.request('/api/action/settings',self.command(snapshot,'testRace'))['ok'])
  snapshot=self.request('/api/status')
  self.assertTrue(self.request('/api/action/settings',self.command(snapshot,'raceMode',['Standard']))['ok'])
  snapshot=self.request('/api/status')
  self.assertEqual(snapshot['settings']['activePreset'],'Quick')
  self.assertEqual(snapshot['settings']['selectedPreset'],'Standard')
  self.assertTrue(snapshot['settings']['hasQueuedPreset'])
  for args in [['Unknown'],['1'],[],['Quick','extra']]:
   self.assertFalse(self.request('/api/action/settings',self.command(snapshot,'raceMode',args))['ok'])
if __name__=='__main__':unittest.main(verbosity=2)
