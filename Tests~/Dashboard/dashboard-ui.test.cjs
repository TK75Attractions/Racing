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
  assert.equal(ui.elements.get('game-state').textContent,'練習');
});
