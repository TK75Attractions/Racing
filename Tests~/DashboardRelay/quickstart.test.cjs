const {test}=require('node:test');
const assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const {mkdtempSync,writeFileSync,readFileSync,existsSync,rmSync}=require('node:fs');
const {tmpdir}=require('node:os');
const path=require('node:path');
const http=require('node:http');
const root=path.resolve(__dirname,'../..');
const dll=path.join(root,'Tools/DashboardRelay/bin/Debug/net10.0/DashboardRelay.dll');
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function until(check){const end=Date.now()+20000;while(Date.now()<end){const result=check();if(result)return result;await wait(20);}throw new Error('Launcher timed out');}
function get(headers={}){return new Promise((resolve,reject)=>{const request=http.get({hostname:'127.0.0.1',port:8766,path:'/api/connection',headers},response=>{let text='';response.on('data',data=>text+=data);response.on('end',()=>resolve({status:response.statusCode,json:JSON.parse(text)}));});request.on('error',reject);});}
function start(env){const child=spawn('dotnet',[dll,'--auto','--no-browser','--managed'],{cwd:root,env:{...process.env,RACING_DASHBOARD_ORIGIN:'',...env}});child.output='';child.stdout.on('data',data=>child.output+=data);child.stderr.on('data',data=>child.output+=data);return child;}
async function ready(child){return until(()=>{const line=child.output.split('\n').find(line=>line.startsWith('RACING_DASHBOARD_READY '));if(line)return JSON.parse(line.slice('RACING_DASHBOARD_READY '.length));if(child.exitCode!==null)throw new Error(child.output);});}
async function stop(child){if(child.exitCode!==null)return;child.stdin.write('stop\n');await until(()=>child.exitCode!==null);assert.equal(child.exitCode,0,child.output);}

test('one-click launcher: automatic Tailscale setup, fallbacks and process ownership',{timeout:150000,skip:process.platform==='win32'},async t=>{
 const temp=mkdtempSync(path.join(tmpdir(),'racing-launcher-'));
 const cli=path.join(temp,'fake-tailscale');
 writeFileSync(cli,`#!/usr/bin/env node
const fs=require('node:fs');const p=require('node:path');
const args=process.argv.slice(2),mode=process.env.FAKE_MODE,dir=process.env.FAKE_DIR;
fs.appendFileSync(p.join(dir,'calls'),JSON.stringify(args)+'\\n');
const route={TCP:{443:{HTTPS:true}},Web:{'race.demo.ts.net:443':{Handlers:{'/':{Proxy:'http://127.0.0.1:8766'}}}}};
if(args[0]==='status'){
 if(mode==='slow'){setTimeout(()=>{},60000);return;}
 console.log(JSON.stringify({BackendState:mode==='logged-out'?'NeedsLogin':'Running',Self:{DNSName:'race.demo.ts.net.',UserID:42,Tags:mode==='tagged'?['tag:game']:[]},User:{42:{LoginName:'owner@example.com'}}}));
}else if(args[0]==='serve'&&args[1]==='status'){
 if(mode==='occupied')route.Web['race.demo.ts.net:443'].Handlers['/'].Proxy='http://127.0.0.1:9999';
 if(mode==='funnel')route.AllowFunnel={'race.demo.ts.net:443':true};
 console.log(JSON.stringify(mode==='occupied'||mode==='funnel'||fs.existsSync(p.join(dir,'served'))?route:{}));
}else if(args[0]==='serve'&&args.includes('--bg')){
 if(mode==='consent'){console.error('Enable HTTPS: https://login.tailscale.com/a/example');process.exit(1);}
 fs.writeFileSync(p.join(dir,'served'),'ok');console.log('Serve started');
}else process.exit(1);
`,{mode:0o755});
 let active;
 const configure=mode=>{
  rmSync(path.join(temp,'served'),{force:true});writeFileSync(path.join(temp,'calls'),'');
  active=start({RACING_TAILSCALE_CLI:cli,FAKE_MODE:mode,FAKE_DIR:temp});return active;
 };
 try{
  await t.test('no Tailscale: local WebSocket starts with no environment file',async()=>{
   active=start({RACING_TAILSCALE_CLI:path.join(temp,'missing')});const info=await ready(active);
   assert.equal(info.localUrl,'http://127.0.0.1:8766/');assert.equal(info.remoteUrl,null);assert.match(info.notice,/Tailscale/);
   assert.equal((await get()).json.transport,'websocket');await stop(active);
  });
  await t.test('DNS name and current operator are discovered and Serve is verified',async()=>{
   active=configure('running');const info=await ready(active);assert.equal(info.remoteUrl,'https://race.demo.ts.net/');
   const owner=await get({Host:'race.demo.ts.net','Tailscale-User-Login':'owner@example.com'});
   const viewer=await get({Host:'race.demo.ts.net','Tailscale-User-Login':'viewer@example.com'});
   assert.equal(owner.json.canControl,true);assert.equal(viewer.json.canControl,false);
   const calls=readFileSync(path.join(temp,'calls'),'utf8');assert.match(calls,/--bg/);assert.match(calls,/--https=443/);assert.ok(!calls.includes('funnel'));
   const reused=start({RACING_TAILSCALE_CLI:cli,FAKE_DIR:temp});const reusedInfo=await ready(reused);
   assert.equal(reusedInfo.reused,true);await until(()=>reused.exitCode!==null);assert.equal(reused.exitCode,0);
   assert.equal((await get()).status,200,'reusing a relay must not stop its owner');await stop(active);
  });
  for(const mode of ['logged-out','occupied','funnel','consent'])await t.test(mode+': local fallback and existing settings preserved',async()=>{
   active=configure(mode);const info=await ready(active);assert.equal(info.remoteUrl,null);assert.ok(info.notice);
   if(mode==='consent')assert.match(info.notice,/https:\/\/login.tailscale.com\/a\/example/);
   if(mode==='occupied'||mode==='funnel')assert.ok(!readFileSync(path.join(temp,'calls'),'utf8').includes('--bg'));
   assert.equal((await get()).status,200);await stop(active);
  });
  await t.test('manual operator configuration is preserved',async()=>{
   rmSync(path.join(temp,'served'),{force:true});active=start({RACING_TAILSCALE_CLI:cli,FAKE_MODE:'running',FAKE_DIR:temp,RACING_DASHBOARD_OPERATORS:'staff@example.com'});await ready(active);
   assert.equal((await get({Host:'race.demo.ts.net','Tailscale-User-Login':'staff@example.com'})).json.canControl,true);
   assert.equal((await get({Host:'race.demo.ts.net','Tailscale-User-Login':'owner@example.com'})).json.canControl,false);await stop(active);
  });
  await t.test('stopping during initial setup cancels the owned CLI too',async()=>{
   active=configure('slow');await until(()=>existsSync(path.join(temp,'calls'))&&readFileSync(path.join(temp,'calls'),'utf8').includes('status'));
   await stop(active);
  });
 }finally{if(active&&active.exitCode===null)active.kill('SIGKILL');rmSync(temp,{recursive:true,force:true});}
});
