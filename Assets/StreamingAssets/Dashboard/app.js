const API = location.origin === 'http://127.0.0.1:8765' ? '' : 'http://127.0.0.1:8765';
const $ = (id) => document.getElementById(id);
const labels = { Title: 'タイトル', Countdown: 'カウントダウン', Game: 'レース中', Goal: 'ゴール処理中', Result: '結果表示' };
const details = { Title: '開始操作を待っています', Countdown: 'スタートまでまもなく', Game: 'レース進行中', Goal: '完走処理中', Result: '次のレースを選択できます' };
const actions = { start: 'Title', result: 'Game', retry: 'Result', title: 'Result' };
let connected = false;
let currentState = null;
let lastFrameAt = 0;
let frameBusy = false;
const frameUrls = [null, null];

function setConnection(isConnected) {
  connected = isConnected;
  const badge = $('connection');
  badge.classList.toggle('online', isConnected);
  badge.classList.toggle('offline', !isConnected);
  badge.querySelector('span').textContent = isConnected ? 'UNITY 接続中' : 'UNITY 未接続';
  $('footer-status').textContent = isConnected ? 'Unity からデータを受信中' : 'Unity との接続を確認中';
  if (!isConnected) {
    currentState = null;
    $('game-state').textContent = '接続待機';
    $('state-detail').textContent = 'Unity を起動してください';
    $('race-time').textContent = '00:00.0';
    $('goal-lap').innerHTML = '--<span class="unit"> LAPS</span>';
    $('serial-count').textContent = '--';
    $('parse-errors').textContent = '--';
    $('serial-sub').textContent = 'ポート未接続';
    $('serial-port').textContent = 'PORT —';
    $('port-status').textContent = '待機中';
    $('port-status').classList.remove('active');
    $('input-mode').textContent = '—';
    $('last-result').textContent = '—';
    $('serial-log').innerHTML = '<div class="empty-log">シリアル入力を待っています。Unity の接続状態を確認してください。</div>';
    for (const button of document.querySelectorAll('[data-action]')) button.disabled = true;
    for (let i = 1; i <= 2; i++) {
      $('player-status-' + i).textContent = '待機中';
      $('player-status-' + i).classList.remove('active');
      $('lap-' + i).textContent = '-- / --';
      $('speed-' + i).innerHTML = '-- <small>km/h</small>';
      $('pedal-' + i).textContent = '--';
      $('steer-' + i).textContent = '--';
      $('feed-' + i).hidden = true;
      $('placeholder-' + i).hidden = false;
      if (frameUrls[i - 1]) URL.revokeObjectURL(frameUrls[i - 1]);
      frameUrls[i - 1] = null;
    }
  }
}

function formatTime(seconds) {
  const value = Math.max(0, Number(seconds) || 0);
  return String(Math.floor(value / 60)).padStart(2, '0') + ':' + String(Math.floor(value % 60)).padStart(2, '0') + '.' + Math.floor((value % 1) * 10);
}

function render(snapshot) {
  setConnection(true);
  currentState = snapshot.state;
  $('game-state').textContent = labels[snapshot.state] || snapshot.state;
  $('state-detail').textContent = snapshot.state === 'Countdown' ? `あと ${Math.ceil(snapshot.countdown)} 秒` : (details[snapshot.state] || '状態を確認中');
  $('race-time').textContent = formatTime(snapshot.raceTime);
  $('goal-lap').innerHTML = `${snapshot.goalLap || '--'}<span class="unit"> LAPS</span>`;
  $('serial-count').textContent = (snapshot.serial.received ?? 0).toLocaleString('ja-JP');
  $('parse-errors').textContent = (snapshot.serial.errors ?? 0).toLocaleString('ja-JP');
  $('serial-sub').textContent = snapshot.serial.portOpen ? 'RAW LINES RECEIVED' : 'ポート未接続';
  $('serial-port').textContent = 'PORT ' + (snapshot.serial.port || '—');
  $('port-status').textContent = snapshot.serial.portOpen ? '接続中' : '未接続';
  $('port-status').classList.toggle('active', snapshot.serial.portOpen);
  $('input-mode').textContent = snapshot.inputMode || '—';
  $('last-result').textContent = snapshot.serial.lastResult || '—';
  for (const player of snapshot.players || []) {
    const n = player.number;
    if (n !== 1 && n !== 2) continue;
    const status = $('player-status-' + n);
    status.textContent = player.connected ? '入力中' : '入力待機';
    status.classList.toggle('active', player.connected);
    $('lap-' + n).textContent = `${player.lap ?? 0} / ${snapshot.goalLap || '--'}`;
    $('speed-' + n).innerHTML = `${Math.max(0, player.speed || 0).toFixed(0)} <small>km/h</small>`;
    $('pedal-' + n).textContent = (player.pedal || 0).toFixed(2);
    $('steer-' + n).textContent = (player.steering || 0).toFixed(2);
  }
  for (const button of document.querySelectorAll('[data-action]')) button.disabled = actions[button.dataset.action] !== currentState;
  $('control-note').textContent = snapshot.state === 'Game' ? '「結果を表示」はレースを終了し、現在の記録で結果画面へ進みます。' : '現在のゲーム状態に応じて操作できます。';
  renderLog(snapshot.serial.lines || []);
}

function renderLog(lines) {
  const log = $('serial-log');
  if (!lines.length) {
    log.innerHTML = '<div class="empty-log">シリアル入力を待っています。</div>';
    return;
  }
  const oldTop = log.scrollTop;
  const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 35;
  log.replaceChildren(...lines.slice().reverse().map((entry) => {
    const row = document.createElement('div');
    row.className = 'log-row';
    const time = document.createElement('span'); time.textContent = entry.time;
    const status = document.createElement('span');
    status.textContent = entry.status;
    status.className = entry.status === 'OK' ? 'ok' : entry.status === 'PARTIAL' ? 'partial' : 'bad';
    const data = document.createElement('code'); data.textContent = entry.line;
    row.append(time, status, data);
    return row;
  }));
  if (!atBottom) log.scrollTop = oldTop;
}

async function apiFetch(path, options = {}) {
  return fetch(API + path, { cache: 'no-store', ...(API ? { targetAddressSpace: 'loopback' } : {}), ...options });
}

async function poll() {
  try {
    const response = await apiFetch('/api/status');
    if (!response.ok) throw new Error('HTTP ' + response.status);
    render(await response.json());
    if (Date.now() - lastFrameAt >= 2000 && !frameBusy) {
      lastFrameAt = Date.now();
      void refreshFrames();
    }
  } catch {
    setConnection(false);
  }
}

async function refreshFrames() {
  frameBusy = true;
  try {
    for (let n = 1; n <= 2; n++) {
      try {
        const response = await apiFetch(`/api/player/${n}/frame`);
        if (!response.ok) throw new Error('Camera unavailable');
        const url = URL.createObjectURL(await response.blob());
        const img = $('feed-' + n);
        const old = frameUrls[n - 1];
        img.onload = () => { if (old) URL.revokeObjectURL(old); };
        img.src = url;
        frameUrls[n - 1] = url;
        img.hidden = false;
        $('placeholder-' + n).hidden = true;
      } catch {
        $('feed-' + n).hidden = true;
        $('placeholder-' + n).hidden = false;
      }
    }
  } finally { frameBusy = false; }
}

document.querySelectorAll('[data-action]').forEach((button) => {
  button.addEventListener('click', async () => {
    if (!connected || actions[button.dataset.action] !== currentState) return;
    button.disabled = true;
    $('control-note').textContent = 'Unity に操作を送信中…';
    try {
      const response = await apiFetch('/api/action/' + button.dataset.action, { method: 'POST' });
      if (!response.ok) throw new Error('操作を受け付けられませんでした');
      await poll();
    } catch {
      $('control-note').textContent = '操作に失敗しました。接続とゲーム状態を確認してください。';
      button.disabled = false;
    }
  });
});

function updateClock() { $('clock').textContent = new Date().toLocaleTimeString('ja-JP', { hour12: false }); }
updateClock();
setInterval(updateClock, 1000);
poll();
setInterval(poll, 1000);
