const {test}=require('node:test');
const assert=require('node:assert/strict');
const {readFileSync}=require('node:fs');
const {resolve}=require('node:path');
const load=()=>import('data:text/javascript;base64,'+Buffer.from(readFileSync(resolve(__dirname,'../../Assets/StreamingAssets/Dashboard/live.js'),'utf8')).toString('base64'));
test('replayed history replaces duplicate IDs and retains chronological newest 500',async()=>{
  const {mergeHistory}=await load();
  const old=Array.from({length:500},(_,id)=>({id,line:'old'}));
  const result=mergeHistory(old,[{id:499,line:'updated'},{id:500,line:'new'}]);
  assert.equal(result.length,500);assert.equal(result[0].id,1);
  assert.equal(result.at(-2).line,'updated');assert.equal(result.at(-1).id,500);
  assert.equal(new Set(result.map(e=>e.id)).size,500);
});

test('silent connection disables the screen immediately without waiting for a close handshake',async()=>{
  const {startLive}=await load();
  const names=['document','location','WebSocket','setInterval','setTimeout','clearTimeout'];
  const original=Object.fromEntries(names.map(name=>[name,globalThis[name]]));const dateNow=Date.now;
  const intervals=[],timeouts=new Map(),sockets=[],elements=new Map();let timer=0,now=1000;
  try{
    Date.now=()=>now;
    globalThis.document={getElementById:id=>{if(!elements.has(id))elements.set(id,{textContent:''});return elements.get(id);}};
    globalThis.location={href:'https://race.example.ts.net/',protocol:'https:'};
    globalThis.WebSocket=class {constructor(url){this.url=String(url);sockets.push(this);}close(){this.closed=true;}};
    globalThis.setInterval=fn=>{intervals.push(fn);return intervals.length;};
    globalThis.setTimeout=fn=>{timeouts.set(++timer,fn);return timer;};
    globalThis.clearTimeout=id=>timeouts.delete(id);
    const connections=[],renders=[];
    await startLive({apiFetch:async()=>({ok:true,json:async()=>({canControl:true,remote:true})}),
      setConnection:value=>connections.push(value),render:(snapshot,options)=>renders.push(options),
      renderTelemetry(){},refreshFrames:async()=>{}});
    const packet={sessionId:'unity',serialSession:'device',cursor:7,lines:[],snapshot:{sessionId:'unity',state:'Title',serial:{}}};
    sockets[0].onmessage({data:JSON.stringify(packet)});intervals[0]();
    assert.equal(renders.at(-1).canControl,true);
    now=5001;intervals[0]();
    assert.equal(connections.at(-1),false);assert.equal(sockets[0].closed,true);
    const previous=renders.length;sockets[0].onmessage({data:JSON.stringify(packet)});intervals[0]();
    assert.equal(renders.length,previous,'late packets from a closed socket cannot revive the old state');
    assert.match(elements.get('live-detail').textContent,/再接続/);
    [...timeouts.values()][0]();assert.equal(sockets.length,2);
    assert.match(sockets[1].url,/wss:/);assert.match(sockets[1].url,/after=7/);
  }finally{Date.now=dateNow;for(const name of names){if(original[name]===undefined)delete globalThis[name];else globalThis[name]=original[name];}}
});
