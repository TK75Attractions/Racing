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
  const document = { getElementById(id) { assert.ok(elements.has(id), `Missing UI element ${id}`); return elements.get(id); },
    createElement(){return new Element();}, querySelectorAll(selector){return selector==='[data-action]'?buttons:[];} };
  const context = vm.createContext({document,location:{origin:'http://127.0.0.1:8765'},URL:{revokeObjectURL(){}}});
  // Load the real render functions without timers, network polling or UI event handlers.
  vm.runInContext(script.slice(0,script.indexOf("document.querySelectorAll('[data-action]').forEach")),context);
  const render = (snapshot,options) => context.render(snapshot,options);
  return {elements,buttons,context,render};
}
const base = {state:'Game',goalLap:2,players:[{number:1},{number:2}],serial:{lines:[]},participantCount:4,
  participants:Array.from({length:4},(_,index)=>({number:index+1,isCpu:index>=2,position:4-index,lap:1,
    status:'Driving',speed:72,targetSpeed:90,progress:100, rivalNumber:index>=2?index-1:0,gap:-20,recoveries:2}))};
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
  assert.match(ui.elements.get('latest-tel').textContent,/P1  HANDLE -12.5° \/ ACCEL 75.0% \/ BRAKE 0.0%/);
  assert.match(ui.elements.get('latest-tel').textContent,/P2  HANDLE 25° \/ ACCEL 0.0% \/ BRAKE 40.0%/);
  assert.ok(ui.elements.get('latest-tel').textContent.includes(tel.line));
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
  assert.match(ui.elements.get('serial-log').textContent,/TX SENT/);
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
  assert.match(ui.elements.get('serial-alert-list').textContent,/解析エラーが4件/);
  ui.render({...base,inputMode:'Keyboard P1/P2 (debug; serial disabled)',serial:{portOpen:false,errors:0,lines:[]}});
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
  const ui=fixture();const tel=entry('OK','TEL,9,0,-30,0.7,0.1,45,0.2,0.3');
  const raw=[{...tel,direction:'RX',line:tel.line+'\r\n'},{id:10,time:'12:00:01',direction:'TX',line:'<img src=x>\n'}];
  ui.render({...base,rawLines:raw,rawGap:true,telemetry:tel});
  const log=ui.elements.get('raw-log');
  assert.equal(log.children.length,2);
  assert.equal(log.children[0].children[2].textContent,tel.line+'\r\n');
  assert.equal(log.children[1].children[1].textContent,'TX');
  assert.equal(log.children[1].children[2].textContent,'<img src=x>\n');
  assert.match(ui.elements.get('raw-status').textContent,/欠落あり/);
  assert.match(ui.elements.get('latest-tel').textContent,/ハンドル 左 30°、アクセル 70.0%、ブレーキ 10.0%/);
  ui.context.setConnection(false);assert.equal(log.children.length,0);
});
test('Hardware uses explicit evidence and never invents pedal connectivity from zero inputs',()=>{
  const ui=fixture();const hardware={sessionOpen:true,recent:true,state:'READY',ads:'Ok',imu:'Fault',imuAddress:'0x68',imuIdentity:'0x68'};
  ui.render({...base,serial:{portOpen:true,lines:[]},hardware});
  const cards=ui.elements.get('hardware-grid').children;
  assert.match(cards[0].textContent,/通信中/);assert.match(cards[1].textContent,/異常・接続要確認/);
  assert.match(cards[2].textContent,/動作確認済み/);
  assert.match(cards[3].textContent,/物理接続：未確認/);assert.match(cards[4].textContent,/物理接続：未確認/);
  assert.equal(ui.elements.get('serial-send').disabled,false);
  ui.render({...base,serial:{portOpen:true,lines:[]},hardware:{...hardware,recent:false}});
  assert.ok(ui.elements.get('hardware-grid').children.slice(1).every(card=>!card.textContent.includes('動作確認済み')));
  assert.equal(ui.elements.get('serial-send').disabled,true);
  ui.render({...base,serial:{portOpen:true,lines:[]},hardware},{canControl:false});
  assert.equal(ui.elements.get('serial-send').disabled,true);
});
test('Players can select cars and practice independently, including timeouts and waiting',()=>{
  const ui=fixture();
  ui.render({...base,state:'Tutorial',playerProgress:[
    {number:1,state:'Tutorial',step:'Steer',carName:'ROADSTER',remaining:39.2,progress:.5},
    {number:2,state:'CarSelection',step:'Paint',carName:'SPORT CAR',remaining:12.2,progress:0}]});
  const cards=ui.elements.get('player-progress').children;
  assert.match(cards[0].textContent,/P1 \/ チュートリアル.*旋回.*ROADSTER.*40 秒/);
  assert.match(cards[1].textContent,/P2 \/ 車選択.*カラーを選択中.*13 秒/);
  assert.equal(cards[0].children.at(-1).value,.5);
  assert.match(ui.elements.get('flow-state').textContent,/3 \/ 7/);
  ui.render({...base,state:'Tutorial',playerProgress:[{number:1,state:'Tutorial',step:'TimedOut'}]});
  assert.match(ui.elements.get('player-progress').textContent,/制限時間終了・相手の完了を待機/);
  ui.context.setConnection(false);assert.match(ui.elements.get('player-progress').textContent,/状態待機/);
  assert.equal([...html.matchAll(/data-flow-state=/g)].length,7);
});
test('Raw display pauses rendering, bounds history, and resumes with fresh data',()=>{
  const ui=fixture();const raws=Array.from({length:600},(_,id)=>({id,time:'',direction:'RX',line:'raw'+id+'\n'}));
  ui.render({...base,rawLines:raws});assert.equal(ui.elements.get('raw-log').children.length,500);
  vm.runInContext('rawPaused=true',ui.context);
  ui.render({...base,rawLines:[{id:601,direction:'RX',line:'new\n'}]});
  assert.equal(ui.elements.get('raw-log').children.length,500);
  assert.match(ui.elements.get('raw-status').textContent,/一時停止中/);
  vm.runInContext('rawPaused=false; renderRaw(rawPending,rawGap,true)',ui.context);
  assert.equal(ui.elements.get('raw-log').children.length,1);
  assert.match(ui.elements.get('raw-log').textContent,/new/);
});


test('Preview retries preserve good frames, decode before display, and release every replaced URL', async () => {
  const ui = fixture(); ui.render(base);
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
  const ui = fixture(); ui.render(base);
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
