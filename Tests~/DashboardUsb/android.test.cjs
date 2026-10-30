const {test}=require('node:test');
const assert=require('node:assert/strict');
const {mkdtempSync,writeFileSync,readFileSync,rmSync}=require('node:fs');
const {tmpdir}=require('node:os');
const {join,resolve}=require('node:path');
const {spawnSync}=require('node:child_process');
const script=resolve(__dirname,'../../Tools/DashboardUsb/Start Android USB Dashboard.command');
function run(mode){
 const folder=mkdtempSync(join(tmpdir(),'race-usb-adb-')),adb=join(folder,'fake-adb'),log=join(folder,'calls');
 writeFileSync(adb,`#!/usr/bin/env node
const fs=require('node:fs'),a=process.argv.slice(2),mode=process.env.FAKE_ADB_MODE;
fs.appendFileSync(process.env.FAKE_ADB_LOG,JSON.stringify(a)+'\\n');
if(a[0]==='-d'&&a[1]==='get-serialno'){if(mode==='multiple')process.exit(1);console.log('USB_TEST_DEVICE');}
else if(a[0]==='-d'&&a[1]==='get-state')console.log(mode==='unauthorized'?'unauthorized':'device');
else if(a[0]==='-s'&&a[1]==='USB_TEST_DEVICE'&&a[2]==='reverse'&&a[3]==='--list'){if(mode==='existing')console.log('UsbFfs tcp:8766 tcp:8766');if(mode==='conflict')console.log('UsbFfs tcp:8766 tcp:1234');}
else if(a[0]==='-s'&&a[1]==='USB_TEST_DEVICE'&&a[2]==='reverse'&&a[3]==='--no-rebind'){if(mode==='conflict')process.exit(1);}
else process.exit(2);
`,{mode:0o700});
 try{const result=spawnSync('/bin/sh',[script],{encoding:'utf8',timeout:5000,env:{...process.env,RACING_ADB:adb,FAKE_ADB_MODE:mode,FAKE_ADB_LOG:log}});return{...result,calls:readFileSync(log,'utf8').trim().split('\n').map(JSON.parse)};}
 finally{rmSync(folder,{recursive:true,force:true});}
}
test('Android selects physical USB then forwards only that fixed device, without rebinding',()=>{
 const result=run('new');assert.equal(result.status,0,result.stderr);assert.match(result.stdout,/http:\/\/127.0.0.1:8766/);
 assert.deepEqual(result.calls,[['-d','get-serialno'],['-d','get-state'],['-s','USB_TEST_DEVICE','reverse','--list'],['-s','USB_TEST_DEVICE','reverse','--no-rebind','tcp:8766','tcp:8766']]);
});
test('Android reuses an identical mapping without changing or removing it',()=>{const result=run('existing');assert.equal(result.status,0);assert.equal(result.calls.length,3);});
test('Android leaves conflicting mappings intact',()=>{const result=run('conflict');assert.notEqual(result.status,0);assert.ok(result.calls.every(call=>!call.includes('--remove')&&!call.includes('--remove-all')));});
test('Android requires the user to authorize one USB device',()=>{for(const mode of ['unauthorized','multiple']){const result=run(mode);assert.notEqual(result.status,0);assert.match(result.stdout,/USBデバッグ/);assert.ok(result.calls.every(call=>!call.includes('reverse')));}});
