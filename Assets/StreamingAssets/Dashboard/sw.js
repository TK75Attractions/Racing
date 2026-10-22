/* No page/API caching: every control and diagnosis must use current Unity state. */
self.addEventListener('install',()=>self.skipWaiting());
self.addEventListener('activate',event=>event.waitUntil(self.clients.claim()));
self.addEventListener('push',event=>{
  let message={};try{message=event.data?.json()||{};}catch{}
  event.waitUntil(self.registration.showNotification(typeof message.title==='string'?message.title:'Racing 機器・接続の異常',{
    body:typeof message.body==='string'?message.body:'管理画面で機器・接続状態を確認してください。',
    tag:'racing-hardware',icon:'/app-icon.png',badge:'/app-icon.png',data:{url:'/'},renotify:true
  }));
});
self.addEventListener('notificationclick',event=>{
  event.notification.close();
  event.waitUntil((async()=>{
    const target=new URL('/',self.location.origin).href;
    const windows=await self.clients.matchAll({type:'window',includeUncontrolled:true});
    const existing=windows.find(client=>new URL(client.url).origin===self.location.origin);
    if(existing){await existing.focus();return;}
    await self.clients.openWindow(target);
  })());
});
