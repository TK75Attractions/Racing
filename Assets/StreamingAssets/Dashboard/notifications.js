/* The relay keeps monitoring while the Home Screen app is suspended or closed. */
(async function(){
  const get=id=>document.getElementById(id),status=get('push-status');
  const enable=get('push-enable'),disable=get('push-disable'),test=get('push-test');
  let registration=null,subscription=null,info=null,busy=false;
  function controls(){enable.disabled=busy||!info?.enabled||!!subscription;disable.disabled=busy||!subscription;test.disabled=busy||!subscription||!info?.enabled;}
  const send=async(path,body)=>{
    const response=await fetch('/api/push/'+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),cache:'no-store',signal:AbortSignal.timeout(12000)});
    if(!response.ok)throw new Error(response.status===429?'テスト通知は1分後に再試行してください。':'通知サーバーへの登録・送信を確認できませんでした。');
    const result=await response.json();if(!result.ok)throw new Error('通知設定を反映できませんでした。');
  };
  const decode=value=>Uint8Array.from(atob(value.replace(/-/g,'+').replace(/_/g,'/').padEnd(Math.ceil(value.length/4)*4,'=')),c=>c.charCodeAt(0));
  function compatible(sub){const key=sub?.options?.applicationServerKey;return key&&Array.from(new Uint8Array(key)).join(',')===Array.from(decode(info.publicKey)).join(',');}
  enable.onclick=async()=>{
    if(busy||!info?.enabled||!registration)return;
    busy=true;controls();
    try{
      // Permission is requested directly from this tap, before asynchronous subscription work.
      const permission=await Notification.requestPermission();
      if(permission!=='granted')throw new Error('通知が許可されていません。iPhoneの設定からこのWebアプリの通知を許可してください。');
      const sub=await registration.pushManager.subscribe({userVisibleOnly:true,applicationServerKey:decode(info.publicKey)});
      await send('subscribe',sub.toJSON());subscription=sub;status.textContent='異常通知が有効です。アプリを閉じていても通知を受信します。';
    }catch(error){status.textContent=error.message;}finally{busy=false;controls();}
  };
  disable.onclick=async()=>{
    if(busy||!subscription)return;busy=true;controls();const sub=subscription;
    try{if(!await sub.unsubscribe())throw new Error('端末の通知購読を解除できませんでした。');subscription=null;await send('unsubscribe',{endpoint:sub.endpoint});status.textContent='この端末への異常通知を停止しました。';}
    catch(error){status.textContent=error.message;}finally{busy=false;controls();}
  };
  test.onclick=async()=>{
    if(busy||!subscription)return;busy=true;controls();
    try{await send('test',{});status.textContent='テスト通知を要求しました。下の配送状態と端末の通知を確認してください。';}
    catch(error){status.textContent=error.message;}finally{busy=false;controls();}
  };
  const ios=/iPad|iPhone|iPod/.test(navigator.userAgent||'')||navigator.platform==='MacIntel'&&navigator.maxTouchPoints>1;
  const installed=navigator.standalone||window.matchMedia?.('(display-mode: standalone)')?.matches;
  if(ios&&!installed){status.textContent='通知を使うには、更新後のHTTPS管理画面をSafariからホーム画面へ追加し直し、そのアイコンから開いてください。';return;}
  if(!window.isSecureContext||!('serviceWorker'in navigator)||!('PushManager'in window)||!('Notification'in window)){
    status.textContent='iPhoneではiOS 16.4以降のHTTPS管理画面を「ホーム画面に追加」して開いてください。';return;
  }
  if(location.origin==='https://tk75attractions.github.io'){status.textContent='通知はTailscaleのHTTPS管理画面で有効にできます。';return;}
  async function refresh(){
    if(busy||document.hidden)return;
    try{
      const response=await fetch('/api/push',{cache:'no-store',signal:AbortSignal.timeout(4000)});if(!response.ok)throw new Error();
      info=await response.json();
      if(!info.enabled){status.textContent='通知にはTailscaleのHTTPS接続と操作権限が必要です。ゲームPCのDashboardを更新・起動してください。';controls();return;}
      if(!registration){registration=await navigator.serviceWorker.register('/sw.js',{scope:'/'});await navigator.serviceWorker.ready;}
      const current=await registration.pushManager.getSubscription();
      if(current&&compatible(current)){
        if(!subscription){await send('subscribe',current.toJSON());subscription=current;status.textContent='異常通知が有効です。'}
      }else if(current){subscription=current;status.textContent='通知サーバーの鍵が変わりました。一度通知を停止して、有効にし直してください。';}
      else {subscription=null;status.textContent=Notification.permission==='denied'?'通知が拒否されています。iPhoneの設定から通知の許可を変更してください。':'「通知を有効にする」を押して、異常通知の受信を許可してください。';}
      get('push-delivery').textContent={sent:'最新の通知：プッシュサービスが受付済み（端末への到達は端末で確認）',failed:'最新の通知：送信失敗。ゲームPCのインターネット接続を確認してください。',expired:'端末の購読が失効しました。通知を有効にし直してください。'}[info.lastDelivery]||'';
      controls();
    }catch{info=null;controls();status.textContent='通知サーバーに接続できません。Tailscaleの管理画面とゲームPCのDashboard更新を確認してください。';}
  }
  await refresh();document.addEventListener('visibilitychange',()=>{if(!document.hidden)void refresh();});setInterval(refresh,15000);
})();
