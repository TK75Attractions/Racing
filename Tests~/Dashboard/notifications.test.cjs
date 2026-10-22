const {test}=require('node:test');const assert=require('node:assert/strict');const fs=require('node:fs');const vm=require('node:vm');const path=require('node:path');
const assets=path.resolve(__dirname,'../../Assets/StreamingAssets/Dashboard');
test('Service worker displays every received push and never navigates outside its own application',async()=>{
 const events={},shown=[],opened=[];const context=vm.createContext({URL,self:{location:{origin:'https://race.test.ts.net'},addEventListener:(key,fn)=>events[key]=fn,
  registration:{showNotification:async(...args)=>shown.push(args)},clients:{matchAll:async()=>[{url:'https://evil.test/',focus:async()=>assert.fail('foreign window')}],openWindow:async url=>opened.push(url)}}});
 vm.runInContext(fs.readFileSync(path.join(assets,'sw.js'),'utf8'),context);
 let waiting;events.push({data:{json:()=>({title:'センサー異常',body:'IMU1',url:'https://evil.test'})},waitUntil:p=>waiting=p});await waiting;
 assert.equal(shown[0][0],'センサー異常');assert.equal(shown[0][1].body,'IMU1');assert.equal(shown[0][1].data.url,'/');
 events.push({data:{json:()=>{throw new Error('bad JSON');}},waitUntil:p=>waiting=p});await waiting;assert.equal(shown.length,2,'iOS must always receive a visible notification');
 events.notificationclick({notification:{close(){}},waitUntil:p=>waiting=p});await waiting;assert.deepEqual(opened,['https://race.test.ts.net/']);
});
test('Notification permission is requested only from the enable tap, followed by scoped subscription registration',async()=>{
 const elements=new Map(['push-status','push-enable','push-disable','push-test','push-delivery'].map(id=>[id,{disabled:true,textContent:''}]));
 const calls=[],permissions=[];let current=null;
 const subscription={endpoint:'https://web.push.apple.com/test',options:{applicationServerKey:new Uint8Array([1,2,3]).buffer},toJSON:()=>({endpoint:'https://web.push.apple.com/test',keys:{p256dh:'key',auth:'auth'}}),unsubscribe:async()=>{current=null;return true;}};
 const registration={pushManager:{getSubscription:async()=>current,subscribe:async options=>{calls.push(['subscribe',options]);current=subscription;return subscription;}}};
 const Notification={permission:'default',requestPermission:()=>{permissions.push('tap');Notification.permission='granted';return Promise.resolve('granted');}};
 const context=vm.createContext({Uint8Array,ArrayBuffer,atob,AbortSignal,Notification,location:{origin:'https://race.test.ts.net'},window:{isSecureContext:true,PushManager:function(){},Notification},
  navigator:{serviceWorker:{register:async(route,options)=>{calls.push(['worker',route,options]);return registration;},ready:Promise.resolve(registration)}},
  document:{hidden:false,getElementById:id=>elements.get(id),addEventListener(){}},setInterval(){},
  fetch:async(route,options)=>{calls.push([route,options]);return {ok:true,json:async()=>route==='/api/push'?{enabled:true,publicKey:'AQID'}:{ok:true}};}});
 await vm.runInContext(fs.readFileSync(path.join(assets,'notifications.js'),'utf8'),context);
 assert.equal(permissions.length,0,'opening page must not prompt');assert.equal(elements.get('push-enable').disabled,false);
 const enabling=elements.get('push-enable').onclick();assert.equal(permissions.length,1,'permission request stays inside synchronous tap handler');await enabling;
 assert.equal(calls.find(x=>x[0]==='subscribe')[1].userVisibleOnly,true);
 assert.equal(JSON.parse(calls.find(x=>x[0]==='/api/push/subscribe')[1].body).endpoint,subscription.endpoint);
 assert.equal(elements.get('push-test').disabled,false);await elements.get('push-disable').onclick();
 assert.ok(calls.some(x=>x[0]==='/api/push/unsubscribe'));assert.equal(elements.get('push-enable').disabled,false);
});

test('iPhone Safari bookmarks explain re-adding the installed app without prompting permission',async()=>{
 const elements=new Map(['push-status','push-enable','push-disable','push-test','push-delivery'].map(id=>[id,{disabled:true,textContent:''}]));
 const context=vm.createContext({navigator:{userAgent:'iPhone',standalone:false},window:{matchMedia:()=>({matches:false})},document:{getElementById:id=>elements.get(id)}});
 await vm.runInContext(fs.readFileSync(path.join(assets,'notifications.js'),'utf8'),context);
 assert.match(elements.get('push-status').textContent,/ホーム画面へ追加し直し/);assert.equal(elements.get('push-enable').disabled,true);
});
