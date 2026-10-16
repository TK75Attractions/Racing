const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const http = require('node:http');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
function wire(body) {
  let crc = 65535;
  for (const c of body) { crc ^= c.charCodeAt(0) << 8; for(let i=0;i<8;i++) crc=((crc&32768)?(crc<<1)^0x1021:crc<<1)&65535; }
  return '@'+body+'*'+crc.toString(16).toUpperCase().padStart(4,'0');
}
function request(route, { port=8766, method='GET', host='127.0.0.1:8766', origin, user, body } = {}) {
  return new Promise((resolve,reject)=>{
    const headers={Host:host};
    if(origin) headers.Origin=origin;
    if(user) headers['Tailscale-User-Login']=user;
    if(body!==undefined) {body=typeof body==='string'?body:JSON.stringify(body);headers['Content-Length']=Buffer.byteLength(body);headers['Content-Type']='application/json';}
    const req=http.request({hostname:'127.0.0.1',port,path:route,method,headers},res=>{
      const chunks=[];res.on('data',chunk=>chunks.push(chunk));res.on('end',()=>{
        const text=Buffer.concat(chunks).toString();let json;try{json=JSON.parse(text);}catch{}
        resolve({status:res.statusCode,text,json,headers:res.headers});
      });
    });req.on('error',reject);req.setTimeout(10000,()=>req.destroy(new Error('HTTP timeout')));req.end(body);
  });
}
async function waitFor(check, timeout=6000) {
  const until=Date.now()+timeout;
  while(Date.now()<until) { const result=await check();if(result)return result;await delay(20); }
  throw new Error('condition timed out');
}
function connect(route='/api/events', {host='127.0.0.1:8766',origin='http://127.0.0.1:8766',user}={}) {
  return new Promise((resolve,reject)=>{
    const headers={Host:host,Origin:origin,Connection:'Upgrade',Upgrade:'websocket','Sec-WebSocket-Version':'13','Sec-WebSocket-Key':crypto.randomBytes(16).toString('base64')};
    if(user)headers['Tailscale-User-Login']=user;
    const req=http.request({hostname:'127.0.0.1',port:8766,path:route,headers});
    req.on('response',res=>{res.resume();reject(new Error('WS '+res.statusCode));});req.on('error',reject);
    req.on('upgrade',(res,socket,head)=>{
      const connection={packets:[],close:()=>socket.destroy(),socket};let buffer=head;
      function parse(data) {
        buffer=Buffer.concat([buffer,data]);
        while(buffer.length>=2) {
          const op=buffer[0]&15;let count=buffer[1]&127,offset=2;
          if(count===126){if(buffer.length<4)return;count=buffer.readUInt16BE(2);offset=4;}
          if(count===127){if(buffer.length<10)return;count=Number(buffer.readBigUInt64BE(2));offset=10;}
          if(buffer.length<offset+count)return;
          const payload=buffer.subarray(offset,offset+count);buffer=buffer.subarray(offset+count);
          if(op===1)connection.packets.push(JSON.parse(payload.toString()));
          if(op===9) {const mask=crypto.randomBytes(4),masked=Buffer.from(payload);for(let i=0;i<masked.length;i++)masked[i]^=mask[i%4];socket.write(Buffer.concat([Buffer.from([0x8a,0x80|payload.length]),mask,masked]));}
        }
      }
      socket.on('data',parse);socket.on('error',()=>{});parse(Buffer.alloc(0));resolve(connection);
    });req.end();
  });
}

test('production bridge → relay → WebSocket integration', {timeout:90000}, async t=>{
  const bridge=spawn('mono',[path.join(root,'Temp/DashboardRelay/BridgeHarness.exe'),path.join(root,'Assets/StreamingAssets')],{cwd:root,stdio:['pipe','pipe','pipe']});
  let relay=spawn('dotnet',[path.join(root,'Tools/DashboardRelay/bin/Debug/net10.0/DashboardRelay.dll')],{cwd:root,env:{...process.env,RACING_DASHBOARD_ORIGIN:'https://race.test.ts.net',RACING_DASHBOARD_OPERATORS:'operator@example.com'},stdio:['ignore','pipe','pipe']});
  let output='';bridge.stdout.on('data',data=>output+=data);bridge.stderr.on('data',data=>output+=data);relay.stderr.on('data',data=>output+=data);
  const sockets=[];const open=async(...args)=>{const socket=await connect(...args);sockets.push(socket);return socket;};
  const send=line=>bridge.stdin.write(line+'\n');
  try {
    await waitFor(async()=>{try{return(await request('/api/connection')).status===200&&output.includes('BRIDGE_READY');}catch{return false;}});
    await t.test('same screen, camera endpoints and transport capabilities',async()=>{
      const html=await request('/');assert.equal(html.status,200);assert.equal(html.headers['x-frame-options'],'DENY');assert.match(html.text,/レース管理/);assert.match(html.text,/latest-tel/);
      assert.equal((await request('/live.js')).status,200);
      const camera=await request('/api/player/1/frame');assert.equal(camera.status,503); // Harness has no GPU camera.
      const snapshot=await request('/api/status');assert.ok(snapshot.json.sessionId);assert.equal(snapshot.json.state,'Title');
      assert.equal((await request('/api/connection')).json.canControl,true);
    });
    const remote={host:'race.test.ts.net',origin:'https://race.test.ts.net',user:'viewer@example.com'};
    await t.test('Serve identity, strict host/origin and operator authorization',async()=>{
      assert.equal((await request('/api/connection',{host:'evil.example'})).status,403);
      assert.equal((await request('/api/connection',{host:remote.host})).status,403);
      assert.equal((await request('/api/connection',remote)).json.canControl,false);
      assert.equal((await request('/api/connection',{...remote,host:'127.0.0.1:8766'})).json.canControl,false);
      assert.equal((await request('/api/connection',{...remote,user:'operator@example.com'})).json.canControl,true);
      assert.equal((await request('/api/action/start',{...remote,method:'POST',body:{}})).status,403);
      assert.equal((await request('/api/action/start',{method:'POST',origin:'https://evil.example',body:{}})).status,403);
      await assert.rejects(connect('/api/events',{origin:'https://evil.example'}),/403/);
    });
    const first=await open('/api/events',remote);
    await waitFor(()=>first.packets.some(packet=>packet.snapshot));
    await t.test('TEL latest only, outbound command and all non-TEL history',async()=>{
      send('tx:'+wire('CMD,1,0,PING'));send('rx:'+wire('ACK,2,0,1,PING,OK,PONG'));send('rx:'+wire('EVT,3,0,STATE,READY'));
      for(let i=0;i<1000;i++) send('rx:'+wire(`TEL,${i+4},0,-12.5,0.75,0,25,0,0.4`));
      send('rx:'+wire('ERR,1004,0,ERROR,I2C,CONNECTION_LOST,0x68'));
      send('rx:@TEL,1005,0,0,1,0,0,1,0*0000');
      await waitFor(()=>first.packets.some(packet=>packet.lines.some(line=>line.status==='INVALID')));
      const lines=first.packets.flatMap(packet=>packet.lines);
      assert.equal(lines.filter(line=>line.status!=='INVALID'&&line.line.startsWith('@TEL,')).length,0);
      assert.equal(lines.filter(line=>line.direction==='TX').length,1);
      assert.ok(first.packets.some(packet=>packet.telemetry?.line.startsWith('@TEL,')));
      assert.ok(lines.some(line=>line.status==='ERROR'));
    });
    await t.test('receive-thread log delivery continues while Unity frame loop is stalled',async()=>{
      send('freeze');await delay(100);const count=first.packets.length;
      send('rx:'+wire('EVT,1010,0,I2C_RESTORED'));
      await waitFor(()=>first.packets.slice(count).some(packet=>packet.lines.some(line=>line.line.includes('I2C_RESTORED'))));
      send('unfreeze');
    });
    await t.test('reconnect replays only unseen logs and signals retained-history gaps',async()=>{
      const last=first.packets.at(-1);first.close();
      send('event:offline event');await delay(60);
      const resumed=await open(`/api/events?serialSession=${last.serialSession}&after=${last.cursor}`);
      await waitFor(()=>resumed.packets.some(packet=>packet.lines.some(line=>line.line==='offline event')));
      const lines=resumed.packets.flatMap(packet=>packet.lines);assert.ok(lines.every(line=>line.id>last.cursor));
      for(let i=0;i<5200;i++)send('event:history '+i);
      await delay(300);
      const gap=await open(`/api/events?serialSession=${last.serialSession}&after=0`);
      await waitFor(()=>gap.packets.some(packet=>packet.gap));
      assert.ok(gap.packets.some(packet=>packet.alerts.some(line=>line.status==='ERROR')),'Errors survive ordinary-history eviction');
      resumed.close();gap.close();
    });
    await t.test('session/state/age checks, command result lookup and duplicate suppression',async()=>{
      await delay(200);
      const snapshot=(await request('/api/status')).json;
      const body={id:crypto.randomUUID(),targetSession:snapshot.sessionId,expectedState:snapshot.state,stateRevision:snapshot.stateRevision,observedAt:snapshot.serverTime};
      const options={method:'POST',origin:'http://127.0.0.1:8766',body};
      assert.equal((await request('/api/action/start',{...options,body:{...body,id:crypto.randomUUID(),targetSession:'old'}})).status,409);
      assert.equal((await request('/api/action/start',{...options,body:{...body,id:crypto.randomUUID(),observedAt:1}})).status,409);
      assert.equal((await request('/api/action/start',{...options,body:{...body,id:crypto.randomUUID(),stateRevision:-1}})).status,409);
      const accepted=await request('/api/action/start',options);assert.equal(accepted.status,200);assert.equal(accepted.json.ok,true);
      const duplicate=await request('/api/action/start',options);assert.deepEqual(duplicate.json,accepted.json);
      assert.equal((await request('/api/action/result',options)).status,409);
      assert.equal((await request('/api/command/'+body.id)).json.ok,true);
      send('count');await waitFor(()=>output.includes('APPLIED=1'));
      assert.equal((await request('/api/action/start',{...options,body:'x'.repeat(5000)})).status,413);
    });
    await t.test('two simultaneous viewers receive the same event',async()=>{
      const a=await open(),b=await open();send('event:multiple viewers');
      await Promise.all([a,b].map(s=>waitFor(()=>s.packets.some(p=>p.lines.some(l=>l.line==='multiple viewers')))));
      a.close();b.close();
    });
    if(process.env.RACING_BROWSER_TEST==='1') await t.test('actual browser: unified screen, numeric TEL, event history and race action',async()=>{
      const {chromium}=require('playwright');
      const browser=await chromium.launch({headless:true,channel:'chrome'});
      try {
        const page=await browser.newPage({viewport:{width:1440,height:1100}});
        const errors=[];page.on('pageerror',error=>errors.push(error.message));
        send('state:Title');await page.goto('http://127.0.0.1:8766/');
        await page.getByText('ローカル / WebSocket',{exact:true}).waitFor();
        assert.equal(await page.locator('#share-url').inputValue(),'https://race.test.ts.net/');
        assert.equal(await page.locator('#share-dashboard').isVisible(),true);
        send('rx:'+wire('EVT,500,0,STATE,READY'));
        send('rx:'+wire('TEL,501,0,-12.5,0.75,0,25,0,0.4'));
        send('rx:'+wire('ERR,502,0,WARN,HANDLE,ANGLE_REFERENCE_INVALID'));
        await page.locator('#latest-tel').filter({hasText:'ACCEL 75.0%'}).waitFor();
        await page.locator('#serial-log').filter({hasText:'2人分の入力が使用可能'}).waitFor();
        assert.ok(!(await page.locator('#serial-log').innerText()).includes('@TEL,'));
        assert.match(await page.locator('#serial-alert-list').innerText(),/基準角度が無効/);
        await page.locator('[data-action="start"]:enabled').waitFor();
        fs.mkdirSync(path.join(root,'Artifacts/DashboardRealtime'),{recursive:true});
        await page.screenshot({path:path.join(root,'Artifacts/DashboardRealtime/desktop.png'),fullPage:true});
        await page.locator('[data-action="start"]').click();
        await page.locator('#game-state').filter({hasText:'カウントダウン'}).waitFor();
        await page.setViewportSize({width:390,height:844});
        await page.screenshot({path:path.join(root,'Artifacts/DashboardRealtime/mobile.png'),fullPage:true});
        assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'mobile overflow');
        send('freeze');await delay(3500);
        assert.equal(await page.locator('[data-action="start"]').isDisabled(),true);
        assert.match(await page.locator('#live-detail').innerText(),/状態更新が停止/);
        send('unfreeze');
        relay.kill('SIGKILL');await waitFor(()=>relay.exitCode!==null||relay.signalCode!==null);
        await page.locator('#connection.offline').waitFor();
        send('rx:'+wire('EVT,503,0,I2C_RESTORED'));
        relay=spawn('dotnet',[path.join(root,'Tools/DashboardRelay/bin/Debug/net10.0/DashboardRelay.dll')],{cwd:root,env:{...process.env,RACING_DASHBOARD_ORIGIN:'https://race.test.ts.net',RACING_DASHBOARD_OPERATORS:'operator@example.com'}});
        relay.stdout.resume();relay.stderr.resume();
        await page.locator('#serial-log').filter({hasText:'センサーとの接続が復旧'}).waitFor();
        assert.deepEqual(errors,[]);
      } finally {await browser.close();}
    });
    fs.mkdirSync(path.join(root,'Artifacts/DashboardRealtime'),{recursive:true});
    fs.writeFileSync(path.join(root,'Artifacts/DashboardRealtime/integration.json'),JSON.stringify({passed:true,source:'production bridge and serial journal / scene and graphics stubs',network:'loopback with simulated Serve headers',tests:process.env.RACING_BROWSER_TEST==='1'?8:7},null,2));
  } finally {
    sockets.forEach(s=>s.close());send('quit');relay.kill('SIGTERM');
    await delay(300);if(bridge.exitCode===null)bridge.kill('SIGKILL');if(relay.exitCode===null)relay.kill('SIGKILL');
    if(t.error)console.error(output);
  }
});

test('Serial Lab → real PTY receiver → production bridge → WebSocket, 100 Hz setting',
 {timeout:75000,skip:process.env.RACING_PTY_TEST!=='1'},async()=>{
  const portFile=path.join(root,'Temp/DashboardRelay/live.port');fs.rmSync(portFile,{force:true});
  const startFile=path.join(root,'Temp/DashboardRelay/live.start');fs.rmSync(startFile,{force:true});
  const simulator=spawn(path.join(root,'Builds/ESP32Simulator/esp32-simulator-cli'),['--duration','30','--rate','100','--frame','30,0.7,0.3,-15,0.25,0.8','--port-file',portFile,'--start-file',startFile],{cwd:root});
  let simulatorOutput='';simulator.stdout.on('data',data=>simulatorOutput+=data);simulator.stderr.on('data',data=>simulatorOutput+=data);
  let bridge,relay,socket,output='';
  try{
    await waitFor(()=>fs.existsSync(portFile));const port=fs.readFileSync(portFile,'utf8').trim();assert.match(port,/^\/dev\/ttys\d+$/);
    bridge=spawn('mono',[path.join(root,'Temp/DashboardRelay/BridgeHarness.exe'),path.join(root,'Assets/StreamingAssets'),port],{cwd:root});
    bridge.stdout.on('data',data=>output+=data);bridge.stderr.on('data',data=>output+=data);
    relay=spawn('dotnet',[path.join(root,'Tools/DashboardRelay/bin/Debug/net10.0/DashboardRelay.dll')],{cwd:root});
    relay.stdout.on('data',data=>output+='relay: '+data);relay.stderr.on('data',data=>output+='relay: '+data);
    await waitFor(async()=>{
      if(bridge.exitCode!==null||relay.exitCode!==null)throw new Error('Startup failed: '+output);
      try{return output.includes('BRIDGE_READY')&&(await request('/api/connection')).status===200;}catch{return false;}
    },20000);
    socket=await connect();fs.writeFileSync(startFile,'start');
    await waitFor(()=>socket.packets.some(packet=>packet.snapshot?.players.every(player=>player.connected)));
    const delays=[];let processed=0;
    const start=Date.now();
    while(Date.now()-start<15000){
      for(const packet of socket.packets.slice(processed))if(packet.telemetry)delays.push(Date.now()-packet.telemetry.receivedAt);
      processed=socket.packets.length;
      await delay(10);
    }
    const latest=socket.packets.filter(packet=>packet.snapshot).at(-1).snapshot;
    assert.ok(latest.serial.received>1000,'Expected sustained serial traffic');
    assert.ok(Math.abs(latest.players[0].pedal-.4)<.001);
    assert.ok(Math.abs(latest.players[1].pedal+.55)<.001);
    const lines=socket.packets.flatMap(packet=>packet.lines);
    assert.ok(lines.some(line=>line.direction==='TX'&&line.line.includes(',OPEN_SESSION,')));
    assert.ok(lines.some(line=>line.direction==='RX'&&line.line.startsWith('@ACK,')));
    assert.equal(lines.filter(line=>line.line.startsWith('@TEL,')).length,0);
    assert.ok(delays.length>100);delays.sort((a,b)=>a-b);
    const metrics={passed:true,transport:'Swift simulator → PTY → production receiver/bridge → relay → WebSocket',
      rateSettingHz:100,durationSeconds:15,serialReceived:latest.serial.received,latestSamples:delays.length,
      loopbackReceiveToClientMs:{median:delays[Math.floor(delays.length*.5)],p95:delays[Math.floor(delays.length*.95)],max:delays.at(-1)},
      note:'TEL is intentionally coalesced into latest values. Same-PC UTC timestamps; no WAN latency claim.'};
    fs.writeFileSync(path.join(root,'Artifacts/DashboardRealtime/pty-websocket.json'),JSON.stringify(metrics,null,2));
    console.log(JSON.stringify(metrics));
  }finally{
    socket?.close();bridge?.stdin.write('quit\n');relay?.kill('SIGTERM');simulator.kill('SIGTERM');
    await delay(300);if(bridge&&bridge.exitCode===null)bridge.kill('SIGKILL');if(relay&&relay.exitCode===null)relay.kill('SIGKILL');
    console.log(simulatorOutput.trim());
    if(!fs.existsSync(startFile))console.error(output);
  }
});
