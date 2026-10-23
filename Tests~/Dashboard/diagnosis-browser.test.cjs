const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const assets=path.join(root,'Assets/StreamingAssets/Dashboard');
const good={state:'Title',goalLap:2,players:[],settings:{inputMode:'serial'},
 serial:{portOpen:true,port:'/dev/test',lines:[]},
 hardware:{sessionOpen:true,recent:true,state:'READY',calibrated:true,angleLocked:true,
 ads:'Ok',adsAddress:'0x48',imus:[{status:'Ok',address:'0x68'},{status:'Ok',address:'0x69'}],
 telemetryEnabled:true,inputFresh:true,pedals:[{number:1,accel:0,brake:0},{number:2,accel:0,brake:0}]}};

test('Diagnosis browser: fault, setup, stale, unused and pedal views remain readable',
 {skip:process.env.RACING_BROWSER_TEST!=='1'},async()=>{
 const {chromium}=require('playwright');
 const browser=await chromium.launch({headless:true,channel:'chrome'});
 const output=process.env.RACING_DIAGNOSIS_ARTIFACTS||path.join(root,'Artifacts/HardwareDiagnosis');
 fs.mkdirSync(output,{recursive:true});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1100}}),errors=[],actions=[];
  page.on('pageerror',error=>errors.push(error.message));
  // Real HTML/CSS/renderers, deterministic snapshots; transport integration is tested in DashboardRelay.
  await page.route('**/*',async route=>{
   const url=new URL(route.request().url());
   if(url.origin!=='http://diagnosis.test')return route.abort();
   if(url.pathname.startsWith('/api/')){actions.push(url.pathname);return route.fulfill({status:503,body:'fixture'});}
   if(url.pathname==='/live.js')return route.fulfill({contentType:'text/javascript',body:'export async function startLive() {}'});
   if(url.pathname==='/notifications.js')return route.fulfill({contentType:'text/javascript',body:''});
   const name=url.pathname==='/'?'index.html':path.basename(url.pathname);
   const types={html:'text/html',js:'text/javascript',css:'text/css',svg:'image/svg+xml',png:'image/png',webmanifest:'application/manifest+json'};
   const file=path.join(assets,name);if(!fs.existsSync(file))return route.fulfill({status:404,body:''});
   return route.fulfill({contentType:types[name.split('.').pop()],body:fs.readFileSync(file)});
  });
  await page.goto('http://diagnosis.test/');
  async function show(snapshot,key,scan=null){
   await page.evaluate(({snapshot,key,scan})=>{
    document.getElementById('diagnostics-dialog').close();
    if(snapshot)render(snapshot,{canControl:false});else latestSnapshot=null;
    openHardwareDiagnosis(key);
    diagnosticScan=scan;renderHardwareDiagnosis();
   },{snapshot,key,scan});
  }
  const fault={...good,hardware:{...good.hardware,ads:'Fault',imus:[{status:'Fault',address:'0x68'},good.hardware.imus[1]]}};
  const setup={...good,hardware:{...good.hardware,calibrated:false,angleLocked:false}};
  const cases=[
   ['shared-bus',fault,1,['0x69']],['setup',setup,1],['usb',{...good,serial:{portOpen:false,lines:[]}},0],
   ['stale',{...fault,hardware:{...fault.hardware,recent:false}},1],['offline',null,1],
   ['unused',{...good,settings:{inputMode:'mixed',steeringMode:'ConstantPedal'}},4],
   ['pedal',{...good,hardware:{...good.hardware,telemetryEnabled:false,inputFresh:false}},5],['zero',good,4]
  ];
  for(const viewport of [{width:1440,height:1100},{width:390,height:844},{width:320,height:700},{width:844,height:390}]){
   await page.setViewportSize(viewport);
   for(const [name,snapshot,key,scan]of cases){
    await show(snapshot,key,scan);
    assert.equal(await page.locator('.diagnosis-node').count(),6);
    assert.equal(await page.locator('.diagnosis-node.selected').count(),1);
    assert.equal(await page.locator('.diagnosis-cause details[open]').count(),0,'verbose explanations start collapsed');
    assert.ok(await page.locator('#diagnostics-summary strong').textContent());
    assert.ok(await page.evaluate(()=>{
     const dialog=document.getElementById('diagnostics-dialog');
     return dialog.scrollWidth<=dialog.clientWidth+1&&document.documentElement.scrollWidth<=innerWidth;
    }),`${name}: no horizontal overflow at ${viewport.width}px`);
    if(name==='stale'||name==='offline')assert.equal(await page.locator('.diagnosis-node.fault').count(),0);
    if(name==='unused')assert.equal(await page.locator('.diagnosis-node.selected.unused').count(),1);
    if(name==='setup')assert.equal(await page.locator('.diagnosis-certainty.observed').count(),2);
    if(name==='shared-bus'){
     assert.equal(await page.locator('.diagnosis-bus.suspect').count(),1);
     assert.equal(await page.locator('.diagnosis-certainty.candidate').count(),1);
     assert.equal(await page.locator('.diagnosis-certainty.observed').count(),1);
     if(viewport.width===1440||viewport.width===390){
      await page.locator('#diagnostics-dialog').screenshot({path:path.join(output,`diagnosis-${viewport.width===1440?'desktop':'mobile'}.png`)});
      if(viewport.width===390)await page.locator('.diagnosis-map').screenshot({path:path.join(output,'diagnosis-mobile-map.png')});
     }
     const explanation=page.locator('.diagnosis-cause details').first();
     await explanation.locator('summary').click();assert.equal(await explanation.getAttribute('open'),'');
     await page.evaluate(()=>renderHardwareDiagnosis());
     assert.equal(await page.locator('.diagnosis-cause details[open]').count(),1,'refresh preserves expanded explanation');
    }
    await page.locator('#diagnostics-details summary').click();assert.ok(await page.locator('#diagnostics-facts').isVisible());
    await page.locator('#diagnostics-close').click();assert.equal(await page.locator('#diagnostics-dialog').isVisible(),false);
   }
  }
  await show(fault,1,['0x69']);await page.evaluate(()=>setConnection(false));
  assert.match(await page.locator('#diagnostics-summary').textContent(),/Unityの接続待ち/);
  assert.equal(await page.locator('.diagnosis-node.fault').count(),0,'disconnect clears old fault colours in an open dialog');
  await show(setup,1);await page.keyboard.press('Escape');assert.equal(await page.locator('#diagnostics-dialog').isVisible(),false);
  assert.deepEqual(errors,[]);assert.deepEqual(actions,[],'read-only viewing never starts a device operation');
 }finally{await browser.close();}
});
