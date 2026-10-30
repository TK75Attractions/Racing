internal static class WiredSetup
{
    internal const string Page="""
<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>有線接続 | RACING CONTROL</title>
<style>body{margin:0;background:#10141d;color:#edf4fa;font:16px system-ui,sans-serif}main{max-width:740px;margin:5vh auto;padding:24px}small{color:#81a9c7;letter-spacing:.15em}h1{font-size:30px}p,li{line-height:1.7;color:#c7d4df}section{background:#1b2432;padding:24px;border-radius:14px;margin:24px 0}label{display:block;margin:16px 0 8px}input,select{box-sizing:border-box;width:100%;padding:12px;background:#10141d;color:inherit;border:1px solid #557084;border-radius:6px;font:inherit}button{margin:16px 12px 0 0;padding:12px 18px;border:0;border-radius:6px;background:#69b8ff;color:#102234;font:inherit;font-weight:700}button:disabled{opacity:.5}.ok{color:#67d5b6}#error{color:#ffb3a7}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:8px;border-bottom:1px solid #3a4c62}</style>
<main><small>RACING CONTROL / ETHERNET</small><h1>Safariから有線で操作</h1>
<p>iPhone → 対応LANアダプター → LANケーブル → Mac</p>
<p>アプリのインストール・開発者モード・インターネット共有は不要です。通常のUSBケーブル1本では、この方式は使えません。</p>
<section><strong>最初の設定</strong><ol><li>iPhoneとMacをLANケーブルでつなぐ。Macの既存ネットワークとは別の、有線ポートを使う。</li><li>MacのEthernetとiPhoneのEthernetで、IPv4を手動設定する。</li></ol>
<table><tr><th></th><th>Mac</th><th>iPhone</th></tr><tr><th>IP</th><td>192.168.77.1</td><td>192.168.77.2</td></tr><tr><th>サブネット</th><td>255.255.255.0</td><td>255.255.255.0</td></tr></table><p>この例ではルーター・DNSは空欄です。既存のネットワークと重なる場合は別のサブネットを使ってください。ここではOSのネットワーク設定を変更しません。</p></section>
<section><strong id="status" role="status">有線ポートを確認中…</strong><p id="detail"></p><div id="error" role="alert"></div>
<label for="nic">使用するMacの有線ポート / IP</label><select id="nic"></select><label for="peer">iPhoneのEthernet IP</label><input id="peer" value="192.168.77.2" inputmode="decimal">
<button id="start" disabled>この有線ポートで開始</button><button id="stop" disabled>待受を停止</button>
<div id="paired" hidden><label for="url">iPhoneのSafariで開くURL</label><input id="url" readonly><button id="copy">URLをコピー</button><p>選んだiPhoneのIPだけを許可します。IP変更・ケーブルの抜き差し後は、この画面で開始し直してください。</p></div></section>
<p>接続後、管理画面に「有線LAN / WebSocket」と表示されます。この画面の「開始」は管理用の待受だけを開始し、レースは開始しません。</p>
<script>
const $=id=>document.getElementById(id);let busy=false,signature='';
function show(info){$('status').textContent=info.listening?'有線ポートで待受中':'接続設定を待っています';$('status').className=info.listening?'ok':'';$('detail').textContent=info.message;$('paired').hidden=!info.url;$('url').value=info.url||'';$('stop').disabled=!info.listening;
const list=info.interfaces||[],next=JSON.stringify(list);if(signature!==next){const old=$('nic').value;$('nic').replaceChildren();for(const nic of list){const option=document.createElement('option');option.value=JSON.stringify(nic);option.textContent=nic.name+' / '+nic.address+' / '+nic.mask;$('nic').append(option);}if(list.some(nic=>JSON.stringify(nic)===old))$('nic').value=old;signature=next;}$('start').disabled=list.length===0;if(!list.length)$('detail').textContent='使用できるEthernetのIPv4がありません。LANアダプターとMac側の手動IP設定を確認してください。';}
async function update(){if(busy)return;busy=true;try{const r=await fetch('/api/wired',{cache:'no-store',signal:AbortSignal.timeout(3000)});if(!r.ok)throw Error();show(await r.json());}catch{$('status').textContent='補助アプリに接続できません';$('start').disabled=true;$('stop').disabled=true;$('paired').hidden=true;$('url').value='';}finally{busy=false;}}
async function action(path,body){if(busy)return;busy=true;$('error').textContent='';try{const r=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(10000)});const info=await r.json();if(!r.ok)throw Error(info.message);show(info);}catch(e){$('error').textContent=e.message||'接続設定を確認してください。';}finally{busy=false;}}
$('start').onclick=()=>{if($('nic').value)action('/api/wired',{...JSON.parse($('nic').value),peer:$('peer').value.trim()});};$('stop').onclick=()=>action('/api/wired/stop',{});$('copy').onclick=async()=>{try{await navigator.clipboard.writeText($('url').value);}catch{$('url').select();}};update();setInterval(update,2000);
</script></main></html>
""";
}
