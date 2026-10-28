const {spawn}=require('node:child_process');
const fs=require('node:fs');const path=require('node:path');const assert=require('node:assert/strict');
const {chromium}=require('playwright');
const root=path.resolve(process.argv[2]);const out=path.resolve(process.argv[3]);fs.mkdirSync(out,{recursive:true});
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const bridge=spawn('mono',[root+'/Temp/DashboardRelay/BridgeHarness.exe',root+'/Assets/StreamingAssets'],{cwd:root});
const relay=spawn('dotnet',[root+'/Tools/DashboardRelay/bin/Debug/net10.0/DashboardRelay.dll'],{cwd:root,env:{...process.env,RACING_PUSH_DATA:root+'/Temp/browser-push',RACING_DASHBOARD_ASSETS:root+'/Assets/StreamingAssets/Dashboard'}});
let output='';for(const child of [bridge,relay]){child.stdout.on('data',d=>output+=d);child.stderr.on('data',d=>output+=d);}
(async()=>{let browser;try{
 for(let i=0;i<150;i++){try{const response=await fetch('http://127.0.0.1:18766/api/status');if(response.ok)break;}catch{}if(i===149)throw new Error(output);await delay(100);}
 browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:18766/');await page.locator('#maintenance-start:enabled').waitFor();
 await page.locator('#maintenance-start').click();await page.locator('#maintenance-status').filter({hasText:'メンテナンス中'}).waitFor();
 for(const route of ['home','race','devices','settings','remote','diagnostics']){
  await page.locator('.nav-item[data-page-link="'+route+'"]').click();await assert.doesNotReject(()=>page.locator('#maintenance-stop:visible:enabled').waitFor());
 }
 await page.screenshot({path:path.join(out,'dashboard-desktop.png'),fullPage:true});
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:path.join(out,'dashboard-mobile.png'),fullPage:true});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,'No mobile horizontal overflow');
 await page.locator('#maintenance-stop').click();await page.locator('#maintenance-status').filter({hasText:'通常表示'}).waitFor();
 await page.locator('#maintenance-start:enabled').click();await page.locator('#maintenance-status').filter({hasText:'メンテナンス中'}).waitFor();
 bridge.stdin.write('quit\n');await page.locator('#maintenance-stop:disabled').waitFor();
 assert.match(await page.locator('#maintenance-status').innerText(),/最後の確認/);assert.deepEqual(errors,[]);
 fs.writeFileSync(path.join(out,'browser-result.json'),JSON.stringify({passed:true,routes:6,mobileWidth:390,noHorizontalOverflow:true,pageErrors:errors},null,2));console.log('MAINTENANCE_BROWSER_PASS: six routes, actual toggle/clear, desktop/mobile, offline indication, no page errors.');
}finally{await browser?.close();if(bridge.exitCode===null)bridge.kill('SIGTERM');if(relay.exitCode===null)relay.kill('SIGTERM');}})().catch(e=>{console.error(e);process.exitCode=1;});
