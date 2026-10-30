internal static class UsbSetup
{
    internal const string Page = """
<!doctype html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>USB接続 | RACING CONTROL</title>
<style>body{margin:0;background:#10141d;color:#edf4fa;font:16px system-ui,sans-serif}main{max-width:700px;margin:8vh auto;padding:28px}small{color:#81a9c7;letter-spacing:.15em}h1{font-size:32px}p,li{line-height:1.8;color:#c7d4df}section{background:#1b2432;padding:24px;border-radius:14px;margin:28px 0}label{display:block;margin:16px 0 8px}input{box-sizing:border-box;width:100%;padding:12px;color:inherit;background:#10141d;border:1px solid #557084;border-radius:6px}button{margin-top:12px;padding:12px 20px;border:0;border-radius:6px;background:#69b8ff;color:#102234;font:inherit;font-weight:700}button:disabled{opacity:.5}.ok{color:#67d5b6}#feedback{min-height:24px;font-size:14px}</style>
<main><small>RACING CONTROL / USB</small><h1>USBで管理画面を開く</h1>
<p>iPhoneの専用アプリから、USBケーブルだけで操作します。インターネット共有は不要です。</p>
<ol><li>初回だけ、iPhoneにRacing USBアプリをインストールする。</li><li>iPhoneをこのMacにUSBでつなぎ、表示されたら「信頼」を選ぶ。</li><li>アプリを開き、下の8けたの接続コードを入力する。</li></ol>
<section><strong id="status" role="status">USB接続を確認中…</strong><p id="detail"></p><label for="code">iPhoneアプリ用 接続コード</label><input id="code" readonly style="font-size:32px;letter-spacing:.15em"><div id="tether" hidden><label for="url">テザリング用URL</label><input id="url" readonly><button id="copy" disabled>URLをコピー</button></div><div id="feedback" role="status"></div></section>
<p>通常のDashboard中継は起動したまま使います。ケーブルを抜くとアプリの操作は止まります。つなぎ直すとUSB経由で再接続します。</p>
<p>アプリの初回インストールにはXcodeの署名設定と、iPhone側の確認が必要です。インストール後の管理画面・状態・映像・操作はUSBだけで通信します。</p>
<script>
const $=id=>document.getElementById(id);let busy=false;
async function update(){if(busy)return;busy=true;try{const response=await fetch('/api/usb',{cache:'no-store',signal:AbortSignal.timeout(3000)});if(!response.ok)throw Error();const info=await response.json();$('status').textContent=info.connected?'USB直通で接続中':'USB接続を待っています';$('code').value=info.iphone?.code||'';$('tether').hidden=!info.url;$('status').className=info.connected?'ok':'';$('detail').textContent=info.error||info.iphone?.state||'Racing USBアプリとケーブルを確認してください。';$('url').value=info.url||'';$('copy').disabled=!info.connected;}catch{$('status').textContent='USB補助アプリに接続できません';$('detail').textContent='Start USB Dashboardを開き直してください。';$('url').value='';$('copy').disabled=true;}finally{busy=false;}}
$('copy').onclick=async()=>{try{await navigator.clipboard.writeText($('url').value);$('feedback').textContent='コピーしました。接続した端末のブラウザーに貼り付けてください。';}catch{$('url').select();$('feedback').textContent='URLを選択しました。コピーしてください。';}};update();setInterval(update,2000);
</script></main></html>
""";
}
