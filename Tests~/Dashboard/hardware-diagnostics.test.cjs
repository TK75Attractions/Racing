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
