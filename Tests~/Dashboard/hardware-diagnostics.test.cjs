const {test}=require('node:test');
const assert=require('node:assert/strict');
const {analyze}=require('../../Assets/StreamingAssets/Dashboard/diagnostics.js');
const good={settings:{inputMode:'Serial'},serial:{portOpen:true,port:'/dev/test'},hardware:{sessionOpen:true,recent:true,state:'READY',calibrated:true,angleLocked:true,ads:'Ok',adsAddress:'0x48',imus:[{status:'Ok',address:'0x68'},{status:'Ok',address:'0x69'}],telemetryEnabled:true,inputFresh:true,pedals:[{number:1,accel:0,brake:0}]}};
const text=x=>JSON.stringify(x);
test('Calibration and angle reference are evidence, not guessed wiring failures',()=>{
 const result=analyze({...good,hardware:{...good.hardware,calibrated:false,angleLocked:false}},1);
 assert.match(text(result.causes),/キャリブレーションが未完了/);assert.match(text(result.causes),/基準が未設定/);
 assert.doesNotMatch(text(result.causes),/配線/);
});
test('Missing individual address identifies its branch; simultaneous faults suggest shared bus',()=>{
 let result=analyze({...good,hardware:{...good.hardware,imus:[{status:'Fault',address:'0x68'}]}},1,['0x48','0x69']);
 assert.match(text(result.causes),/登録アドレス 0x68.*応答していません/);assert.match(text(result.checks),/VCC\/GND\/SDA\/SCL/);
 result=analyze({...good,hardware:{...good.hardware,ads:'Fault',imus:[{status:'Fault',address:'0x68'}]}},1,[]);
 assert.match(text(result.causes),/共通の電源/);
});
test('Stale data and zero pedals never become a made-up broken wire diagnosis',()=>{
 const stale=analyze({...good,hardware:{...good.hardware,recent:false,ads:'Fault'}},3);
 assert.match(text(stale.facts),/新しい応答を確認できません/);assert.doesNotMatch(text(stale.causes),/ADS1115/);
 const zero=analyze(good,4);assert.doesNotMatch(text(zero.causes),/断線/);assert.match(text(zero.checks),/0%.*断線と判定できません/);
});
test('Stopped telemetry is distinguished from a hardware fault and input branch is scoped to player',()=>{
 assert.match(text(analyze({...good,hardware:{...good.hardware,telemetryEnabled:false}},4).causes),/TEL配信が停止/);
 const missing=analyze({...good,hardware:{...good.hardware,inputFresh:false}},5);
 assert.match(text(missing.checks),/P2ペダル/);assert.match(text(missing.checks),/物理端子や断線箇所を特定できません/);
});

test('P1 wheel-only mode does not diagnose deliberately absent pedals or P2 as faults',()=>{
 const only={...good,settings:{inputMode:'mixed',steeringMode:'ConstantPedal'},hardware:{...good.hardware,inputMode:'P1_STEERING',ads:'Fault',imus:[{status:'Ok'},{status:'Fault'}]}};
 for(const key of [2,3,4,5]){const result=analyze(only,key);assert.match(text(result.facts),/使用しません/);assert.equal(result.causes.length,0);}
});

test('Fully automated matches do not require physical sensors',()=>{
 const automatic={...good,settings:{inputMode:'serial',bothPlayersAutodrive:true},serial:{portOpen:false}};
 for(let key=0;key<6;key++)assert.equal(analyze(automatic,key).causes.length,0);
});

test('Visual cards distinguish observed failures from wiring hypotheses',()=>{
 const broken={...good,hardware:{...good.hardware,calibrated:false,imus:[{status:'Fault',address:'0x68'},{status:'Ok',address:'0x69'}]}};
 const result=analyze(broken,1,['0x48','0x69']);
 assert.equal(result.summary.level,'fault');
 assert.equal(result.issues[0].id,'address');assert.equal(result.issues[0].confirmed,true);
 assert.equal(result.issues.find(x=>x.id==='sensor').confirmed,false);
 assert.equal(result.issues.find(x=>x.id==='calibration').confirmed,true);
 assert.equal(result.nodes[1].level,'fault');assert.equal(result.nodes[1].selected,true);
 assert.equal(result.nodes[2].level,'ok');assert.equal(result.sharedBusSuspect,false);
 assert.ok(result.readings.some(x=>x.label==='今回のI2C検索'&&x.level==='fault'));
 for(const issue of result.issues){assert.ok(issue.title.length<=24);assert.ok(issue.next);}
});
test('The schematic never presents stale or unpowered downstream devices as failed',()=>{
 const old={...good,hardware:{...good.hardware,recent:false,ads:'Fault',imus:[{status:'Fault'}]}};
 for(const snapshot of [null,old,{...old,serial:{portOpen:false}}]){
  const result=analyze(snapshot,1,[]);
  assert.ok(result.nodes.slice(1).every(node=>node.level==='unknown'));
  assert.equal(result.sharedBusSuspect,false);
 }
 const only={...good,settings:{inputMode:'mixed'},hardware:{...good.hardware,ads:'Fault',imus:[{status:'Fault'},{status:'Fault'}]}};
 const result=analyze(only,1);
 assert.equal(result.sharedBusSuspect,false);
 for(const key of [2,3,4,5])assert.equal(result.nodes[key].level,'unused');
});
test('Calibration, stopped TEL and zero input have clear non-wiring states',()=>{
 const setup=analyze({...good,hardware:{...good.hardware,calibrated:false,angleLocked:false}},1);
 assert.equal(setup.summary.level,'warn');assert.ok(setup.nodes.slice(0,5).every(node=>node.level==='ok'));
 const stopped=analyze({...good,hardware:{...good.hardware,telemetryEnabled:false,inputFresh:false}},4);
 assert.equal(stopped.issues.find(x=>x.id==='telemetry').confirmed,true);
 assert.equal(stopped.nodes[4].level,'unknown');assert.equal(stopped.nodes[3].level,'ok');
 const zero=analyze(good,4);assert.equal(zero.issues.length,0);assert.equal(zero.summary.level,'ok');
 // USB diagnosis must not turn an absent ADS address into a fault in the USB cable.
 assert.equal(analyze(good,0,[]).issues.length,0);
});
test('A missing player input or an invalid pedal value never appears green',()=>{
 const missing=analyze(good,5);assert.equal(missing.nodes[5].level,'unknown');
 assert.ok(missing.issues.some(x=>x.id==='input'));assert.notEqual(missing.summary.level,'ok');
 const invalid=analyze({...good,hardware:{...good.hardware,pedals:[{number:1,accel:1.5,brake:0}]}},4);
 assert.equal(invalid.nodes[4].level,'fault');assert.equal(invalid.summary.level,'fault');
});
