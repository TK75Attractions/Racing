// Browser regression/preview using sample data; never connects to a real Unity or Firebase instance.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const { readFileSync, mkdirSync } = require('node:fs');
const { resolve } = require('node:path');
const dashboard = resolve(__dirname, '../../Assets/StreamingAssets/Dashboard');
const output = resolve(__dirname, '../../Artifacts/DashboardSerial');
function wire(body) {
  let crc=0xFFFF;
  for(const c of body){crc^=c.charCodeAt(0)<<8;for(let i=0;i<8;i++)crc=((crc&0x8000)?(crc<<1)^0x1021:crc<<1)&65535;}
  return '@'+body+'*'+crc.toString(16).toUpperCase().padStart(4,'0');
}
const snapshot = {sessionId:'preview',stateRevision:1,serverTime:Date.now(),state:'CarSelection',countdown:3,raceTime:42.6,goalLap:2,inputMode:'Serial P1/P2',players:[
  {number:1,connected:true,pedal:0.75,steering:-1.92,lap:1,speed:68},
  {number:2,connected:true,pedal:-0.4,steering:3.85,lap:1,speed:42}],participants:[],participantCount:0,
  serial:{portOpen:true,port:'/dev/cu.SLAB_USBtoUART',received:8420,processed:8418,errors:1,lastResult:'OK',lines:[
    {id:1,time:'12:34:51.120',status:'ERROR',line:wire('ERR,10,8100,ERROR,I2C,CONNECTION_LOST,0x68')},
    {id:2,time:'12:34:51.125',status:'EVENT',line:wire('EVT,11,8105,STATE,RECOVERING')},
    {id:3,time:'12:34:52.330',status:'EVENT',line:wire('EVT,12,8210,I2C_RESTORED')},
    {id:4,time:'12:34:52.335',status:'EVENT',line:wire('EVT,13,8215,STATE,READY')},
    {id:5,time:'12:34:52.350',status:'OK',line:wire('TEL,14,8230,-12.50,0.750,0.000,25.00,0.000,0.400')} ]}};
snapshot.telemetry=snapshot.serial.lines.at(-1);
snapshot.rawLines=snapshot.serial.lines.map(entry=>({...entry,direction:'RX',line:entry.line+'\r\n'}));
snapshot.playerProgress=[1,2].map(number=>({number,state:'CarSelection',carName:'SPORT CAR',step:'Vehicle',remaining:40}));
snapshot.hardware={sessionOpen:true,recent:true,state:'READY',ads:'Ok',imu:'Ok',angleLocked:true,calibrated:true,generation:1,
  inputFresh:true,invalidTelemetry:false,telemetryEnabled:true,
  imus:[{address:'0x68',identity:'0x68',status:'Ok'},{address:'0x69',identity:'0x68',status:'Ok'}],
  pedals:[{number:1,accel:.75,brake:0},{number:2,accel:0,brake:.4}],commands:[]};
(async()=>{
  mkdirSync(output,{recursive:true});
  const browser=await chromium.launch({headless:true,channel:process.env.PLAYWRIGHT_CHANNEL || 'chrome'});
  try{
    const page=await browser.newPage({viewport:{width:1440,height:1100},deviceScaleFactor:1});
    const errors=[];page.on('pageerror',error=>errors.push(error.message));
    await page.route('**/*',route=>{
      const path=new URL(route.request().url()).pathname;
      if(path==='/api/status')return route.fulfill({json:snapshot});
      if(path==='/api/action/tutorial') {
        snapshot.state='Tutorial';snapshot.playerProgress.forEach(player=>{player.state='Tutorial';player.step='Accelerate';});
        return route.fulfill({json:{ok:true}});
      }
      if(path==='/api/action/countdown') {snapshot.state='Countdown';snapshot.playerProgress.forEach(player=>{player.state='Countdown';player.step='';});return route.fulfill({json:{ok:true}});}
      if(path==='/api/action/serial') {
        const {command}=route.request().postDataJSON();
        snapshot.hardware.commands=[{sequence:8,command,status:'OK',data:['8',command,'OK','READY','1','1','1','1']}];
        return route.fulfill({json:{ok:true,serialSequence:8}});
      }
      if(path.startsWith('/api/'))return route.fulfill({status:404,body:''});
      const name=path==='/'?'index.html':path.slice(1);
      if(!['index.html','app.js','style.css','favicon.svg'].includes(name))return route.abort();
      return route.fulfill({body:readFileSync(resolve(dashboard,name)),contentType:name.endsWith('.js')?'text/javascript':name.endsWith('.css')?'text/css':name.endsWith('.svg')?'image/svg+xml':'text/html'});
    });
    await page.goto('http://127.0.0.1:8765/');
    await page.locator('#latest-tel').filter({hasText:'75.0%'}).waitFor();
    assert.equal(await page.locator('.steering-wheel g').nth(0).getAttribute('transform'),'rotate(-12.5 64 64)');
    assert.equal(await page.locator('.steering-wheel g').nth(1).getAttribute('transform'),'rotate(25 64 64)');
    assert.equal(await page.locator('#serial-alerts').isVisible(),true);
    assert.match(await page.locator('#serial-alert-list').innerText(),/I2C通信で接続が失われました/);
    await page.locator('[data-action="tutorial"]').click();
    await page.locator('[data-action="countdown"]:enabled').waitFor();
    await page.locator('[data-action="countdown"]').click();
    await page.locator('#game-state').filter({hasText:'カウントダウン'}).waitFor();
    assert.equal(await page.locator('.hardware-card').count(),6);
    assert.match(await page.locator('#hardware-grid').innerText(),/P1 ペダル\s*動作確認済み/);
    await page.click('#serial-send');
    await page.locator('#serial-command-badge').filter({hasText:'完了'}).waitFor();
    assert.match(await page.locator('#serial-command-values').innerText(),/ハンドル基準\s*設定済み/);
    // iPad landscape: devices share one row and the toolbar does not stretch the cameras.
    for(const viewport of [{width:1024,height:768},{width:1180,height:820},{width:1366,height:1024}]) {
      await page.setViewportSize(viewport);
      const layout=await page.evaluate(()=>{
        const box=selector=>document.querySelector(selector).getBoundingClientRect().toJSON();
        return {hardware:box('.hardware'),commands:box('.serial-controls'),flow:box('.controls'),cameras:box('.cameras'),
          width:document.documentElement.scrollWidth,viewport:innerWidth};
      });
      assert.equal(layout.hardware.top,layout.commands.top,'iPad device panels must be side by side');
      assert.ok(layout.commands.left>layout.hardware.left);
      await page.locator('.controls').screenshot({path:resolve(output,`toolbar-${viewport.width}.png`)});
      assert.ok(layout.flow.height<185,'Compact progression toolbar');
      assert.ok(layout.width<=layout.viewport,'iPad horizontal overflow');
      await page.screenshot({path:resolve(output,`overview-${viewport.width}.png`),fullPage:true});
    }
    assert.equal(await page.locator('#raw-dialog').isVisible(),false);
    assert.equal(await page.locator('#raw-log .raw-row').count(),0,'Closed dialog defers rendering');
    await page.click('#raw-open');
    assert.equal(await page.locator('#raw-dialog').isVisible(),true);
    assert.equal(await page.locator('#raw-log .raw-row').count(),5);
    assert.equal(await page.locator('#raw-log .raw-row code').first().textContent(),snapshot.rawLines[0].line);
    assert.equal(await page.locator('#raw-close').evaluate(node=>node===document.activeElement),true);
    for(let index=0;index<4;index++) {
      await page.keyboard.press('Tab');
      // Native dialogs can return focus to browser chrome (BODY), but background controls remain inert.
      assert.equal(await page.locator('#raw-dialog').evaluate(node=>node.contains(document.activeElement)||document.activeElement===document.body),true,'Modal blocks background controls');
    }
    await page.click('#raw-pause');
    snapshot.rawLines.push({id:6,time:'12:35:00',direction:'TX',line:'<img src=x onerror=alert(1)>\r\n'});
    await page.locator('#raw-status').filter({hasText:'6 件'}).waitFor();
    assert.equal(await page.locator('#raw-log .raw-row').count(),5);
    await page.click('#raw-pause');assert.equal(await page.locator('#raw-log .raw-row').count(),6);
    assert.equal(await page.locator('#raw-log img').count(),0);
    await page.locator('#raw-dialog').screenshot({path:resolve(output,'raw-dialog-ipad.png')});
    await page.keyboard.press('Escape');assert.equal(await page.locator('#raw-dialog').isVisible(),false);
    assert.equal(await page.locator('#raw-open').evaluate(node=>node===document.activeElement),true);
    await page.click('#raw-open');await page.click('#raw-close');
    await page.click('#raw-open');await page.mouse.click(5,5);
    assert.equal(await page.locator('#raw-dialog').isVisible(),false,'Backdrop dismisses modal');
    assert.equal(await page.locator('#raw-dialog').isVisible(),false);
    await page.setViewportSize({width:1440,height:1100});
    await page.locator('.hardware').screenshot({path:resolve(output,'hardware-desktop.png')});
    await page.locator('.serial-controls').screenshot({path:resolve(output,'command-desktop.png')});
    await page.locator('.serial').screenshot({path:resolve(output,'desktop.png')});
    await page.setViewportSize({width:390,height:844});
    await page.locator('.hardware').screenshot({path:resolve(output,'hardware-mobile.png')});
    await page.locator('.serial-controls').screenshot({path:resolve(output,'command-mobile.png')});
    await page.locator('.controls').screenshot({path:resolve(output,'flow-mobile.png')});
    await page.locator('.serial').screenshot({path:resolve(output,'mobile.png')});
    await page.click('#raw-open');
    assert.ok(await page.locator('#raw-dialog').evaluate(node=>node.scrollWidth<=node.clientWidth),'Mobile dialog overflow');
    await page.locator('#raw-dialog').screenshot({path:resolve(output,'raw-dialog-mobile.png')});
    await page.click('#raw-close');
    await page.locator('#serial-alerts').waitFor({state:'hidden',timeout:15000});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'Mobile horizontal overflow');
    assert.deepEqual(errors,[],'Browser script errors');
    console.log('Browser passed: iPad landscape pairing, compact toolbar, modal open/close/Escape/focus, raw pause/resume, safe text, preparation transitions, two IMUs, pedals, inline command results, ten-second alert recovery, desktop/mobile layout, no JS errors.');
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
