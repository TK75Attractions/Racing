const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');
const dashboard = resolve(__dirname, '../../Assets/StreamingAssets/Dashboard');
const html = readFileSync(resolve(dashboard, 'index.html'), 'utf8');
const script = readFileSync(resolve(dashboard, 'app.js'), 'utf8');
class Element {
  children = []; hidden = false; dataset = {}; scrollTop = 0; scrollHeight = 0; clientHeight = 0;
  classList = {toggle(){}, add(){}, remove(){}};
  set textContent(value) { this.children = [String(value)]; }
  get textContent() { return this.children.map(child => typeof child === 'string' ? child : child.textContent).join(''); }
  set innerHTML(value) { this.children = [value]; }
  setAttribute(name,value) { this[name] = value; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  querySelector() { return this.span ||= new Element(); }
}
function fixture() {
  const elements = new Map([...html.matchAll(/id="([^"]+)"/g)].map(match=>[match[1],new Element()]));
  const buttons = [...html.matchAll(/data-action="([^"]+)"/g)].map(match=>Object.assign(new Element(),{dataset:{action:match[1]}}));
  const settingControls = [...html.matchAll(/<(input|select|button) id="([^"]+)"[^>]*data-setting="([^"]+)"([^>]*)>/g)].map(match => {
    const control = elements.get(match[2]); control.dataset.setting = match[3];
    control.dataset.settingArg = /data-setting-arg="([^"]+)"/.exec(match[4])?.[1];
    return control;
  });
  const inputButtons = [...html.matchAll(/<button([^>]+)>/g)].map(match => {
    const button = new Element();
    for (const [, name, value] of match[1].matchAll(/data-([a-z-]+)="([^"]*)"/g))
      button.dataset[name.replace(/-([a-z])/g,(_,letter)=>letter.toUpperCase())]=value;
    return button;
  }).filter(button=>button.dataset.inputHold||button.dataset.inputPulse||button.dataset.menuAction);
  const document = { getElementById(id) { assert.ok(elements.has(id), `Missing UI element ${id}`); return elements.get(id); },
    createElement(){return new Element();}, querySelectorAll(selector){return selector==='[data-action]'?buttons:selector==='[data-setting]'?[...settingControls,...elements.get('settings-features').children.map(row=>row.children[0]).filter(child=>child?.dataset?.setting)]:selector==='[data-input-hold], [data-input-pulse]'?inputButtons.filter(button=>button.dataset.inputHold||button.dataset.inputPulse):selector==='[data-menu-action]'?inputButtons.filter(button=>button.dataset.menuAction):[];} };
  let now = 100000;
  class ClockDate extends Date { static now() { return now; } }
  const context = vm.createContext({Date:ClockDate,document,location:{origin:'http://127.0.0.1:8765'},URL:{revokeObjectURL(){}}});
  vm.runInContext(readFileSync(resolve(dashboard,'diagnostics.js'),'utf8'),context);
  // Load the real render functions without timers, network polling or UI event handlers.
  vm.runInContext(script.slice(0,script.indexOf("document.querySelectorAll('[data-action]').forEach")),context);
  const render = (snapshot,options) => context.render(snapshot,options);
  return {elements,buttons,inputButtons,context,render,advance(ms) { now += ms; }};
}
const base = {state:'Game',goalLap:2,players:[{number:1},{number:2}],serial:{lines:[]},participantCount:4,
  participants:Array.from({length:4},(_,index)=>({number:index+1,isCpu:index>=2,position:4-index,lap:1,
    status:'Driving',speed:72,targetSpeed:90,progress:100, rivalNumber:index>=2?index-1:0,gap:-20,recoveries:2}))};
test('Unity FPS and frame time update and clear on disconnect',()=>{
  const ui=fixture();
  ui.render({...base,performance:{fps:59.94,frameTimeMs:1000/59.94}});
  assert.equal(ui.elements.get('fps').textContent,'59.9');
  assert.match(ui.elements.get('frame-time').textContent,/16.7 ms \/ frame/);
  ui.render({...base,performance:{fps:24,frameTimeMs:1000/24}});
  assert.equal(ui.elements.get('fps').textContent,'24.0');
  assert.match(ui.elements.get('frame-time').textContent,/41.7 ms/);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('fps').textContent,'--');
  assert.match(ui.elements.get('frame-time').textContent,/計測待ち/);
});
test('Missing, warming up or invalid FPS never shows a stale measurement',()=>{
  const ui=fixture();
  for(const performance of [undefined,null,{}, {fps:0,frameTimeMs:0}, {fps:NaN,frameTimeMs:16.7},
      {fps:60,frameTimeMs:Infinity}, {fps:-1,frameTimeMs:-1}, {fps:'60',frameTimeMs:16.7}]){
    ui.render({...base,performance:{fps:60,frameTimeMs:16.7}});
    ui.render({...base,performance});
    assert.equal(ui.elements.get('fps').textContent,'--');
    assert.match(ui.elements.get('frame-time').textContent,/計測待ち/);
  }
});
test('Four standings include CPU diagnostics and preserve two player feeds',()=>{
  const ui=fixture();ui.render(base);
  const rows=ui.elements.get('participant-rows').children;
  assert.equal(rows.length,4);assert.match(rows[0].textContent,/CPU 2/);
  assert.match(rows[0].textContent,/目標 90 km\/h/);assert.match(rows[0].textContent,/P2-20.0 m/);
  assert.match(rows[0].textContent,/2回/);assert.equal(ui.elements.get('participant-count').textContent,'4台 / CPU 2台');
  assert.equal([...html.matchAll(/<img id="feed-/g)].length,2);
});
test('All driver modes, finish/DNF and contact protection have explicit labels',()=>{
  const ui=fixture();
  for(const [status,label] of Object.entries({Stopped:'停止',Driving:'走行中',Airborne:'空中',Reversing:'後退復旧',
    Yielding:'接触後の譲り',Stalled:'停止・復旧待ち',Finished:'完走',DNF:'未完走'})){
    ui.render({...base,participants:[{...base.participants[3],status,contactGrace:true}]});
    const text=ui.elements.get('participant-rows').textContent;
    assert.ok(text.includes(label));assert.match(text,/接触保護中/);
  }
});
test('Disconnect, round reset and old snapshots clear previous CPU rows',()=>{
  const ui=fixture();ui.render(base);ui.context.setConnection(false);
  assert.equal(ui.elements.get('participant-rows').children.length,0);
  assert.match(ui.elements.get('participant-empty').textContent,/接続を待っています/);
  ui.render({...base,state:'Title',participantCount:0,participants:[]});
  assert.equal(ui.elements.get('participant-rows').children.length,0);
  assert.match(ui.elements.get('participant-empty').textContent,/生成を待っています/);
  const old={...base};delete old.participants;delete old.participantCount;ui.render(old);
  assert.match(ui.elements.get('participant-empty').textContent,/Unity を更新/);
});
test('Remote render uses the same CPU rows and does not enable viewer controls',()=>{
  const ui=fixture();ui.render(base,{canControl:false});
  assert.equal(ui.elements.get('participant-rows').children.length,4);
  assert.ok(ui.buttons.every(button=>button.disabled));
  ui.render({...base,state:'Tutorial',participantCount:0,participants:[]},{canControl:false});
  assert.equal(ui.elements.get('game-state').textContent,'チュートリアル');
});

function wire(body) {
  // Independent expected CRC implementation for dashboard fixtures.
  let crc = 65535;
  for (const byte of Buffer.from(body, 'ascii')) {
    crc ^= byte * 256;
    for (let bit=0;bit<8;bit++) crc = crc >= 32768 ? ((crc*2)^0x1021)&65535 : (crc*2)&65535;
  }
  return '@'+body+'*'+crc.toString(16).toUpperCase().padStart(4,'0');
}
const entry = (status,body,id=1) => ({id,time:'12:34:56.789',status,line:wire(body)});
test('TEL updates latest numeric values without entering natural-language history',()=>{
  const ui=fixture();const tel=entry('OK','TEL,13,8210,-12.50,0.750,0.000,25.00,0.000,0.400');
  const event=entry('EVENT','EVT,12,8200,STATE,READY',2);
  ui.render({...base,telemetry:tel,serial:{portOpen:true,lines:[event,tel]}});
  const log=ui.elements.get('serial-log');
  assert.ok(!log.textContent.includes(tel.line));
  assert.match(log.textContent,/2人分の入力が使用可能/);
  assert.match(ui.elements.get('latest-tel').textContent,/P1 の受信入力[\s\S]*ハンドル 左 12.5°アクセル75.0%ブレーキ0.0%/);
  assert.match(ui.elements.get('latest-tel').textContent,/P2 の受信入力[\s\S]*ハンドル 右 25°アクセル0.0%ブレーキ40.0%/);
  assert.ok(!ui.elements.get('latest-tel').textContent.includes(tel.line));
  ui.render({...base,telemetry:{...tel,id:3},serial:{portOpen:true,lines:[event,tel]}});
  assert.equal(log.children.length,1);
  ui.context.setConnection(false);
  assert.match(ui.elements.get('latest-tel').textContent,/待っています/);
});
test('Invalid TEL remains in error history and outbound commands are translated',()=>{
  const ui=fixture();
  const invalid={id:1,status:'INVALID',line:'@TEL,1,0,0,1,0,0,1,0*0000'};
  const command=entry('SENT','CMD,1,0,PING',2);command.direction='TX';
  ui.render({...base,serial:{portOpen:true,lines:[invalid,command]}});
  assert.match(ui.elements.get('serial-log').textContent,/装置へ「応答確認」を送信/);
  assert.match(ui.elements.get('serial-log').textContent,/↑ ✓ 送信済み/);
  assert.match(ui.elements.get('serial-alert-list').textContent,/CRC・形式/);
});
test('SYS, state, calibration, configuration and ACK have meaningful labels',()=>{
  const ui=fixture();const explain=(status,body)=>ui.context.explainSerialEntry(entry(status,body));
  assert.match(explain('HELLO','SYS,0,0,HELLO,RGP,1.0,1.0.0').text,/RGP 1.0.*接続セッション/);
  assert.match(explain('EVENT','EVT,1,0,STATE,READY').text,/2人分の入力が使用可能/);
  assert.match(explain('EVENT','EVT,2,0,CAL_DONE,AUTO,ALL').text,/全センサーの校正を完了/);
  assert.match(explain('EVENT','EVT,3,0,CONFIG_CHANGED,TEL_RATE,50').text,/50 Hz/);
  assert.match(explain('ACK_OK','ACK,4,0,1,PING,OK,PONG').text,/応答確認.*成功/);
  assert.match(explain('ACK_OK','ACK,5,0,2,GET_STATUS,OK,READY,1,1,1,1').text,/校正完了/);
  const future=explain('ACK_FAILED','ACK,6,0,3,PING,FUTURE_STATUS');
  assert.equal(future.level,'error');assert.match(future.text,/未知の応答/);
  assert.equal(explain('SYS','SYS,7,0,FUTURE_KEY,X').level,'info');
  assert.match(explain('UNMATCHED_ACK','ACK,8,0,99,PING,OK,PONG').text,/状態には反映しません/);
});
test('Errors, warnings, failed ACK and timeouts appear separately without losing raw logs',()=>{
  const ui=fixture();const lines=[entry('ERROR','ERR,1,0,ERROR,I2C,CONNECTION_LOST,0x68'),
    entry('WARN','ERR,2,0,WARN,HANDLE,ANGLE_REFERENCE_INVALID',2),entry('ACK_FAILED','ACK,3,0,2,ANGLE_ZERO,FAILED,ANGLE_REFERENCE_INVALID',3),
    {id:4,time:'12:34:57',status:'TIMEOUT',line:'GET_STATUS'},entry('OK','TEL,4,0,0,1,0,0,1,0',5)];
  ui.render({...base,serial:{portOpen:true,errors:0,lines}});
  assert.equal(ui.elements.get('serial-alerts').hidden,false);
  const text=ui.elements.get('serial-alert-list').textContent;
  assert.match(text,/I2C通信で接続が失われました/);assert.match(text,/対象 0x68/);
  assert.match(text,/ハンドル.*基準角度が無効/);assert.match(text,/状態照会.*応答がありません/);
  assert.equal(ui.elements.get('serial-alert-list').children.length,4);
  assert.ok(!text.includes('アクセル100%'));
  // The same retained error survives a later normal snapshot; no repeated live-region replacement.
  const oldList=ui.elements.get('serial-alert-list').children;
  ui.render({...base,serial:{portOpen:true,errors:0,lines}});
  assert.equal(ui.elements.get('serial-alert-list').children,oldList);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('serial-alerts').hidden,true);
  assert.equal(ui.elements.get('serial-alert-list').children.length,0);
});
test('Invalid, truncated and unknown data never masquerade as valid telemetry',()=>{
  const ui=fixture();
  const bad=ui.context.explainSerialEntry({...entry('INVALID','TEL,1,0,0,1,0,0,1,0'),line:'@TEL,1,0,0,1,0,0,1,0*0000'});
  assert.equal(bad.level,'error');assert.match(bad.text,/反映していません/);assert.ok(!bad.text.includes('アクセル100%'));
  const text=encodeURIComponent('装置の文章です。'.repeat(10));
  const full=entry('ERROR','ERR,1,0,ERROR,I2C,READ_FAILED,I2C_SCAN,TEXT,'+text);
  const truncated=ui.context.explainSerialEntry({...full,line:full.line.slice(0,120)});
  assert.equal(truncated.level,'error');assert.match(truncated.text,/読み取りに失敗/);assert.match(truncated.text,/末尾が省略/);
  const unknown=ui.context.explainSerialEntry(entry('IGNORED','FUTURE,1,0'));
  assert.match(unknown.text,/未対応/);assert.equal(unknown.level,'info');
});
test('Diagnostic text is decoded once and inserted as text, including HTML-like device text',()=>{
  const ui=fixture();const text='%3Cimg%20src%3Dx%20onerror%3Dalert%281%29%3E';
  ui.render({...base,serial:{portOpen:true,lines:[entry('DBG','DBG,1,0,SYSTEM,'+text)]}});
  const payload=ui.elements.get('serial-log').children[0].children[2];
  assert.match(payload.children[0].textContent,/<img src=x onerror=alert\(1\)>/);
  assert.equal(payload.children[0].children.length,1); // One text node, not device-supplied elements.
  assert.match(ui.context.explainSerialEntry(entry('DBG','DBG,2,0,SYSTEM,%2520')).text,/%20/);
  assert.ok(!ui.context.explainSerialEntry(entry('DBG','DBG,3,0,SYSTEM,%FF')).text.includes('undefined'));
});
test('Remote rendering shows alerts, parse counts and port failures while keyboard mode stays quiet',()=>{
  const ui=fixture();
  ui.render({...base,inputMode:'Serial P1/P2',serial:{portOpen:false,errors:4,lines:[]}}, {canControl:false});
  assert.match(ui.elements.get('serial-alert-list').textContent,/シリアルポートが未接続/);
  assert.equal(ui.elements.get('parse-errors').textContent,'4');
  assert.ok(!ui.elements.get('serial-alert-list').textContent.includes('解析エラーが4件'));
  ui.render({...base,inputMode:'Keyboard P1/P2 (debug; serial disabled)',serial:{portOpen:false,errors:0,lines:[]}});
  assert.match(ui.elements.get('serial-alert-list').textContent,/解決済み/);
  ui.advance(10000); ui.render({...base,inputMode:'Keyboard',serial:{portOpen:false,errors:4,lines:[]}});
  assert.equal(ui.elements.get('serial-alerts').hidden,true);
});
test('All supplied host acceptance vectors can be explained without exceptions',()=>{
  const ui=fixture();
  const vectors=readFileSync(resolve(__dirname,'../Rgp/Vectors/valid-frames.txt'),'utf8').split('\n');
  let count=0;
  for(const row of vectors){
    if(!row||row.startsWith('#'))continue;
    const [,direction,,escaped]=row.split('|');if(direction!=='ESP_TO_HOST')continue;
    const line=escaped.replace(/\\r/g,'\r').replace(/\\n/g,'\n').trimEnd();
    const result=ui.context.explainSerialEntry({status:line.startsWith('@ERR')?line.split(',')[3]:'OK',line});
    assert.equal(typeof result.text,'string');assert.ok(result.text.length>0);assert.ok(!result.text.includes('undefined'),line);count++;
  }
  assert.equal(count,50);
});

test('Dedicated alerts survive history eviction, are deduplicated and ordered newest first',()=>{
  const ui=fixture();
  const old=entry('ERROR','ERR,1,0,ERROR,I2C,CONNECTION_LOST',1);
  const recent=entry('WARN','ERR,2,0,WARN,HANDLE,ANGLE_REFERENCE_INVALID',2);
  ui.render({...base,serial:{portOpen:true,lines:[recent],alerts:[old,recent]}});
  const alerts=ui.elements.get('serial-alert-list').children;
  assert.equal(alerts.length,2);
  assert.match(alerts[0].textContent,/基準角度が無効/);
  assert.match(alerts[1].textContent,/接続が失われました/);
});

test('Raw section preserves TEL, TX direction, CRLF and untrusted text without translating',()=>{
  const ui=fixture();ui.elements.get('raw-dialog').open=true;const tel=entry('OK','TEL,9,0,-30,0.7,0.1,45,0.2,0.3');
  const raw=[{...tel,direction:'RX',line:tel.line+'\r\n'},{id:10,time:'12:00:01',direction:'TX',line:'<img src=x>\n'}];
  ui.render({...base,rawLines:raw,rawGap:true,telemetry:tel});
  const log=ui.elements.get('raw-log');
  assert.equal(log.children.length,2);
  assert.equal(log.children[0].children[2].textContent,tel.line+'\r\n');
  assert.equal(log.children[1].children[1].textContent,'TX');
  assert.equal(log.children[1].children[2].textContent,'<img src=x>\n');
  assert.match(ui.elements.get('raw-status').textContent,/欠落あり/);
  assert.match(ui.elements.get('latest-tel').textContent,/ハンドル 左 30°アクセル70.0%ブレーキ10.0%/);
  ui.context.setConnection(false);assert.match(log.textContent,/送受信データを待っています/);
});
const healthyHardware = {sessionOpen:true,recent:true,state:'READY',ads:'Ok',imu:'Ok',generation:1,
  inputFresh:true,telemetryEnabled:true,invalidTelemetry:false,angleLocked:true,calibrated:true,
  imus:[{address:'0x68',identity:'0x68',status:'Ok'},{address:'0x69',identity:'0x68',status:'Fault'}],
  pedals:[{number:1,accel:0,brake:0},{number:2,accel:.7,brake:.3}],commands:[]};
test('Hardware shows both IMUs and validates independent pedal values including zero',()=>{
  const ui=fixture();
  ui.render({...base,serial:{portOpen:true,lines:[]},hardware:healthyHardware});
  const cards=ui.elements.get('hardware-grid').children;
  assert.equal(cards.length,6);
  assert.match(cards[1].textContent,/IMU 1.*動作確認済み.*0x68/);
  assert.match(cards[2].textContent,/IMU 2.*異常・接続要確認.*0x69/);
  assert.match(cards[4].textContent,/P1 ペダル.*動作確認済み.*アクセル 0%/);
  assert.match(cards[5].textContent,/P2 ペダル.*動作確認済み.*アクセル 70%.*ブレーキ 30%/);
  ui.render({...base,serial:{portOpen:true,lines:[]},hardware:{...healthyHardware,pedals:[{number:1,accel:2,brake:0},healthyHardware.pedals[1]]}});
  assert.match(ui.elements.get('hardware-grid').children[4].textContent,/入力データ異常/);
  assert.match(ui.elements.get('hardware-grid').children[5].textContent,/動作確認済み/);
  for(const [overrides,label] of [[{inputFresh:false},'正常な入力を受信できません'],[{invalidTelemetry:true},'入力データ異常'],
    [{ads:'Fault'},'センサー異常'],[{telemetryEnabled:false},'入力配信停止中'],[{recent:false},'入力未確認']]) {
    ui.render({...base,serial:{portOpen:true,lines:[]},hardware:{...healthyHardware,...overrides}});
    assert.ok(ui.elements.get('hardware-grid').children[4].textContent.includes(label));
  }
  assert.equal(ui.elements.get('serial-send').disabled,true);
  ui.render({...base,serial:{portOpen:true,lines:[]},hardware:healthyHardware},{canControl:false});
  assert.equal(ui.elements.get('serial-send').disabled,true);
});
test('Players practice independently, including timeouts and waiting',()=>{
  const ui=fixture();
  ui.render({...base,state:'Tutorial',playerProgress:[
    {number:1,state:'Tutorial',step:'Steer',carName:'ROADSTER',remaining:39.2,progress:.5},
    {number:2,state:'Title',step:'Waiting',remaining:0,progress:0}]});
  const cards=ui.elements.get('player-progress').children;
  assert.match(cards[0].textContent,/P1 \/ チュートリアル.*旋回.*ROADSTER.*40 秒/);
  assert.match(cards[1].textContent,/P2 \/ タイトル.*練習の開始を待機/);
  assert.equal(cards[0].children.at(-1).value,.5);
  assert.match(ui.elements.get('flow-state').textContent,/2 \/ 6/);
  ui.render({...base,state:'Tutorial',playerProgress:[{number:1,state:'Tutorial',step:'TimedOut'}]});
  assert.match(ui.elements.get('player-progress').textContent,/制限時間終了・相手の完了を待機/);
  ui.context.setConnection(false);assert.match(ui.elements.get('player-progress').textContent,/状態待機/);
  assert.equal([...html.matchAll(/data-flow-state=/g)].length,6);
});
test('Raw display pauses rendering, bounds history, and resumes with fresh data',()=>{
  const ui=fixture();ui.elements.get('raw-dialog').open=true;const raws=Array.from({length:600},(_,id)=>({id,time:'',direction:'RX',line:'raw'+id+'\n'}));
  ui.render({...base,rawLines:raws});assert.equal(ui.elements.get('raw-log').children.length,500);
  vm.runInContext('rawPaused=true',ui.context);
  ui.render({...base,rawLines:[{id:601,direction:'RX',line:'new\n'}]});
  assert.equal(ui.elements.get('raw-log').children.length,500);
  assert.match(ui.elements.get('raw-status').textContent,/一時停止中/);
  vm.runInContext('rawPaused=false; renderRaw(rawPending,rawGap,true)',ui.context);
  assert.equal(ui.elements.get('raw-log').children.length,1);
  assert.match(ui.elements.get('raw-log').textContent,/new/);
});

test('Race buttons start practice and handle mixed player preparation',()=>{
  const ui=fixture(), enabled=()=>ui.buttons.filter(button=>!button.disabled).map(button=>button.dataset.action);
  ui.render({...base,state:'Title'}); assert.deepEqual(enabled(),['start']);
  ui.render({...base,state:'Tutorial',playerProgress:[{state:'Tutorial'},{state:'Title'}]});
  assert.deepEqual(enabled(),['tutorial']);
  ui.render({...base,state:'Tutorial',playerProgress:[{state:'Tutorial'},{state:'Tutorial'}]});
  assert.deepEqual(enabled(),['countdown']);
  ui.render({...base,state:'Countdown'});assert.deepEqual(enabled(),[]);
});
test('Resolved alerts disappear after ten seconds and retained history cannot resurrect them',()=>{
  const ui=fixture(), error={...entry('ERROR','ERR,1,0,ERROR,I2C,CONNECTION_LOST,0x68'),receivedAt:99000};
  const render=(lines,hardware={...healthyHardware,ads:'Unknown',imu:'Unknown',state:'RECOVERING'})=>
    ui.render({...base,serverTime:100000,serial:{portOpen:true,errors:9,lines,alerts:[error]},hardware});
  render([error]);assert.match(ui.elements.get('serial-alert-list').textContent,/未解決/);
  ui.advance(20000);render([error]);assert.equal(ui.elements.get('serial-alerts').hidden,false);
  const recovery={...entry('EVENT','EVT,2,0,I2C_RESTORED',2),receivedAt:120000};
  render([error,recovery],healthyHardware);assert.match(ui.elements.get('serial-alert-list').textContent,/解決済み/);
  ui.advance(9999);render([error,recovery],healthyHardware);assert.equal(ui.elements.get('serial-alerts').hidden,false);
  ui.advance(1);render([error,recovery],healthyHardware);assert.equal(ui.elements.get('serial-alerts').hidden,true);
  render([error,recovery],healthyHardware);assert.equal(ui.elements.get('serial-alerts').hidden,true);
  const newFault=entry('ERROR','ERR,3,0,ERROR,I2C,CONNECTION_LOST,0x68',3);
  ui.render({...base,serial:{portOpen:true,lines:[error,recovery,newFault]}});
  assert.match(ui.elements.get('serial-alert-list').textContent,/未解決/);
});
test('Recovering one IMU does not dismiss the other IMU fault',()=>{
  const ui=fixture();const a=entry('ERROR','ERR,1,0,ERROR,IMU,READ_FAILED,0x68'),b=entry('ERROR','ERR,2,0,ERROR,IMU,READ_FAILED,0x69',2);
  ui.render({...base,serial:{portOpen:true,lines:[a,b]},hardware:healthyHardware});
  ui.advance(10000);ui.render({...base,serial:{portOpen:true,lines:[a,b]},hardware:healthyHardware});
  assert.equal(ui.elements.get('serial-alert-list').children.length,1);
  assert.match(ui.elements.get('serial-alert-list').textContent,/0x69/);
});
test('A failed startup angle reset resolves after verified automatic reference recovery',()=>{
  const ui=fixture();
  const failure={...entry('ACK_FAILED','ACK,79,16153,57,ANGLE_ZERO,FAILED,ANGLE_REFERENCE_INVALID',79),receivedAt:90000};
  const hardware={...healthyHardware,imus:healthyHardware.imus.map(imu=>({...imu,status:'Ok'}))};
  const render=(h,lines=[failure])=>ui.render({...base,serverTime:100000,serial:{portOpen:true,lines,alerts:[failure]},hardware:h});
  render({...hardware,state:'WAIT_ANGLE',angleLocked:false,inputFresh:false});
  assert.match(ui.elements.get('serial-alert-list').textContent,/未解決/);
  const locked=entry('EVENT','EVT,100,21125,ANGLE_LOCKED',100);
  render({...hardware,inputFresh:false},[failure,locked]);
  assert.match(ui.elements.get('serial-alert-list').textContent,/未解決/,'Angle lock alone does not verify usable inputs');
  render(hardware,[failure,locked]);
  assert.match(ui.elements.get('serial-alert-list').textContent,/解決済み.*失敗しました.*現在は装置が基準角度を確定し、入力が復旧/);
  ui.advance(10000);render(hardware,[failure,locked]);
  assert.equal(ui.elements.get('serial-alerts').hidden,true,'Historical failure must not reappear after recovery');
});
test('Current healthy input can verify angle recovery after replay eviction but cannot clear other command failures',()=>{
  const ui=fixture(),hardware={...healthyHardware,imus:healthyHardware.imus.map(imu=>({...imu,status:'Ok'}))};
  const failure={...entry('ACK_FAILED','ACK,79,0,57,ANGLE_ZERO,FAILED,ANGLE_REFERENCE_INVALID',79),receivedAt:90000};
  ui.render({...base,serverTime:100000,serial:{portOpen:true,lines:[],alerts:[failure]},hardware});
  assert.match(ui.elements.get('serial-alert-list').textContent,/解決済み.*入力が復旧/);
  const other=entry('ACK_FAILED','ACK,80,0,58,SET_STREAM,UNSUPPORTED',80);
  ui.render({...base,serverTime:100000,serial:{portOpen:true,lines:[other]},hardware});
  assert.match(ui.elements.get('serial-alert-list').textContent,/未解決.*対応していません/);
  const next=fixture();next.render({...base,serverTime:80000,serial:{portOpen:true,lines:[failure]},hardware});
  assert.match(next.elements.get('serial-alert-list').textContent,/未解決/,'An older snapshot cannot prove recovery');
});
function trackCommand(ui,command,sequence=8) {
  vm.runInContext(`commandRequest = {command:${JSON.stringify(command)},sequence:${sequence},generation:1,sessionId:'unity'}`,ui.context);
}
function commandSnapshot(command,status,data=[],sequence=8) {
  return {...base,sessionId:'unity',serial:{portOpen:true,lines:[]},hardware:{...healthyHardware,commands:[{sequence,command,status,data,devices:['0x48','0x68']}]}};
}
test('Structured command results show status, configuration, progress, completion and failures inline',()=>{
  const ui=fixture();trackCommand(ui,'GET_CONFIG');
  ui.render(commandSnapshot('GET_CONFIG','OK',['8','GET_CONFIG','OK','TEL_RATE','50','IMU_RATE','20','TEL_STREAM','1','IMU_STREAM','0','DBG_STREAM','0','BETA','0.200']));
  assert.equal(ui.elements.get('serial-command-badge').textContent,'完了');
  assert.match(ui.elements.get('serial-command-values').textContent,/入力の送信頻度50 Hz/);
  assert.ok(!ui.elements.get('serial-command-result').textContent.includes('@ACK'));
  trackCommand(ui,'GET_STATUS');ui.render(commandSnapshot('GET_STATUS','OK',['8','GET_STATUS','OK','READY','1','0','1','0']));
  assert.match(ui.elements.get('serial-command-values').textContent,/IMU全体異常.*校正未完了/);
  trackCommand(ui,'CALIBRATE');ui.render(commandSnapshot('CALIBRATE','ACCEPTED',['8','CALIBRATE','ACCEPTED','GYRO']));
  assert.equal(ui.elements.get('serial-command-badge').textContent,'実行中');
  ui.render(commandSnapshot('CALIBRATE','DONE',['CAL_DONE','8','GYRO']));
  assert.match(ui.elements.get('serial-command-status').textContent,/校正が完了/);
  assert.match(ui.elements.get('serial-command-values').textContent,/ジャイロ/);
  trackCommand(ui,'SCAN_I2C');ui.render(commandSnapshot('SCAN_I2C','DONE',['I2C_SCAN_END','8','2']));
  assert.match(ui.elements.get('serial-command-values').textContent,/検出数2台.*0x48 \/ 0x68/);
  ui.render(commandSnapshot('SCAN_I2C','UNSUPPORTED',['8','SCAN_I2C','UNSUPPORTED']));
  assert.equal(ui.elements.get('serial-command-badge').textContent,'失敗');
  assert.match(ui.elements.get('serial-command-status').textContent,/対応していません/);
  ui.render(commandSnapshot('SCAN_I2C','TIMEOUT'));
  assert.equal(ui.elements.get('serial-command-badge').textContent,'結果未確認');
});
test('Unrelated results and new device sessions cannot complete an operator request',()=>{
  const ui=fixture();trackCommand(ui,'PING');ui.context.showCommandResult('waiting','応答待ち');
  ui.render(commandSnapshot('PING','OK',['9','PING','OK','PONG'],9));
  assert.equal(ui.elements.get('serial-command-badge').textContent,'応答待ち');
  const snapshot=commandSnapshot('PING','OK',['8','PING','OK','PONG']);snapshot.hardware.generation=2;
  ui.render(snapshot);assert.equal(ui.elements.get('serial-command-badge').textContent,'結果未確認');
});

test('Repeated reports update one active issue and a recurrence restarts the recovery window',()=>{
  const ui=fixture();
  const lines=Array.from({length:40},(_,i)=>entry('ERROR','ERR,'+(i+1)+',0,ERROR,IMU,READ_FAILED,0x69',i+1));
  ui.render({...base,serial:{portOpen:true,lines},hardware:healthyHardware});
  assert.equal(ui.elements.get('serial-alert-list').children.length,1);
  const recovered={...healthyHardware,imus:healthyHardware.imus.map(imu=>({...imu,status:'Ok'}))};
  ui.render({...base,serial:{portOpen:true,lines},hardware:recovered});
  ui.advance(10000);ui.render({...base,serial:{portOpen:true,lines},hardware:recovered});
  assert.equal(ui.elements.get('serial-alerts').hidden,true);
  lines.push(entry('ERROR','ERR,41,0,ERROR,IMU,READ_FAILED,0x69',41));
  ui.render({...base,serial:{portOpen:true,lines},hardware:healthyHardware});
  assert.equal(ui.elements.get('serial-alert-list').children.length,1);
  assert.match(ui.elements.get('serial-alert-list').textContent,/未解決/);
});


test('Preview retries preserve good frames, decode before display, and release every replaced URL', async () => {
  const ui = fixture(); ui.render(base); vm.runInContext("activePage='race'",ui.context);
  const revoked = []; let next = 0; let fail = false;
  ui.context.AbortSignal = AbortSignal;
  ui.context.URL = {createObjectURL: () => 'blob:'+(++next), revokeObjectURL: url => revoked.push(url)};
  ui.context.Image = class { async decode() { if (fail) throw new Error('bad JPEG'); } };
  ui.context.fetch = async (_, options) => { assert.ok(options.signal); return {ok:true,blob:async()=>({})}; };
  await ui.context.refreshFrames();
  const first = ui.elements.get('feed-1').src;
  assert.equal(ui.elements.get('feed-1').hidden, false);
  fail = true; await ui.context.refreshFrames();
  assert.equal(ui.elements.get('feed-1').src, first);
  assert.equal(ui.elements.get('feed-1').hidden, false);
  assert.equal(revoked.length, 2);
  fail = false; await ui.context.refreshFrames();
  assert.notEqual(ui.elements.get('feed-1').src, first);
  assert.ok(revoked.includes(first));
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('feed-1').hidden, true);
  assert.equal(revoked.length, 6);
});

test('Preview fetches cannot overlap or restore stale images after a disconnect', async () => {
  const ui = fixture(); ui.render(base); vm.runInContext("activePage='race'",ui.context);
  const revoked = []; const pending = []; let calls = 0;
  ui.context.AbortSignal = AbortSignal;
  ui.context.URL = {createObjectURL: () => 'blob:'+calls, revokeObjectURL: url => revoked.push(url)};
  ui.context.Image = class { async decode() {} };
  ui.context.fetch = () => { calls++; return new Promise(resolve => pending.push(resolve)); };
  const refresh = ui.context.refreshFrames();
  await ui.context.refreshFrames();
  assert.equal(calls, 2);
  ui.context.setConnection(false);
  for (const resolve of pending) resolve({ok:true,blob:async()=>({})});
  await refresh;
  assert.equal(ui.elements.get('feed-1').hidden, true);
  assert.equal(revoked.length, 2);
});


test('Device overview counts faults and never reports stale or incomplete hardware as healthy',()=>{
  const ui=fixture();
  const render=hardware=>ui.render({...base,serial:{portOpen:true,lines:[]},hardware});
  render(healthyHardware);assert.match(ui.elements.get('device-summary').textContent,/1件 要確認/);
  const healthy={...healthyHardware,imus:healthyHardware.imus.map(imu=>({...imu,status:'Ok'}))};
  render(healthy);assert.match(ui.elements.get('device-summary').textContent,/すべて正常/);
  assert.equal(ui.elements.get('device-summary-items').children.length,6);
  for(const change of [{recent:false},{calibrated:false},{angleLocked:false},{state:'CALIBRATING'},{imus:[]}]) {
    render({...healthy,...change});assert.ok(!ui.elements.get('device-summary').textContent.includes('すべて正常'));
  }
  ui.context.setConnection(false);assert.match(ui.elements.get('device-summary').textContent,/確認待ち/);
});
test('Progression shows only applicable actions and keeps viewer actions disabled',()=>{
  const ui=fixture(), visible=()=>ui.buttons.filter(button=>!button.hidden);
  ui.render({...base,state:'Title'});assert.deepEqual(visible().map(button=>button.dataset.action),['start']);
  ui.render({...base,state:'Result'},{canControl:false});
  assert.deepEqual(visible().map(button=>button.dataset.action),['retry','title']);assert.ok(visible().every(button=>button.disabled));
  ui.render({...base,state:'Countdown'});assert.equal(visible().length,0);
  assert.match(ui.elements.get('action-empty').textContent,/自動で次の画面へ/);
  ui.context.setConnection(false);assert.equal(visible().length,0);
});
test('Input meters preserve independent values and warn for old or invalid telemetry',()=>{
  const ui=fixture(), tel=entry('OK','TEL,9,0,0,0,0,25,0.7,0.3');
  ui.render({...base,telemetry:tel,serial:{portOpen:true,lines:[]},hardware:healthyHardware});
  const target=ui.elements.get('latest-tel');assert.match(target.textContent,/入力を受信中/);
  const cards=target.children[1].children;
  assert.equal(cards[0].children[2].children[2].value,0);assert.equal(cards[1].children[3].children[2].value,.3);
  ui.render({...base,telemetry:tel,serial:{portOpen:true,lines:[]},hardware:{...healthyHardware,inputFresh:false}});
  assert.match(target.textContent,/現在の入力は未確認/);
  ui.render({...base,telemetry:entry('OK','TEL,9,0,0,2,0,25,0.7,0.3')});assert.match(target.textContent,/不正/);
  ui.render(base);assert.match(target.textContent,/入力を待っています/);
});
test('Closed raw dialog buffers recent frames without rebuilding its contents',()=>{
  const ui=fixture();const raw=[{id:1,direction:'RX',line:'@raw\r\n'}];
  ui.render({...base,rawLines:raw});assert.equal(ui.elements.get('raw-log').children.length,0);
  ui.elements.get('raw-dialog').open=true;vm.runInContext('renderRaw(rawPending,rawGap,true)',ui.context);
  assert.equal(ui.elements.get('raw-log').children[0].children[2].textContent,raw[0].line);
});

const settings = {master:.8,bgm:.6,engine:.35,effects:.9,inputAvailable:true,inputMode:'keyboard',steeringMode:'Normal',
  constantPedal:.7,constantPaused:false,serialDebug:true,featurePanel:false,
  deviceNames:['USB <script>','HDMI'],deviceKeys:['usb','hdmi'],selectedDevices:['usb','hdmi'],
  outputStatus:['USB','HDMI'],audioRefreshAvailable:true,outputAvailable:[true,true]};
test('Settings mirror Unity, use stable device keys, prevent duplicate outputs and disable viewers',()=>{
  const ui=fixture();ui.render({...base,settings});
  assert.equal(ui.elements.get('settings-master').value,80);
  assert.equal(ui.elements.get('settings-engine-value').textContent,'35%');
  assert.equal(ui.elements.get('settings-inputMode').value,'keyboard');
  assert.equal(ui.elements.get('settings-serialDebug').checked,true);
  const output=ui.elements.get('settings-output-0');
  assert.equal(output.value,'usb');assert.equal(output.children[2].disabled,true);
  assert.equal(output.children[1].textContent,'USB <script>');
  assert.equal(ui.elements.get('settings-master').disabled,false);
  assert.equal(ui.elements.get('settings-constantPedal').disabled,true);
  ui.render({...base,settings},{canControl:false});
  assert.equal(ui.elements.get('settings-inputMode').disabled,true);
  assert.equal(ui.elements.get('settings-master').disabled,true);
  ui.render({...base,settings:{...settings,outputAvailable:[false,false]}});
  assert.equal(ui.elements.get('settings-output-0').disabled,true);
  assert.equal(ui.elements.get('settings-refresh').disabled,false);
  ui.render({...base,settings:{...settings,steeringMode:'ConstantPedal',constantPaused:true}});
  assert.equal(ui.elements.get('settings-constantPedal').disabled,false);
  assert.equal(ui.elements.get('settings-constantPaused').checked,true);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('settings-output-0').disabled,true);
  assert.equal(ui.elements.get('settings-serialDebug').checked,false);
});
test('Old Unity snapshots leave settings disabled and active slider drafts survive polling',()=>{
  const ui=fixture();ui.render(base);
  assert.equal(ui.elements.get('settings-inputMode').disabled,true);
  const slider=ui.elements.get('settings-bgm');slider.value=25;
  vm.runInContext("document.activeElement = document.getElementById('settings-bgm')",ui.context);
  ui.render({...base,settings});assert.equal(slider.value,25);
  vm.runInContext('document.activeElement = null',ui.context);
  ui.render({...base,settings:{...settings,bgm:.45}});assert.equal(slider.value,45);
});
test('Acknowledged settings survive late snapshots and reset with a new Unity session',()=>{
  const ui=fixture();
  ui.render({...base,sessionId:'one',settingsRevision:2,settings});
  ui.render({...base,sessionId:'one',settingsRevision:1,settings:{...settings,master:.1}});
  assert.equal(ui.elements.get('settings-master').value,80);
  ui.render({...base,sessionId:'two',settingsRevision:1,settings:{...settings,master:.2}});
  assert.equal(ui.elements.get('settings-master').value,20);
});
test('HTTP settings use guarded requests, display acknowledged values and query lost responses without replay',async()=>{
  const ui=fixture();ui.context.AbortSignal=AbortSignal;
  ui.context.crypto={randomUUID:require('node:crypto').randomUUID};
  ui.context.poll=async()=>{};
  ui.render({...base,sessionId:'one',settingsRevision:2,stateRevision:4,serverTime:123,settings});
  const slider=ui.elements.get('settings-master');slider.value=49;slider.blur=()=>{};
  const calls=[];
  ui.context.apiFetch=async(route,options)=>{
    calls.push([route,options]);
    return {json:async()=>({ok:true,settingsRevision:3,settings:{...settings,master:.49}})};
  };
  await ui.context.submitSetting(slider);
  const request=JSON.parse(calls[0][1].body);
  assert.equal(calls[0][0],'/api/action/settings');assert.equal(request.targetSession,'one');
  assert.equal(request.stateRevision,4);assert.equal(request.settingsRevision,2);
  assert.deepEqual(request.args,['master','0.49']);assert.equal(slider.value,49);
  assert.match(ui.elements.get('settings-status').textContent,/反映しました/);
  ui.render({...base,sessionId:'one',settingsRevision:2,settings});assert.equal(slider.value,49);
  calls.length=0;slider.value=50;
  ui.context.apiFetch=async(route,options)=>{
    calls.push([route,options]);
    if(options?.method==='POST')throw new Error('response lost');
    return {ok:false};
  };
  await ui.context.submitSetting(slider);
  assert.equal(calls.filter(([,options])=>options?.method==='POST').length,1);
  assert.match(calls[1][0],/^\/api\/command\//);
  assert.match(ui.elements.get('settings-status').textContent,/操作は再送していません/);
});
const f8Settings={...settings,featuresAvailable:true,activePreset:'Standard',selectedPreset:'Standard',rulesLocked:true,
  hasQueuedPreset:false,canStartTestRace:false,features:[{id:'SteeringAssist',label:'スリップストリーム加速',enabled:true},
    {id:'PlayerTwoAutodrive',label:'P2自動 <script>',enabled:false}]};
test('F8 controls show live switches and the fixed three-minute race, synchronize without rebuilding, and disable on disconnect',()=>{
  const ui=fixture();ui.render({...base,settings:f8Settings});
  const rows=ui.elements.get('settings-features').children,slip=rows[0].children[0];
  assert.equal(rows.length,2);assert.equal(slip.dataset.settingArg,'SteeringAssist');
  assert.equal(slip.checked,true);assert.equal(slip.disabled,false);
  assert.equal(rows[1].children[1].textContent,'P2自動 <script>');
  assert.equal(ui.elements.get('settings-feature-count').textContent,'1 / 2 項目がON');
  assert.match(ui.elements.get('settings-rules').textContent,/通常レース.*2周.*180秒/);
  assert.match(ui.elements.get('settings-rule-help').textContent,/最大60秒.*15秒後/);
  assert.equal(ui.elements.get('settings-test-race').disabled,true);
  ui.render({...base,settings:{...f8Settings,features:f8Settings.features.map(feature=>({...feature,enabled:false})),canStartTestRace:true}});
  assert.equal(ui.elements.get('settings-features').children[0].children[0],slip);
  assert.equal(slip.checked,false);assert.equal(ui.elements.get('settings-test-race').disabled,false);
  ui.render({...base,settings:f8Settings},{canControl:false});
  assert.equal(slip.checked,true);assert.equal(slip.disabled,true);
  assert.equal(ui.elements.get('settings-all-off').disabled,true);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('settings-features').children.length,0);
  ui.render(base);
  assert.match(ui.elements.get('settings-feature-count').textContent,/取得できません/);
});
test('F8 feature, all-off and test-start requests use the shared guarded settings transport',async()=>{
  const ui=fixture();ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:require('node:crypto').randomUUID};
  ui.context.poll=async()=>{};
  const calls=[];ui.context.apiFetch=async(route,options)=>{
    calls.push(JSON.parse(options.body));return {json:async()=>({ok:true})};
  };
  ui.render({...base,settings:{...f8Settings,canStartTestRace:true},sessionId:'current',settingsRevision:5});
  const slip=ui.elements.get('settings-features').children[0].children[0];slip.checked=false;slip.blur=()=>{};
  await ui.context.submitSetting(slip);
  for(const [id,value] of [['settings-all-off'],['settings-test-race']]){
    const control=ui.elements.get(id);control.value=value;control.blur=()=>{};
    await ui.context.submitSetting(control);
  }
  assert.deepEqual(calls.map(request=>[request.command,request.args]),
    [['feature',['SteeringAssist','false']],['allFeaturesOff',[]],['testRace',[]]]);
  assert.ok(calls.every(request=>request.settingsRevision===5&&request.targetSession==='current'));
  assert.match(ui.elements.get('settings-status').textContent,/テストレースを開始しました/);
});
const inputControl={available:true,canEscape:true,players:[{number:1,available:true,active:false,owner:'',menuOpen:false},
  {number:2,available:true,active:false,owner:'',menuOpen:false}]};
test('Player input requires permission and explicit takeover; occupied players and ESC actions reflect Unity',()=>{
  const ui=fixture();ui.render({...base,inputControl},{canControl:false});
  assert.equal(ui.elements.get('keyboard-take-0').disabled,true);
  assert.equal(ui.elements.get('keyboard-escape').disabled,true);
  ui.render({...base,inputControl});
  assert.equal(ui.elements.get('keyboard-take-0').disabled,false);
  assert.ok(ui.inputButtons.filter(button=>button.dataset.inputHold).every(button=>button.disabled));
  ui.render({...base,inputControl:{...inputControl,players:[{...inputControl.players[0],active:true,owner:'someone-else'},
    {...inputControl.players[1],available:false,menuOpen:true}]}});
  assert.equal(ui.elements.get('keyboard-take-0').disabled,true);
  assert.match(ui.elements.get('keyboard-status-0').textContent,/別の管理画面/);
  assert.equal(ui.inputButtons.find(button=>button.dataset.menuPlayer==='1').disabled,false);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('keyboard-escape').disabled,true);
  assert.ok(ui.inputButtons.every(button=>button.disabled));
  ui.render(base);assert.equal(ui.elements.get('keyboard-take-0').disabled,true);
});
test('Remote movement uses independent player sequences and stops with the original session after a transition',async()=>{
  const ui=fixture(),calls=[];ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:require('node:crypto').randomUUID};
  ui.context.apiFetch=async(route,options)=>{calls.push([route,JSON.parse(options.body)]);return {json:async()=>({ok:true})};};
  ui.render({...base,inputControl,sessionId:'one',stateRevision:2,serverTime:100});
  ui.context.beginRemoteInput(0);await new Promise(setImmediate);
  assert.equal(calls[0][1].command,'begin');assert.equal(calls[0][1].args.length,3,'begin with neutral input');
  vm.runInContext('remotePlayers[0].pointers.set(1,"accelerate");remotePlayers[0].pointers.set(2,"left")',ui.context);
  await ui.context.pumpRemoteInput(0);
  assert.deepEqual(calls[1][1].args.slice(3),['accelerate','left']);
  assert.equal(calls[1][1].args[0],'0');assert.equal(calls[1][1].targetSession,'one');
  ui.context.beginRemoteInput(1);await new Promise(setImmediate);
  await ui.context.pulseRemoteInput(1,'ready');
  assert.equal(calls.at(-1)[1].command,'pulse');assert.equal(calls.at(-1)[1].args[0],'1');
  assert.equal(calls.at(-1)[1].args.at(-1),'ready');
  ui.render({...base,state:'Countdown',stateRevision:3,sessionId:'one',serverTime:200,
    inputControl:{...inputControl,players:inputControl.players.map(player=>({...player,available:false}))}});
  assert.equal(calls.filter(([,request])=>request.command==='release').length,2);
  const release=calls.at(-1)[1];assert.equal(release.targetSession,'one');assert.equal(release.stateRevision,2);
  assert.equal(ui.elements.get('keyboard-stop-0').disabled,true);
  assert.ok(ui.inputButtons.filter(button=>button.dataset.inputHold).every(button=>button.disabled));
});
test('Stopping during an outstanding request cancels queued button edges and never queues release behind them',async()=>{
  const ui=fixture(),calls=[];let finish;
  ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:require('node:crypto').randomUUID};
  ui.context.apiFetch=async(route,options)=>{
    const request=JSON.parse(options.body);calls.push(request);
    if(request.command==='begin')return new Promise(resolve=>{finish=()=>resolve({json:async()=>({ok:true})});});
    return {json:async()=>({ok:true})};
  };
  ui.render({...base,inputControl,sessionId:'one',stateRevision:1});ui.context.beginRemoteInput(0);await new Promise(setImmediate);
  const pulse=ui.context.pulseRemoteInput(0,'reset');ui.context.stopRemoteInput(0);
  assert.equal(calls.at(-1).command,'release');finish();await pulse;
  assert.equal(calls.filter(request=>request.command==='pulse').length,0);
  assert.equal(calls.filter(request=>request.command==='begin').length,1);
});
test('Acknowledged lease generations ignore older snapshots and stop after Unity clears input without a phase change',async()=>{
  const ui=fixture(),calls=[];ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:require('node:crypto').randomUUID};
  ui.context.apiFetch=async(route,options)=>{
    calls.push(JSON.parse(options.body));return {json:async()=>({ok:true,inputControl:{players:[{number:1,generation:1}]}})};
  };
  const state={...base,inputControl:{...inputControl,players:inputControl.players.map(player=>({...player,generation:0}))},sessionId:'one',stateRevision:2};
  ui.render(state);ui.context.beginRemoteInput(0);await new Promise(setImmediate);
  ui.render(state);assert.equal(ui.elements.get('keyboard-stop-0').disabled,false,'old snapshot must not cancel the new lease');
  ui.render({...state,inputControl:{...state.inputControl,players:[{...state.inputControl.players[0],generation:2},state.inputControl.players[1]]}});
  assert.equal(ui.elements.get('keyboard-stop-0').disabled,true);
  assert.equal(calls.at(-1).command,'release');
});

test('Recovery is available without takeover and never acquires a movement lease',async()=>{
  const ui=fixture(),calls=[];ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:require('node:crypto').randomUUID};
  ui.context.apiFetch=async(route,options)=>{calls.push([route,JSON.parse(options.body)]);return {json:async()=>({ok:true})};};
  const control={...inputControl,players:inputControl.players.map(p=>({...p,canRecover:true}))};
  ui.render({...base,state:'Game',inputControl:control,sessionId:'recovery',stateRevision:3,serverTime:100});
  assert.equal(ui.inputButtons.find(b=>b.dataset.inputPulse==='reset').disabled,false);
  assert.ok(ui.inputButtons.filter(b=>b.dataset.inputHold).every(b=>b.disabled));
  await ui.context.pulseRemoteInput(0,'reset');
  assert.equal(calls.length,1);assert.equal(calls[0][0],'/api/action/keyboardMenu');
  assert.equal(calls[0][1].command,'recover');assert.deepEqual(calls[0][1].args,['0']);
  ui.render({...base,inputControl:control},{canControl:false});
  assert.equal(ui.inputButtons.find(b=>b.dataset.inputPulse==='reset').disabled,true);
});


test('Maintenance covers every game phase while an authorized operator can always clear it',()=>{
  const ui=fixture();
  for(const state of ['Title','CarSelection','Tutorial','Countdown','Game','Goal','Result']) {
    ui.render({...base,state,settings:{maintenanceAvailable:true,maintenanceActive:true}},{canControl:true});
    assert.equal(ui.elements.get('maintenance-status').textContent,'メンテナンス中');
    assert.equal(ui.elements.get('maintenance-start').hidden,true);
    assert.equal(ui.elements.get('maintenance-stop').hidden,false);
    assert.equal(ui.elements.get('maintenance-stop').disabled,false);
    assert.ok(ui.buttons.every(button=>button.disabled));
    assert.equal(ui.elements.get('game-state').textContent,'メンテナンス中');
  }
  ui.render({...base,settings:{maintenanceAvailable:true,maintenanceActive:true}},{canControl:false});
  assert.equal(ui.elements.get('maintenance-stop').disabled,true);
  ui.context.setConnection(false);
  assert.match(ui.elements.get('maintenance-status').textContent,/最後の確認/);
  assert.equal(ui.elements.get('maintenance-stop').disabled,true);
  ui.render({...base,settings:{maintenanceAvailable:true,maintenanceActive:false}},{canControl:true});
  assert.equal(ui.elements.get('maintenance-status').textContent,'通常表示');
  assert.equal(ui.elements.get('maintenance-start').hidden,false);
  ui.render({...base,settings:{}});
  assert.equal(ui.elements.get('maintenance-start').disabled,true);
});

test('Maintenance submits explicit on/off through the existing versioned settings channel',async()=>{
  const ui=fixture();const sent=[];
  ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:()=> 'maintenance-request'};
  ui.context.maintenanceSent=(action,payload)=>sent.push({action,...JSON.parse(JSON.stringify(payload))});
  vm.runInContext("liveController={command:async(action,payload)=>{maintenanceSent(action,payload);return {ok:true,settingsRevision:3,settings:{maintenanceAvailable:true,maintenanceActive:true}};}}",ui.context);
  ui.render({...base,sessionId:'s',settingsRevision:2,settings:{maintenanceAvailable:true,maintenanceActive:false}});
  const button=ui.elements.get('maintenance-start');button.value='true';button.blur=()=>{};
  await ui.context.submitSetting(button);
  assert.deepEqual(sent,[{action:'settings',command:'maintenance',args:['true'],settingsRevision:2}]);
  assert.equal(ui.elements.get('maintenance-stop').hidden,false);
  assert.match(ui.elements.get('maintenance-feedback').textContent,/全ゲーム画面/);
});

test('Updater requires current operator access, availability and no running update',()=>{
  const ui=fixture();
  const settings={updateAvailable:true,updateBusy:false,updateStatus:'Ready to update'};
  ui.render({...base,settings},{canControl:true});
  assert.equal(ui.elements.get('operator-update').disabled,false);
  assert.equal(ui.elements.get('operator-update-status').textContent,'Ready to update');
  for(const settingsValue of [undefined,{}, {...settings,updateAvailable:false},{...settings,updateBusy:true}]) {
    ui.render({...base,settings:settingsValue},{canControl:true});
    assert.equal(ui.elements.get('operator-update').disabled,true);
  }
  ui.render({...base,settings},{canControl:false});
  assert.equal(ui.elements.get('operator-update').disabled,true);
  ui.context.setConnection(false);
  assert.match(ui.elements.get('operator-update-status').textContent,/接続/);
});
test('Update button sends an argument-free updateAndRun command',async()=>{
  const ui=fixture(),sent=[];
  ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:()=> 'update-request'};
  ui.context.recordUpdate=(action,payload)=>sent.push({action,...JSON.parse(JSON.stringify(payload))});
  vm.runInContext("liveController={command:async(action,payload)=>{recordUpdate(action,payload);return {ok:true,settingsRevision:4,settings:{updateAvailable:false,updateBusy:true,updateStatus:'Checking'}};}}",ui.context);
  ui.render({...base,sessionId:'s',settingsRevision:3,settings:{updateAvailable:true,updateBusy:false}});
  const button=ui.elements.get('operator-update');button.blur=()=>{};
  await ui.context.submitSetting(button);
  assert.deepEqual(sent,[{action:'settings',command:'updateAndRun',args:[],settingsRevision:3}]);
  assert.equal(button.disabled,true);
});

test('Per-output delay controls show milliseconds and require an operator',()=>{
  const ui=fixture();const settings={outputDelayMs:[180,0]};
  ui.render({...base,settings},{canControl:true});
  assert.equal(ui.elements.get('settings-delay-0').value,180);
  assert.equal(ui.elements.get('settings-delay-1').value,0);
  assert.equal(ui.elements.get('settings-delay-0-value').textContent,'180 ms');
  assert.equal(ui.elements.get('settings-delay-0').disabled,false);
  ui.render({...base,settings},{canControl:false});
  assert.equal(ui.elements.get('settings-delay-0').disabled,true);
  ui.render({...base,settings:{}},{canControl:true});
  assert.equal(ui.elements.get('settings-delay-1').disabled,true);
});
test('Speaker delay sends player and integer milliseconds without volume scaling',async()=>{
  const ui=fixture(),sent=[];
  ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:()=> 'delay-request'};
  ui.context.recordDelay=(action,payload)=>sent.push({action,...JSON.parse(JSON.stringify(payload))});
  vm.runInContext("liveController={command:async(action,payload)=>{recordDelay(action,payload);return {ok:true,settingsRevision:4,settings:{outputDelayMs:[0,230]}};}}",ui.context);
  ui.render({...base,sessionId:'s',settingsRevision:3,settings:{outputDelayMs:[0,0]}});
  const control=ui.elements.get('settings-delay-1');control.value=230;control.blur=()=>{};
  await ui.context.submitSetting(control);
  assert.deepEqual(sent,[{action:'settings',command:'outputDelay',args:['1','230'],settingsRevision:3}]);
});


test('Independent updater stays usable while Unity is disconnected and respects relay permission',()=>{
  const ui=fixture();ui.context.setConnection(false);
  vm.runInContext("operatorStatus={available:true,busy:false,canControl:true,message:'Unity停止中でも更新できます。'};renderOperatorUpdate();",ui.context);
  assert.equal(ui.elements.get('operator-update').disabled,false);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('operator-update').disabled,false,'disconnect render preserves independent updater');
  vm.runInContext("operatorStatus.canControl=false;renderOperatorUpdate();",ui.context);
  assert.equal(ui.elements.get('operator-update').disabled,true);
});
test('Offline button posts to the independent endpoint and renders the real failure',async()=>{
  const ui=fixture(),sent=[];ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:()=> 'offline-update'};
  ui.context.captureUpdate=(route,options)=>{sent.push([route,options]);};
  vm.runInContext("operatorStatus={available:true,busy:false,canControl:true};operatorPolling=true;renderOperatorUpdate();apiFetch=async(route,options)=>{captureUpdate(route,options);return {json:async()=>({ok:false,message:'Git認証に失敗しました。'})};};",ui.context);
  const button=ui.elements.get('operator-update');button.blur=()=>{};
  await ui.context.submitSetting(button);
  assert.equal(sent.length,1);assert.equal(sent[0][0],'/api/operator-update');
  assert.deepEqual(JSON.parse(sent[0][1].body),{id:'offline-update'});
  assert.equal(ui.elements.get('settings-status').textContent,'Git認証に失敗しました。');
});

// Private HTTP origins used by USB tethering do not expose crypto.randomUUID.
test('USB HTTP origins generate secure v4 command IDs without randomUUID',()=>{
  const ui=fixture();ui.context.crypto={getRandomValues:require('node:crypto').webcrypto.getRandomValues.bind(require('node:crypto').webcrypto)};
  const ids=new Set(Array.from({length:200},()=>ui.context.newRequestId()));
  assert.equal(ids.size,200);for(const id of ids)assert.match(id,/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/);
});

test('Home has independent recovery controls without takeover and disables them on disconnect',()=>{
  const ui=fixture();
  ui.render({...base,inputControl:{available:true,players:[{number:1,canRecover:true},{number:2,canRecover:false}]}});
  const buttons=ui.inputButtons.filter(button=>button.dataset.inputPulse==='reset');
  assert.equal(buttons.length,4);
  assert.equal(buttons[0].disabled,false);
  assert.equal(buttons[1].disabled,true);
  ui.context.setConnection(false);
  assert.ok(buttons.every(button=>button.disabled));
  assert.match(ui.elements.get('home-inputs').textContent,/待っています/);
});
test('Home mirrors verified physical wheel and both independent pedal values, marking stale input',()=>{
  const ui=fixture();
  const body='TEL,1,1,12,0.75,0.2,-24,0,0.6';
  const line='@'+body+'*'+ui.context.rgpCrc(body);
  const snapshot={...base,telemetry:{line,status:'OK'},serial:{portOpen:true,lines:[]},hardware:{sessionOpen:true,recent:true,inputFresh:true,telemetryEnabled:true}};
  ui.render(snapshot);
  const home=ui.elements.get('home-inputs').textContent;
  assert.match(home,/右 12°/);assert.match(home,/左 24°/);
  assert.match(home,/75.0%/);assert.match(home,/20.0%/);assert.match(home,/60.0%/);
  ui.render({...snapshot,hardware:{...snapshot.hardware,inputFresh:false}});
  assert.match(ui.elements.get('home-inputs').textContent,/現在の入力は未確認/);
});

test('Home and race progress reflect independent results and waiting without changing the active race',()=>{
  const ui=fixture();
  ui.render({...base,playerProgress:[{number:1,state:'Result',step:'Results',remaining:12},{number:2,state:'Game',step:'Driving'}]});
  assert.match(ui.elements.get('home-player-detail-1').textContent,/結果を表示中/);
  assert.equal(ui.elements.get('home-player-status-2').textContent,'レース中');
  ui.render({...base,playerProgress:[{number:1,state:'Game',step:'Spectating'},{number:2,state:'Game',step:'Driving'}]});
  assert.match(ui.elements.get('home-player-detail-1').textContent,/相手のゴールを待っています/);
});

test('Build progress survives Unity disconnect and generic settings rendering without becoming a version error',()=>{
  const ui=fixture();
  vm.runInContext("operatorStatus={available:false,busy:true,canControl:true,canCancel:true,phase:'building',requestId:'job-1',message:'Unityでビルドしています。'};renderOperatorUpdate();",ui.context);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('operator-update-status').textContent,'Unityでビルドしています。');
  assert.equal(ui.elements.get('settings-status').textContent,'Unityでビルドしています。');
  assert.doesNotMatch(ui.elements.get('settings-feature-count').textContent,/取得できません/);
  assert.equal(ui.elements.get('operator-cancel').hidden,false);
  assert.equal(ui.elements.get('operator-cancel').disabled,false);
});
test('Cancellation requires an operator, an active cancellable build, and the exact request anchor',async()=>{
  const ui=fixture(),sent=[];ui.context.AbortSignal=AbortSignal;ui.context.crypto={randomUUID:()=> 'cancel-command'};
  ui.context.captureCancel=(route,options)=>sent.push([route,options]);
  vm.runInContext("operatorStatus={available:false,busy:true,canControl:true,canCancel:true,phase:'building',requestId:'job-1'};operatorPolling=true;renderOperatorUpdate();apiFetch=async(route,options)=>{captureCancel(route,options);return {json:async()=>({ok:true,message:'キャンセルを受け付けました。'})};};",ui.context);
  await ui.context.cancelOperatorUpdate();
  assert.equal(sent[0][0],'/api/operator-update');
  assert.deepEqual(JSON.parse(sent[0][1].body),{id:'cancel-command',command:'cancel',requestId:'job-1'});
  assert.equal(ui.elements.get('operator-cancel').disabled,true);
  assert.match(ui.elements.get('operator-cancel').textContent,/キャンセル処理中/);
  await ui.context.cancelOperatorUpdate();assert.equal(sent.length,1);
  for(const field of ['canControl','canCancel']) {
    vm.runInContext("operatorStatus={busy:true,canControl:true,canCancel:true,requestId:'job-2'};operatorStatus['"+field+"']=false;renderOperatorUpdate();",ui.context);
    assert.equal(ui.elements.get('operator-cancel').disabled,true);
    await ui.context.cancelOperatorUpdate();assert.equal(sent.length,1);
  }
});
test('Cancellation completion and real build failure remain visible after disconnected snapshots',()=>{
  const ui=fixture();
  for(const [phase,message] of [['cancelled','更新をキャンセルしました。'],['failed','Unityビルドに失敗しました。build.logを確認してください。']]) {
    vm.runInContext('operatorStatus='+JSON.stringify({available:true,busy:false,canControl:true,canCancel:false,phase,message})+';',ui.context);
    ui.context.setConnection(false);
    assert.equal(ui.elements.get('settings-status').textContent,message);
    assert.equal(ui.elements.get('operator-cancel').hidden,true);
    assert.equal(ui.elements.get('operator-update').disabled,false);
  }
});


test('Window reposition requires supported Unity and operator permissions',()=>{
  const ui=fixture();
  for(const settings of [undefined,{}, {windowRepositionAvailable:false}]) {
    ui.render({...base,settings},{canControl:true});
    assert.equal(ui.elements.get('settings-reposition').disabled,true);
    assert.match(ui.elements.get('settings-reposition-note').textContent,/Mac版Race/);
  }
  ui.render({...base,settings:{windowRepositionAvailable:true}},{canControl:true});
  assert.equal(ui.elements.get('settings-reposition').disabled,false);
  assert.match(ui.elements.get('settings-reposition-note').textContent,/再接続/);
  ui.render({...base,settings:{windowRepositionAvailable:true}},{canControl:false});
  assert.equal(ui.elements.get('settings-reposition').disabled,true);
  ui.context.setConnection(false);
  assert.equal(ui.elements.get('settings-reposition').disabled,true);
});

test('Window reposition submits a versioned command without arguments and reports failure',async()=>{
  const ui=fixture(),sent=[];
  ui.context.recordReposition=(action,payload)=>sent.push({action,...JSON.parse(JSON.stringify(payload))});
  vm.runInContext("liveController={command:async(action,payload)=>{recordReposition(action,payload);return {ok:false,reason:'window_layout_unavailable'};}}",ui.context);
  ui.render({...base,sessionId:'s',settingsRevision:3,settings:{windowRepositionAvailable:true}});
  const control=ui.elements.get('settings-reposition');control.blur=()=>{};
  await ui.context.submitSetting(control);
  assert.deepEqual(sent,[{action:'settings',command:'repositionWindows',args:[],settingsRevision:3}]);
  assert.match(ui.elements.get('settings-status').textContent,/外部ディスプレイ2台/);
});


test('Race mode selection reports queued rules without changing the current race',()=>{
  const ui=fixture();
  ui.render({...base,goalLap:2,settings:{featuresAvailable:true,raceModeAvailable:true,activePreset:'Standard',selectedPreset:'Quick',hasQueuedPreset:true}},{canControl:true});
  assert.equal(ui.elements.get('settings-raceMode').value,'Quick');
  assert.equal(ui.elements.get('settings-raceMode').disabled,false);
  assert.match(ui.elements.get('settings-race-mode-status').textContent,/次の試合.*クイック/);
  assert.match(ui.elements.get('settings-rules').textContent,/通常レース.*2周/);
  ui.render({...base,goalLap:1,settings:{featuresAvailable:true,raceModeAvailable:true,activePreset:'Quick',selectedPreset:'Quick'}},{canControl:true});
  assert.match(ui.elements.get('settings-rules').textContent,/クイック.*1周/);
  assert.match(ui.elements.get('settings-rule-help').textContent,/練習とコース紹介を省略/);
  ui.render({...base,settings:{}},{canControl:true});assert.equal(ui.elements.get('settings-raceMode').disabled,true);
  ui.render({...base,settings:{raceModeAvailable:true}},{canControl:false});assert.equal(ui.elements.get('settings-raceMode').disabled,true);
});

test('Race mode submits a versioned selection',async()=>{
  const ui=fixture(),sent=[];
  ui.context.captureMode=(action,payload)=>sent.push({action,...JSON.parse(JSON.stringify(payload))});
  vm.runInContext("liveController={command:async(action,payload)=>{captureMode(action,payload);return {ok:true};}}",ui.context);
  ui.render({...base,sessionId:'s',settingsRevision:7,settings:{raceModeAvailable:true}});
  const control=ui.elements.get('settings-raceMode');control.value='Quick';control.blur=()=>{};
  await ui.context.submitSetting(control);
  assert.deepEqual(sent,[{action:'settings',command:'raceMode',args:['Quick'],settingsRevision:7}]);
});

test('A built update waiting for the match remains cancellable',()=>{
  const ui=fixture();
  vm.runInContext("operatorStatus={available:false,busy:true,canControl:true,canCancel:true,phase:'waiting-race',message:'現在の試合終了を待っています。'};renderOperatorUpdate();",ui.context);
  assert.equal(ui.elements.get('operator-cancel').disabled,false);
  assert.equal(ui.elements.get('operator-cancel').hidden,false);
  assert.equal(ui.elements.get('operator-cancel').textContent,'更新をキャンセル');
  assert.match(ui.elements.get('operator-update-status').textContent,/試合終了/);
});


test('Quick mode offers a race start and shows a ready player waiting for the other',()=>{
  const ui=fixture();
  ui.render({...base,state:'Title',settings:{activePreset:'Quick'},playerProgress:[{number:1,state:'Title',step:'Ready'}]});
  assert.match(ui.buttons.find(button=>button.dataset.action==='start').textContent,/レースを始める.*練習なし/);
  assert.match(ui.elements.get('player-progress').textContent,/開始準備完了・相手の開始操作を待機/);
  ui.render({...base,state:'Title',settings:{activePreset:'Standard'}});
  assert.match(ui.buttons.find(button=>button.dataset.action==='start').textContent,/練習を始める/);
});
