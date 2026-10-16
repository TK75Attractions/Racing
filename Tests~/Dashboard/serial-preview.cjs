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
const snapshot = {state:'Game',raceTime:42.6,goalLap:2,inputMode:'Serial P1/P2',players:[
  {number:1,connected:true,pedal:0.75,steering:-1.92,lap:1,speed:68},
  {number:2,connected:true,pedal:-0.4,steering:3.85,lap:1,speed:42}],participants:[],participantCount:0,
  serial:{portOpen:true,port:'/dev/cu.SLAB_USBtoUART',received:8420,processed:8418,errors:1,lastResult:'OK',lines:[
    {id:1,time:'12:34:51.120',status:'ERROR',line:wire('ERR,10,8100,ERROR,I2C,CONNECTION_LOST,0x68')},
    {id:2,time:'12:34:51.125',status:'EVENT',line:wire('EVT,11,8105,STATE,RECOVERING')},
    {id:3,time:'12:34:52.330',status:'EVENT',line:wire('EVT,12,8210,I2C_RESTORED')},
    {id:4,time:'12:34:52.335',status:'EVENT',line:wire('EVT,13,8215,STATE,READY')},
    {id:5,time:'12:34:52.350',status:'OK',line:wire('TEL,14,8230,-12.50,0.750,0.000,25.00,0.000,0.400')} ]}};
snapshot.telemetry=snapshot.serial.lines.at(-1);
(async()=>{
  mkdirSync(output,{recursive:true});
  const browser=await chromium.launch({headless:true,channel:process.env.PLAYWRIGHT_CHANNEL || 'chrome'});
  try{
    const page=await browser.newPage({viewport:{width:1440,height:1100},deviceScaleFactor:1});
    const errors=[];page.on('pageerror',error=>errors.push(error.message));
    await page.route('**/*',route=>{
      const path=new URL(route.request().url()).pathname;
      if(path==='/api/status')return route.fulfill({json:snapshot});
      if(path.startsWith('/api/'))return route.fulfill({status:404,body:''});
      const name=path==='/'?'index.html':path.slice(1);
      if(!['index.html','app.js','style.css','favicon.svg'].includes(name))return route.abort();
      return route.fulfill({body:readFileSync(resolve(dashboard,name)),contentType:name.endsWith('.js')?'text/javascript':name.endsWith('.css')?'text/css':name.endsWith('.svg')?'image/svg+xml':'text/html'});
    });
    await page.goto('http://127.0.0.1:8765/');
    await page.locator('#latest-tel').filter({hasText:'ACCEL 75.0%'}).waitFor();
    assert.equal(await page.locator('#serial-alerts').isVisible(),true);
    assert.match(await page.locator('#serial-alert-list').innerText(),/I2C通信で接続が失われました/);
    await page.locator('.serial').screenshot({path:resolve(output,'desktop.png')});
    await page.setViewportSize({width:390,height:844});
    await page.locator('.serial').screenshot({path:resolve(output,'mobile.png')});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'Mobile horizontal overflow');
    assert.deepEqual(errors,[],'Browser script errors');
    console.log('Browser passed: Japanese explanations, persistent alerts, desktop/mobile layout, no JS errors.');
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
