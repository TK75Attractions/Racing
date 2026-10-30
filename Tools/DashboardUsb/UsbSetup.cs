internal static class UsbSetup
{
    internal const string Page = """
<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>USB接続 | RACING CONTROL</title>
<style>body{margin:0;background:#10141d;color:#edf4fa;font:16px system-ui,sans-serif}main{max-width:700px;margin:8vh auto;padding:28px}small{color:#81a9c7;letter-spacing:.15em}h1{font-size:32px}p,li{line-height:1.8;color:#c7d4df}section{background:#1b2432;padding:24px;border-radius:14px;margin:28px 0}label{display:block;margin:16px 0 8px}input{box-sizing:border-box;width:100%;padding:12px;color:inherit;background:#10141d;border:1px solid #557084;border-radius:6px}button{margin-top:12px;padding:12px 20px;border:0;border-radius:6px;background:#69b8ff;color:#102234;font:inherit;font-weight:700}button:disabled{opacity:.5}.ok{color:#67d5b6}#feedback{min-height:24px;font-size:14px}</style>
<main><small>RACING CONTROL / USB</small><h1>USBで管理画面を開く</h1>
<p>ケーブル経由で接続し、TailscaleやWi-Fiを通さずに操作します。</p>
<ol><li>端末をUSBケーブルでこのPCにつなぐ。</li><li>iPhone / 対応iPadの「インターネット共有」をオンにして、PCを「信頼」する。</li><li>下のUSB用URLをコピーし、接続した端末のSafariなどで開く。</li></ol>
<section><strong id="status" role="status">USB接続を確認中…</strong><p id="detail"></p><label for="url">USB用URL</label><input id="url" readonly><button id="copy" disabled>URLをコピー</button><div id="feedback" role="status"></div></section>
<p>通常のDashboard中継は起動したまま使います。ケーブルを抜いた場合は、通常のTailscale用URLから開き直してください。再接続後は新しいUSB用URLを使います。</p>
<p>Wi-FiモデルのiPadは、通常のUSBケーブルだけではこのUSBネットワークを利用できません。iPhone / Wi-Fi + Cellular対応iPadでは、契約回線のインターネット共有が必要です。</p>
<script>
const $=id=>document.getElementById(id);let busy=false;
async function update(){if(busy)return;busy=true;try{const response=await fetch('/api/usb',{cache:'no-store',signal:AbortSignal.timeout(3000)});if(!response.ok)throw Error();const info=await response.json();$('status').textContent=info.connected?'USB接続を確認しました':'USB接続を待っています';$('status').className=info.connected?'ok':'';$('detail').textContent=info.error||(info.connected?'このURLをUSB接続した端末で開いてください。':'ケーブル・インターネット共有・「信頼」を確認してください。');$('url').value=info.url||'';$('copy').disabled=!info.connected;}catch{$('status').textContent='USB補助アプリに接続できません';$('detail').textContent='Start USB Dashboardを開き直してください。';$('url').value='';$('copy').disabled=true;}finally{busy=false;}}
$('copy').onclick=async()=>{try{await navigator.clipboard.writeText($('url').value);$('feedback').textContent='コピーしました。接続した端末のブラウザーに貼り付けてください。';}catch{$('url').select();$('feedback').textContent='URLを選択しました。コピーしてください。';}};update();setInterval(update,2000);
</script></main></html>
""";
}
