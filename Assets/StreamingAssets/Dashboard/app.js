const localMode = location.origin !== 'https://tk75attractions.github.io';
const API = '';
// USB tethering uses a private HTTP address; randomUUID requires a secure context.
// getRandomValues remains available there, so command IDs retain cryptographic randomness.
function newRequestId() {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
}
let liveController = null;
let latestSnapshot = null;
let logSignature = '';
let rawPaused = false, rawSignature = '', rawPending = [], rawGap = false;
let rawElements = new Map();
let canSendSerial = false;
let latestCanControl = false;
let settingsBusy = false;
let operatorStatus = null, operatorBusy = false, operatorPolling = false;
let lastMaintenanceActive = null;
let featureSignature = null;
const featureElements = new Map();
let inputControllerId = null, latestInputAllowed = false;
const remotePlayers = [0, 1].map(() => ({ active:false, epoch:0, sequence:0, inFlight:false, started:false, generation:null, pointers:new Map(), anchor:null, tail:Promise.resolve() }));
const $ = (id) => document.getElementById(id);
const labels = { Title: 'タイトル', CarSelection: '車選択', Tutorial: 'チュートリアル', Countdown: 'カウントダウン', Game: 'レース中', Goal: 'ゴール処理中', Result: '結果表示' };
const details = { Title: '開始操作を待っています', CarSelection: '参加者が車を選んでいます', Tutorial: '参加者が操作を練習しています', Countdown: 'スタートまでまもなく', Game: 'レース進行中', Goal: '完走処理中', Result: '次のレースを選択できます' };
const actions = { start: 'Title', tutorial: 'CarSelection', countdown: 'Tutorial', result: 'Game', retry: 'Result', title: 'Result' };
function actionAvailable(action, snapshot) {
  if (snapshot?.settings?.maintenanceActive) return false;
  const preparing = snapshot?.playerProgress || [];
  if (action === 'tutorial' && snapshot?.state === 'Tutorial')
    return preparing.some(player => ['Title', 'CarSelection'].includes(player.state));
  if (action === 'countdown' && preparing.some(player => player.state !== 'Tutorial')) return false;
  return actions[action] === snapshot?.state;
}
let commandRequest = null, commandResultSignature = '';
let alertSession = '', alertItems = new Map();
let connected = false;
let currentState = null;
let lastFrameAt = 0;
let frameBusy = false;
let frameGeneration = 0;
const frameUrls = [null, null];

const dashboardPages = {
  home: ['ホーム', '機器の接続・要確認事項・プレイヤーの復帰と入力を、ひと目で。', 'HOME'],
  statistics: ['プレイ集計', '本日のプレイ人数とランキング、日別の人数を確認。', 'PLAY STATISTICS'],
  race: ['レース運営', '進行の操作、プレイヤー映像、順位をまとめて確認。', 'RACE OPERATIONS'],
  devices: ['機器・入力', '接続状態と、ハンドル・ペダルの反応を確認。', 'DEVICES & INPUT'],
  settings: ['ゲーム設定', '音量・出力先・レースルールを調整。', 'GAME SETTINGS'],
  remote: ['遠隔操作', 'プレイヤーを選び、必要なときだけ操作を引き受けます。', 'REMOTE CONTROL'],
  diagnostics: ['診断・ログ', '現在の警告と、接続・操作の履歴を確認。', 'DIAGNOSTICS']
};
let activePage = 'home';

function dashboardRoute(hash) {
  const route = String(hash || '').replace(/^#/, '');
  // Preserve older bookmarks into the former single-page dashboard.
  const legacy = { 'hardware-title':'devices', 'serial-title':'diagnostics', 'settings-title':'settings',
    'controls-title':'race', 'cameras-title':'race', 'keyboard-input-title':'remote' };
  return Object.hasOwn(dashboardPages, route) ? route : legacy[route] || 'home';
}

function showDashboardPage(page, focus = false) {
  if (!Object.hasOwn(dashboardPages, page)) page = 'home';
  const changed = activePage !== page;
  if (activePage === 'remote' && page !== 'remote') {
    stopAllRemoteInputs();
    renderRemoteInputs(latestSnapshot?.inputControl, latestInputAllowed);
  }
  activePage = page;
  document.querySelectorAll('[data-page]').forEach(panel => { panel.hidden = panel.dataset.page !== page; });
  document.querySelectorAll('.nav-item[data-page-link]').forEach(link => {
    if (link.dataset.pageLink === page) link.setAttribute('aria-current', 'page');
    else link.removeAttribute('aria-current');
  });
  const [title, description, kicker] = dashboardPages[page];
  const accent = document.createElement('span'); accent.className = 'accent'; accent.textContent = '.';
  $('page-title').replaceChildren(title, accent);
  $('page-description').textContent = description;
  $('page-kicker').textContent = 'CONTROL DESK / ' + kicker;
  document.title = `${title} | RACING CONTROL`;
  // Invalidate any camera request belonging to the screen we just left.
  if (changed) frameGeneration++;
  if (focus) $('page-title').focus({preventScroll:true});
  if (changed && page === 'race' && localMode) void refreshFrames();
  if (page === 'statistics') void pollStatistics();
}

function renderHome(snapshot) {
  $('home-input-mode').textContent = !snapshot ? '—' : $('input-mode').textContent;
  for (const n of [1, 2]) {
    const player = snapshot?.players?.find(value => value.number === n);
    const progress = snapshot?.playerProgress?.find(value => value.number === n);
    const participant = snapshot?.participants?.find(value => value.number === n);
    const status = $('home-player-status-' + n);
    status.textContent = !snapshot ? '待機中' : progress ? labels[progress.state] || '状態確認中' : labels[snapshot.state] || '状態確認中';
    status.classList.toggle('active', !!snapshot && !!player?.connected);
    const detail = progress?.step === 'Results' ? '結果を表示中' : progress?.step === 'Spectating' ? '相手のゴールを待っています' : progress?.step === 'Withdrawn' ? '途中棄権・相手の走行を待機' :
      participant?.status === 'Finished' ? '完走' : participant?.status === 'DNF' ? '未完走' :
      progress?.step === 'Menu' ? '中断メニューを表示中' : snapshot ? details[progress?.state || snapshot.state] || '状態を確認中' : 'プレイヤーの状態を待っています';
    $('home-player-detail-' + n).textContent = [progress?.carName, detail].filter(Boolean).join(' · ');
    $('home-lap-' + n).textContent = player ? `${player.lap ?? 0} / ${snapshot.goalLap || '—'}` : '—';
    $('home-speed-' + n).textContent = player && Number.isFinite(player.speed) ? `${Math.max(0, player.speed).toFixed(0)} km/h` : '—';
  }
  renderAttention(snapshot);
}

function renderAttention(snapshot) {
  // Summarize current observations, never the cumulative parse-error counter.
  const items = [];
  if (!snapshot) items.push({text: localMode ? 'Unity の接続待ちです。ゲームPCで Play 中か確認してください。' : '遠隔データの接続を待っています。'});
  else {
    const serial = snapshot.serial || {}, h = snapshot.hardware;
    const hardwareRequired = !/^keyboard/i.test(snapshot.settings?.inputMode || snapshot.inputMode || '') && HardwareDiagnostics.required(snapshot, 0);
    if (hardwareRequired) {
      if (!serial.portOpen) items.push({text:'実機のシリアル接続を確認してください。', issue:true});
      else if (!h?.sessionOpen || !h.recent) items.push({text:'装置からの応答を待っています。', issue:true});
      else if (h.state !== 'READY' || !h.calibrated || !h.angleLocked) items.push({text:'機器を準備中です。「機器・入力」で進行を確認できます。'});
      else if (!h.inputFresh || h.invalidTelemetry) items.push({text:'実機入力の更新・形式を確認してください。', issue:true});
      if (h?.sessionOpen && h.recent && ((h.ads === 'Fault' && HardwareDiagnostics.required(snapshot, 3)) ||
          h.imus?.some((imu, index) => imu.status === 'Fault' && HardwareDiagnostics.required(snapshot, index + 1))))
        items.push({text:'センサーに要確認の通知があります。「機器・入力」で対象を確認してください。', issue:true});
    }
    if (alertSession === (snapshot.sessionId || '')) {
      for (const [key, item] of alertItems)
        if (item.resolvedAt == null && key !== 'port') items.push({text:item.text, issue:true});
    }
    if (!items.length) items.push({text:hardwareRequired ? '現在、要確認の通知はありません。' : '現在、要確認の通知はありません。実機入力は未使用です。'});
  }
  const issues = items.filter(item => item.issue), count = issues.length;
  $('global-notice').hidden = !count;
  const title = `${count}件の確認が必要です`, detail = issues[0]?.text || '';
  if ($('global-notice-title').textContent !== title) $('global-notice-title').textContent = title;
  if ($('global-notice-detail').textContent !== detail) $('global-notice-detail').textContent = detail;
  $('nav-issue-count').hidden = !count;
  $('nav-issue-count').textContent = String(count);
  $('home-issue-status').textContent = !snapshot ? '接続待ち' : count ? `${count}件 要確認` : items[0].text.includes('準備中') ? '準備中' : '通知なし';
  $('home-issue-status').classList.toggle('fault', count > 0);
  // Text nodes keep device-provided messages inert. Do not interrupt screen readers on every snapshot.
  const signature = JSON.stringify(items.map(item => item.text));
  if ($('home-issue-list').dataset.signature !== signature) {
    $('home-issue-list').dataset.signature = signature;
    $('home-issue-list').replaceChildren(...items.slice(0, 3).map(item => {
      const li = document.createElement('li'); li.textContent = item.text; return li;
    }));
    if (items.length > 3) {
      const li = document.createElement('li'); li.textContent = `ほか ${items.length - 3} 件は診断・ログで確認できます。`;
      $('home-issue-list').append(li);
    }
  }
}

function setConnection(isConnected) {
  connected = isConnected;
  const badge = $('connection');
  badge.classList.toggle('online', isConnected);
  badge.classList.toggle('offline', !isConnected);
  badge.querySelector('span').textContent = isConnected ? (localMode ? 'UNITY 接続中' : '遠隔監視中') : (localMode ? 'UNITY 未接続' : '遠隔未接続');
  $('footer-status').textContent = isConnected ? 'Unity からデータを受信中' : 'Unity との接続を確認中';
  if (!isConnected) {
    stopAllRemoteInputs();
    renderRemoteInputs(null, false);
    frameGeneration++;
    currentState = null;
    $('game-state').textContent = '接続待機';
    $('state-detail').textContent = localMode ? 'Unity を起動してください' : '遠隔データを待っています';
    $('race-time').textContent = '00:00.0';
    renderPerformance(null);
    setGoalLap('--');
    $('serial-count').textContent = '--';
    $('parse-errors').textContent = '--';
    $('serial-sub').textContent = 'ポート未接続';
    $('serial-port').textContent = 'PORT —';
    $('port-status').textContent = '待機中';
    $('port-status').classList.remove('active');
    $('input-mode').textContent = '—';
    $('last-result').textContent = '—';
    $('serial-alerts').hidden = true;
    $('serial-alert-list').replaceChildren();
    serialAlertSignature = '';
    logSignature = '';
    renderTelemetry(null);
    rawPending = []; rawSignature = ''; rawGap = false; renderRaw([], false, true);
    renderHardware(null); renderPlayerProgress(null); setSerialControls(false);
    if($('diagnostics-dialog').open){
      diagnosticGeneration++;renderHardwareDiagnosis();
      $('diagnostics-progress').textContent='Unityへの接続が切れました。再接続後に診断を開き直してください。';
    }
    $('serial-command-status').textContent = 'Unityへの再接続を待っています。';
    if (commandRequest) showCommandResult('unknown', '接続が中断しました。再接続後に実行結果を確認します。');
    updateFlow(null);
    renderParticipants(null);
    $('serial-log').innerHTML = '<div class="empty-log">シリアル入力を待っています。Unity の接続状態を確認してください。</div>';
    updateActions(null, false);
    renderSettings(null, false);
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
    renderHome(null);
  }
}

function formatTime(seconds) {
  const value = Math.max(0, Number(seconds) || 0);
  return String(Math.floor(value / 60)).padStart(2, '0') + ':' + String(Math.floor(value % 60)).padStart(2, '0') + '.' + Math.floor((value % 1) * 10);
}

function setGoalLap(value) {
  const unit = document.createElement('span');
  unit.className = 'unit';
  unit.textContent = ' LAPS';
  $('goal-lap').replaceChildren(String(value), unit);
}

function updateFlow(state) {
  const order = ['Title', 'Tutorial', 'Countdown', 'Game', 'Goal', 'Result'];
  const position = order.indexOf(state);
  document.querySelectorAll('[data-flow-state]').forEach((step, index) => {
    step.classList.toggle('current', position === index);
    step.classList.toggle('complete', position > index);
    if (position === index) step.setAttribute('aria-current', 'step');
    else step.removeAttribute('aria-current');
  });
  document.querySelectorAll('.flow-line').forEach((line, index) =>
    line.classList.toggle('complete', position > index));
  $('flow-state').textContent = position < 0 ? 'Unity の状態を待っています' :
    `現在：${labels[state]}（${position + 1} / ${order.length}）`;
}

function updateActions(snapshot, canControl) {
  let available = 0;
  for (const button of document.querySelectorAll('[data-action]')) {
    if (button.dataset.action === 'start') button.textContent = ['Quick', 'NoTutorial'].includes(snapshot?.settings?.activePreset) ? '▶ レースを始める（練習なし）' : '▶ 練習を始める';
    const applicable = actionAvailable(button.dataset.action, snapshot);
    button.hidden = !applicable;
    button.disabled = !canControl || !applicable;
    button.classList.toggle('primary', applicable && button.dataset.action !== 'title');
    if (applicable) available++;
  }
  $('action-empty').hidden = available > 0;
  $('action-empty').textContent = !snapshot ? 'Unity の接続を待っています' :
    ['Countdown', 'Goal'].includes(snapshot.state) ? '◷ 自動で次の画面へ進みます' : '現在、進行操作はありません';
}

function renderPerformance(performance) {
  const valid = Number.isFinite(performance?.fps) && performance.fps > 0 &&
    Number.isFinite(performance?.frameTimeMs) && performance.frameTimeMs > 0;
  $('fps').textContent = valid ? performance.fps.toFixed(1) : '--';
  $('frame-time').textContent = valid ? `${performance.frameTimeMs.toFixed(1)} ms / frame · 約0.5秒平均` : 'Unity全体 · 計測待ち';
}

function render(snapshot, { canControl = localMode, canInput = canControl } = {}) {
  setConnection(true);
  // An HTTP acknowledgement can arrive before the next WebSocket snapshot.
  if (latestSnapshot && latestSnapshot.sessionId === snapshot.sessionId && latestSnapshot.settingsRevision > snapshot.settingsRevision)
    snapshot = { ...snapshot, settings: latestSnapshot.settings, settingsRevision: latestSnapshot.settingsRevision };
  latestSnapshot = snapshot;
  latestCanControl = canControl;
  renderSettings(snapshot.settings, canControl);
  renderRemoteInputs(snapshot.inputControl, canInput && !snapshot.settings?.maintenanceActive);
  currentState = snapshot.state;
  updateFlow(snapshot.state);
  $('game-state').textContent = snapshot.settings?.maintenanceActive ? 'メンテナンス中' : labels[snapshot.state] || snapshot.state;
  $('state-detail').textContent = snapshot.settings?.maintenanceActive ? '全ゲーム画面で調整中の表示・進行停止中' : snapshot.state === 'Countdown' ? `あと ${Math.ceil(snapshot.countdown)} 秒` : (details[snapshot.state] || '状態を確認中');
  $('race-time').textContent = formatTime(snapshot.raceTime);
  renderPerformance(snapshot.performance);
  setGoalLap(snapshot.goalLap || '--');
  $('serial-count').textContent = (snapshot.serial.received ?? 0).toLocaleString('ja-JP');
  $('parse-errors').textContent = (snapshot.serial.errors ?? 0).toLocaleString('ja-JP');
  $('parse-errors').classList.toggle('error-count', snapshot.serial.errors > 0);
  $('serial-sub').textContent = snapshot.serial.portOpen ? '受信した行数 / 累計' : 'ポート未接続';
  $('serial-port').textContent = 'PORT ' + (snapshot.serial.port || '—');
  $('port-status').textContent = snapshot.serial.portOpen ? '接続中' : '未接続';
  $('port-status').classList.toggle('active', snapshot.serial.portOpen);
  $('input-mode').textContent = snapshot.settings?.inputMode === 'mixed' ? '↔ P1ハンドル / ⌨ 代替ペダル・P2' : /^keyboard/i.test(snapshot.inputMode || '') ? '⌨ キーボード' :
    /serial/i.test(snapshot.inputMode || '') ? '↔ シリアル機器' : snapshot.inputMode || '—';
  $('last-result').textContent = serialStatusLabel(snapshot.serial.lastResult);
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
  updateActions(snapshot, canControl);
  if (localMode) $('control-note').textContent = snapshot.settings?.maintenanceActive ? 'メンテナンスを解除すると進行操作を再開できます。' : snapshot.state === 'Game' ? '「結果を表示」はレースを終了し、現在の記録で結果画面へ進みます。' : '現在のゲーム状態に応じて操作できます。';
  renderSerialAlerts(snapshot.serial, snapshot.inputMode, snapshot);
  renderLog(snapshot.serial.lines || []);
  renderTelemetry(snapshot.telemetry || null);
  renderParticipants(snapshot);
  renderPlayerProgress(snapshot);
  renderHardware(snapshot);
  renderRaw(snapshot.rawLines || [], snapshot.rawGap || false);
  setSerialControls(canControl && snapshot.serial.portOpen && snapshot.hardware?.sessionOpen && snapshot.hardware?.recent);
  renderCommandResult(snapshot);
  renderHome(snapshot);
}

const drivingLabels = { Stopped: '停止', Driving: '走行中', Airborne: '空中',
  Reversing: '後退復旧', Yielding: '接触後の譲り', Stalled: '停止・復旧待ち', Finished: '完走', DNF: '未完走' };
const driverLabel = (number) => number >= 3 ? `CPU ${number - 2}` : `P${number}`;
const metric = (value) => Number.isFinite(value) ? value : 0;

function renderParticipants(snapshot) {
  const entries = (snapshot?.participants || []).slice().sort((a, b) => a.position - b.position);
  const count = snapshot?.participantCount ?? entries.length;
  $('participant-count').textContent = snapshot ? `${count}台 / CPU ${entries.filter(entry => entry.isCpu).length}台` : '接続待機';
  $('participant-empty').hidden = entries.length > 0;
  $('participant-empty').textContent = !snapshot ? 'Unity の接続を待っています。' :
    snapshot.participantCount == null && !snapshot.participants ? '参加車両のデータがありません。Unity を更新してください。' : '車両の生成を待っています。';
  $('participant-rows').replaceChildren(...entries.map((entry) => {
    const row = document.createElement('tr');
    row.className = entry.isCpu ? 'cpu-row' : '';
    const cell = (text, detail) => {
      const td = document.createElement('td');
      const value = document.createElement('strong'); value.textContent = text; td.append(value);
      if (detail) { const small = document.createElement('small'); small.textContent = detail; td.append(small); }
      row.append(td); return td;
    };
    cell(`${entry.position} / ${count}`);
    const auto=snapshot.settings?.features?.some(feature=>feature.id===(entry.number===1?'PlayerOneAutodrive':'PlayerTwoAutodrive')&&feature.enabled);
    cell(driverLabel(entry.number), entry.isCpu ? 'CPU' : auto ? '自動運転' : '人間');
    const status = cell(drivingLabels[entry.status] || '状態不明', entry.contactGrace ? '接触保護中' : null);
    if (entry.status === 'Finished') status.className = 'participant-finished';
    if (entry.status === 'Stalled' || entry.status === 'DNF') status.className = 'participant-warning';
    cell(`${entry.lap} / ${snapshot.goalLap || '--'}`);
    cell(`${metric(entry.speed).toFixed(0)} km/h`, entry.isCpu ? `目標 ${metric(entry.targetSpeed).toFixed(0)} km/h` : null);
    cell(`${metric(entry.progress).toFixed(1)} m`);
    const gap = metric(entry.gap);
    cell(entry.rivalNumber ? driverLabel(entry.rivalNumber) : '—', entry.rivalNumber ? `${gap >= 0 ? '+' : ''}${gap.toFixed(1)} m` : null);
    cell(entry.isCpu ? `${entry.recoveries || 0}回` : '—');
    return row;
  }));
}

const rgpStates = { BOOTING: '起動中', INITIALIZING: 'センサー初期化中', CALIBRATING: '校正中',
  WAIT_ANGLE: 'ハンドルの基準角度を待機中', READY: '2人分の入力が使用可能', RECOVERING: 'センサー接続を復旧中', FATAL: '継続できない異常' };
const rgpCommands = { OPEN_SESSION: 'セッション開始', PING: '応答確認', GET_STATUS: '状態照会', GET_CONFIG: '設定照会',
  ANGLE_ZERO: 'ハンドル基準のリセット', SET_BETA: '姿勢推定の調整', SET_RATE: '送信頻度の変更', SET_STREAM: '送信の切り替え',
  CALIBRATE: 'センサー校正', SCAN_I2C: 'I2C機器の検索' };
const rgpSubsystems = { SYSTEM: '装置', SERIAL: 'シリアル通信', PROTOCOL: '通信プロトコル', I2C: 'I2C通信',
  ADS1115: 'ペダル用センサー', IMU: '姿勢センサー', HANDLE: 'ハンドル', CALIBRATION: '校正' };
const rgpErrors = { INIT_FAILED: '初期化に失敗しました', BUS_ERROR: '通信バスで異常が発生しました', UNKNOWN_ERROR: '異常が発生しました',
  REINIT_FAILED: '再初期化に失敗しました', READ_FAILED: '読み取りに失敗しました', WRITE_FAILED: '書き込みに失敗しました',
  INVALID_STATE: '現在の状態では操作できません', TIMEOUT: '処理が時間内に完了しませんでした', NO_RESPONSE: '機器から応答がありません',
  WHOAMI_FAILED: '機器の識別情報を読めません', WHOAMI_MISMATCH: '接続機器の識別情報が一致しません', CONNECTION_LOST: '接続が失われました',
  ANGLE_REFERENCE_INVALID: 'ハンドルの基準角度が無効です', INVALID_COMMAND: 'コマンドが不正です', INVALID_ARGUMENT: '引数が不正です',
  SEQ_REUSE: '同じ要求番号が異なるコマンドで再使用されました' };
const rgpSettings = { TEL_RATE: '入力の送信頻度', IMU_RATE: '姿勢データの送信頻度', TEL_STREAM: '入力の送信',
  IMU_STREAM: '姿勢データの送信', DBG_STREAM: '診断ログの送信', BETA: '姿勢推定の調整値' };
const rgpTargets = { ALL: '全センサー', GYRO: 'ジャイロ', MAG: '磁気センサー' };
const rgpName = (table, key) => Object.hasOwn(table, key) ? table[key] : (key || '不明');
const rgpState = (state) => Object.hasOwn(rgpStates, state) ? rgpStates[state] : `未知の状態「${state || '不明'}」（入力は使用不可）`;
function rgpSetting(name, value) {
  const shown = /_STREAM$/.test(name) ? (value === '1' ? '有効' : value === '0' ? '停止' : value) :
    /_RATE$/.test(name) ? `${value} Hz` : value;
  return `${rgpName(rgpSettings, name)}：${shown}`;
}
function rgpText(text) {
  try { return decodeURIComponent(text); } catch { return '文章を復元できません'; }
}
function rgpCrc(body) {
  let crc = 0xFFFF;
  for (let i = 0; i < body.length; i++) {
    crc ^= body.charCodeAt(i) << 8;
    for (let bit = 0; bit < 8; bit++) crc = ((crc & 0x8000) ? (crc << 1) ^ 0x1021 : crc << 1) & 0xFFFF;
  }
  return crc.toString(16).toUpperCase().padStart(4, '0');
}
function rgpErrorExplanation(d, truncated = false) {
  const severity = { WARN: '警告', ERROR: 'エラー', FATAL: '重大なエラー' };
  let text = `${rgpName(severity, d[0])}：${rgpName(rgpSubsystems, d[1])}で${rgpName(rgpErrors, d[2])}。`;
  if (!Object.hasOwn(rgpErrors, d[2])) text = `${rgpName(severity, d[0])}：${rgpName(rgpSubsystems, d[1])}が未知のエラー「${d[2]}」を通知しました。`;
  const detail = d.slice(3);
  const textAt = detail.indexOf('TEXT');
  const reqAt = detail.indexOf('REQ');
  if (reqAt >= 0 && /^\d+$/.test(detail[reqAt + 1] || '')) text += ` 要求番号 ${detail[reqAt + 1]}。`;
  if (!truncated && textAt >= 0 && detail[textAt + 1]) text += ` 装置からの補足：${rgpText(detail[textAt + 1])}`;
  if (d[2] === 'WHOAMI_MISMATCH' && detail.length >= 3) text += ` アドレス ${detail[0]}、期待値 ${detail[1]}、実際 ${detail[2]}。`;
  else if (/^0x[0-9A-F]{2}$/.test(detail[0] || '')) text += ` 対象 ${detail[0]}。`;
  if (truncated) text += '（遠隔データの末尾が省略されています）';
  return text;
}

// Describe the already processed wire frame. Invalid or incomplete telemetry is never presented as input.
function explainSerialEntry(entry) {
  const line = String(entry.line || '');
  const status = entry.status || '';
  const level = ['ERROR', 'FATAL', 'INVALID', 'INCOMPATIBLE', 'ACK_FAILED', 'TIMEOUT'].includes(status) ? 'error' :
    ['WARN', 'PARTIAL', 'STALE'].includes(status) ? 'warning' : 'info';
  const result = (text, override = level) => ({ text, level: override });
  if (status === 'INVALID') return result('フレームを解析できないため、入力に反映していません。CRC・形式・値の範囲を確認してください。');
  if (status === 'INCOMPATIBLE') return result('RGPのバージョンが一致しません。入力の反映とコマンド送信を停止しています。');
  if (status === 'TIMEOUT') return result(`「${rgpName(rgpCommands, line)}」の応答がありません。同じ要求を2回再送しましたが、実行結果を確認できませんでした。`);
  if (status === 'STALE') return result('有効な入力の受信が時間内に確認できないため、両プレイヤーの入力を中立に戻しました。');
  const match = line.match(/^@([A-Z][A-Z0-9_.-]*),([0-9]+),([0-9]+)(?:,([^*]*))?\*([0-9A-F]{4})$/);
  if (!match || rgpCrc(line.slice(1, line.lastIndexOf('*'))) !== match[5]) {
    // Remote snapshots retain only 120 characters per line under the existing database rules.
    const prefix = line.match(/^@ERR,\d+,\d+,(WARN|ERROR|FATAL),([A-Z0-9_.-]+),([A-Z0-9_.-]+),?/);
    if (!match && line.length === 120 && !line.includes('*') && prefix && status === prefix[1])
      return result(rgpErrorExplanation(prefix.slice(1), true));
    if (status === 'ERROR') {
      if (line.startsWith('Failed to open shared serial controller')) return result('シリアルポートを開けませんでした。USB接続と、ほかのアプリがポートを使用していないか確認してください。');
      if (line.startsWith('Shared serial read failed:')) return result('シリアル受信に失敗しました。USB接続と装置の状態を確認してください。');
      if (line.startsWith('RGP command write failed:')) return result('装置へのコマンド送信に失敗しました。USB接続を確認してください。');
      return result('シリアル通信でエラーが発生しました。詳細は受信データを確認してください。');
    }
    return result(line.startsWith('@') ? 'フレームが不完全か、CRCを確認できません。遠隔配信では末尾が省略されることがあります。' : 'RGP/1形式の説明対象ではありません。受信データを確認してください。');
  }
  const d = match[4]?.split(',') || [];
  let text;
  switch (match[1]) {
    case 'TEL': return result('TELの受信値は最新値欄に表示します。');
    case 'CMD': text = `装置へ「${rgpName(rgpCommands, d[0])}」を送信しました。` + (d[0] === 'OPEN_SESSION' ? '' : d.length > 1 ? ` 引数：${d.slice(1).join('、')}` : ''); break;
    case 'ERR': return result(rgpErrorExplanation(d), d[0] === 'WARN' ? 'warning' : 'error');
    case 'EVT': {
      const events = { BOOT: '装置が起動しました', ADS_READY: 'ペダル用センサーの準備ができました', IMU_FOUND: '姿勢センサーを検出しました',
        ANGLE_LOCKED: '両ハンドルの基準角度が確定しました', ANGLE_UNLOCKED: 'ハンドルの基準角度が無効になりました', ANGLE_RESET: '両ハンドルの基準をリセットしました',
        I2C_LOST: 'センサーとの接続が失われました', I2C_RESTORED: 'センサーとの接続が復旧しました' };
      if (d[0] === 'STATE') return result(`装置の状態：${rgpState(d[1])}。`, d[1] === 'FATAL' ? 'error' : d[1] === 'RECOVERING' ? 'warning' : 'info');
      if (d[0] === 'CONFIG_CHANGED') text = `設定を変更しました。${rgpSetting(d[1], d[2])}`;
      else if (d[0] === 'CAL_START' || d[0] === 'CAL_DONE') text = `${rgpName(rgpTargets, d[2])}の校正を${d[0] === 'CAL_START' ? '開始' : '完了'}しました（${d[1] === 'AUTO' ? '起動時の自動処理' : `要求番号 ${d[1]}`}）。`;
      else if (/^CAL_(MAG|GYRO)_(START|DONE|SKIPPED)$/.test(d[0])) text = `${d[0].includes('_MAG_') ? '磁気センサー' : 'ジャイロ'}の校正を${d[0].endsWith('START') ? '開始しました' : d[0].endsWith('DONE') ? '完了しました' : '省略しました'}。`;
      else if (d[0] === 'I2C_SCAN_START') text = `I2C機器の検索を開始しました（要求番号 ${d[1]}）。`;
      else if (d[0] === 'I2C_SCAN_END') text = `I2C機器の検索が完了しました。${d[2]}台を検出しました（要求番号 ${d[1]}）。`;
      else text = Object.hasOwn(events, d[0]) ? `${events[d[0]]}。` : `未対応のイベント「${d[0]}」を受信しました。`;
      break;
    }
    case 'SYS':
      if (d[0] === 'HELLO') text = `装置が起動しました。RGP ${d[2]}、ファームウェア ${d[3]}。接続セッションを開始します。`;
      else if (d[0] === 'DEVICE') text = `${rgpName(rgpSubsystems, d[1])}を検出しました。I2Cアドレス ${d[2]}${d[3] ? `、識別値 ${d[3]}` : ''}。`;
      else if (d[0] === 'CONFIG') text = `装置の設定：${rgpSetting(d[1], d[2])}`;
      else if (d[0] === 'I2C_DEVICE') text = `I2Cアドレス ${d[2]} に機器を検出しました（要求番号 ${d[1]}）。`;
      else text = `未対応の装置情報「${d[0]}」を受信しました。`;
      break;
    case 'ACK': {
      const name = rgpName(rgpCommands, d[1]);
      const outcome = { OK: '成功しました', ACCEPTED: '受け付けました。完了通知を待っています', INVALID_ARG: `引数${d[3] || ''}が不正です`,
        UNSUPPORTED: '装置が対応していません', BUSY: 'ほかの処理を実行中です', NOT_READY: `装置の準備ができていません（${rgpState(d[3])}）`, FAILED: `失敗しました（${rgpName(rgpErrors, d[3])}）` };
      text = `「${name}」${Object.hasOwn(outcome, d[2]) ? outcome[d[2]] : `未知の応答「${d[2]}」のため失敗として扱います`}（要求番号 ${d[0]}）。`;
      if (d[2] === 'OK' && d[1] === 'GET_STATUS') text += ` 装置：${rgpState(d[3])}。ペダル用センサー${d[4] === '1' ? '正常' : '異常'}、姿勢センサー${d[5] === '1' ? '正常' : '異常'}、角度基準${d[6] === '1' ? '有効' : '無効'}、校正${d[7] === '1' ? '完了' : '未完了'}。`;
      if (d[2] === 'OK' && d[1] === 'GET_CONFIG') text += ' ' + d.slice(3).reduce((pairs, v, i, a) => i % 2 ? pairs : [...pairs, rgpSetting(v, a[i + 1])], []).join('、');
      if (status === 'UNMATCHED_ACK') text += ' 対応する未完了の要求がないため、状態には反映しません。';
      return result(text, status === 'UNMATCHED_ACK' ? 'info' : ['OK', 'ACCEPTED'].includes(d[2]) ? 'info' : 'error');
    }
    case 'DBG': text = `診断ログ（${rgpName(rgpSubsystems, d[0])}）：${rgpText(d[1])}`; break;
    case 'IMU': text = `診断用IMU：加速度 ${d.slice(0, 3).join(' / ')} g、角速度 ${d.slice(3, 6).join(' / ')} °/秒（ゲーム入力には使用しません）。`; break;
    default: text = `未対応の種類「${match[1]}」を受信しました。入力には反映しません。`;
  }
  return result(text);
}

let serialAlertSignature = '';
function serialFrame(entry) {
  const line = String(entry?.line || '').trimEnd();
  if (entry?.status === 'INVALID') return null;
  const match = line.match(/^@([A-Z][A-Z0-9_.-]*),([0-9]+),([0-9]+)(?:,([^*]*))?\*([0-9A-F]{4})$/);
  return match && rgpCrc(line.slice(1, line.lastIndexOf('*'))) === match[5] ?
    { type: match[1], data: match[4]?.split(',') || [] } : null;
}
function alertRecovered(item, entries, snapshot) {
  const frame = serialFrame(item.entry), d = frame?.data || [];
  const h = snapshot?.hardware;
  const known = snapshot?.serial?.portOpen && h?.sessionOpen && h?.recent;
  const address = d.slice(3).find(value => /^0x[0-9A-F]{2}$/.test(value));
  const after = entries.filter(entry => entry.id > item.entry.id).map(entry => ({entry, frame:serialFrame(entry)}));
  const command = frame?.type === 'ACK' ? d[1] : item.entry.status === 'TIMEOUT' ? item.entry.line : null;
  const angleReferenceFailure = frame?.type === 'ACK' && command === 'ANGLE_ZERO' &&
    d[2] === 'FAILED' && d[3] === 'ANGLE_REFERENCE_INVALID';
  const referenceRecovered = angleReferenceFailure && known && h.state === 'READY' &&
    h.angleLocked && h.calibrated && h.inputFresh && !h.invalidTelemetry && h.telemetryEnabled;
  const requestAt = d.indexOf('REQ');
  const recovery = after.find(({frame: next}) => {
    if (!next) return false;
    const n = next.data;
    if (command) return (next.type === 'ACK' && n[1] === command && n[2] === 'OK') ||
      (referenceRecovered && next.type === 'EVT' && n[0] === 'ANGLE_LOCKED');
    if (frame?.type === 'ERR' && requestAt >= 0)
      return next.type === 'EVT' && ['CAL_DONE', 'I2C_SCAN_END'].includes(n[0]) && n[1] === d[requestAt + 1];
    if (item.entry.status === 'INVALID') return item.entry.line?.startsWith('@TEL,') ?
      next.type === 'TEL' && !h?.invalidTelemetry : next.type === 'TEL' || next.type === 'ACK';
    if (item.entry.status === 'STALE') return next.type === 'TEL';
    if (frame?.type === 'ERR') {
      if (d[1] === 'I2C') return next.type === 'EVT' && n[0] === 'I2C_RESTORED';
      if (d[1] === 'IMU') return next.type === 'EVT' && n[0] === 'IMU_FOUND' && (!address || n[1] === address);
      if (d[1] === 'ADS1115') return next.type === 'EVT' && n[0] === 'ADS_READY';
      if (d[1] === 'HANDLE') return next.type === 'EVT' && n[0] === 'ANGLE_LOCKED';
      if (d[1] === 'CALIBRATION') return next.type === 'EVT' && n[0] === 'CAL_DONE';
    }
    return next.type === 'EVT' && n[0] === 'STATE' && n[1] === 'READY';
  });
  if (recovery) {
    if (referenceRecovered && recovery.frame.type === 'EVT')
      item.recoveryNote = '現在は装置が基準角度を確定し、入力が復旧しています。';
    return recovery.entry.receivedAt || Date.now();
  }
  // Snapshots cover recovery events that have already left the replay window.
  if (!known || (item.entry.receivedAt && snapshot.serverTime < item.entry.receivedAt)) return null;
  if (referenceRecovered) {
    item.recoveryNote = '現在は装置が基準角度を確定し、入力が復旧しています。';
    return Date.now();
  }
  if (command || requestAt >= 0) return null;
  if (item.entry.status === 'INVALID') return !h.invalidTelemetry && h.inputFresh ? Date.now() : null;
  if (item.entry.status === 'STALE') return h.inputFresh ? Date.now() : null;
  if (frame?.type === 'ERR') {
    const healthy = { I2C: h.ads === 'Ok' && h.imu === 'Ok', ADS1115: h.ads === 'Ok',
      IMU: address ? h.imus?.some(imu => imu.address === address && imu.status === 'Ok') : h.imu === 'Ok',
      HANDLE: h.angleLocked, CALIBRATION: h.calibrated };
    if (Object.hasOwn(healthy, d[1])) return healthy[d[1]] ? Date.now() : null;
    return null;
  }
  return h.state === 'READY' && h.inputFresh ? Date.now() : null;
}
function renderSerialAlerts(serial, inputMode = '', snapshot = latestSnapshot) {
  const session = snapshot?.sessionId || '';
  if (session !== alertSession) { alertItems.clear(); alertSession = session; }
  const unique = new Map([...(serial.lines || []), ...(serial.alerts || []), ...(snapshot?.telemetry ? [snapshot.telemetry] : [])].map(entry => [entry.id, entry]));
  const entries = [...unique.values()].sort((a, b) => a.id - b.id);
  for (const entry of entries) {
    const explanation = explainSerialEntry(entry);
    if (explanation.level === 'info') continue;
    const frame = serialFrame(entry), d = frame?.data || [];
    const address = d.slice(3).find(value => /^0x[0-9A-F]{2}$/.test(value)) || '';
    const key = frame?.type === 'ERR' ? ['ERR', ...d.slice(0,3), address].join(':') :
      frame?.type === 'ACK' ? `ACK:${d[1]}` : frame?.type === 'EVT' ? `EVT:${d.slice(0,2).join(':')}` :
      `${entry.status}:${entry.status === 'TIMEOUT' ? entry.line : entry.line?.startsWith('@TEL,') ? 'TEL' : '通信'}`;
    if (!alertItems.has(key) || alertItems.get(key).entry.id < entry.id)
      alertItems.set(key, {entry, ...explanation, resolvedAt:null});
  }
  const now = Date.now();
  const portIssue = serial.portOpen === false && /serial|シリアル/i.test(inputMode) && !/disabled/i.test(inputMode);
  if (portIssue) alertItems.set('port', {entry:{time:'現在'},level:'warning',text:'シリアルポートが未接続です。USB接続とUnityのポート設定を確認してください。',resolvedAt:null});
  for (const [key, item] of alertItems) {
    if (item.resolvedAt == null) item.resolvedAt = key === 'port' ? (portIssue ? null : now) : alertRecovered(item, entries, snapshot);
  }
  const alerts = [...alertItems.values()].filter(item => item.resolvedAt == null || now - item.resolvedAt < 10000)
    .sort((a,b) => Number(a.resolvedAt != null) - Number(b.resolvedAt != null) || (b.entry.id || Infinity) - (a.entry.id || Infinity));
  // Retain tombstones for IDs still present in replay, so expired notices cannot reappear.
  for (const [key, item] of alertItems)
    if (key !== 'port' && !unique.has(item.entry.id) && item.resolvedAt != null && now - item.resolvedAt >= 10000) alertItems.delete(key);
  const signature = JSON.stringify(alerts);
  renderAttention(snapshot);
  if (signature === serialAlertSignature) return;
  serialAlertSignature = signature;
  $('serial-alerts').hidden = !alerts.length;
  $('serial-alert-list').replaceChildren(...alerts.map(item => {
    const row = document.createElement('div'); row.className = 'serial-alert ' + item.level + (item.resolvedAt != null ? ' resolved' : '');
    const time = document.createElement('span'); time.className = 'alert-time'; time.textContent = item.entry.time || '—';
    const text = document.createElement('p'); text.textContent = (item.resolvedAt != null ? '解決済み · ' : '未解決 · ') + item.text +
      (item.recoveryNote ? ' ' + item.recoveryNote : '');
    row.append(time, text); return row;
  }));
}

function isTelemetryEntry(entry) {
  return entry.status !== 'INVALID' && /^@TEL,/.test(entry.line || '') &&
    /\*[0-9A-F]{4}$/.test(entry.line) && rgpCrc(entry.line.slice(1, -5)) === entry.line.slice(-4);
}
function renderTelemetry(entry) {
  for (const id of ['latest-tel', 'home-inputs']) renderTelemetryTarget(entry, $(id));
}
function renderTelemetryTarget(entry, target) {
  if (!entry || !isTelemetryEntry(entry)) { target.textContent = 'ペダル・ハンドル入力を待っています。'; return; }
  const values = entry.line.slice(1, -5).split(',').slice(3, 9);
  if (values.length !== 6 || values.some((v, i) => v.trim() === '' || !Number.isFinite(Number(v)) || (i % 3 === 0 ? Math.abs(Number(v)) > 3600 : Number(v) < 0 || Number(v) > 1))) { target.textContent = '⚠ 受信した入力値が不正です。機器と接続を確認してください。'; return; }
  const hardware = latestSnapshot?.hardware;
  const current = latestSnapshot?.serial?.portOpen && hardware?.sessionOpen && hardware?.recent &&
    hardware?.inputFresh && !hardware?.invalidTelemetry && hardware?.telemetryEnabled;
  const heading = document.createElement('p'); heading.className = 'telemetry-heading';
  heading.textContent = `${current ? '● 入力を受信中' : '◷ 最新の受信記録 / 現在の入力は未確認'} · ${entry.time || '時刻不明'}`;
  const grid = document.createElement('div'); grid.className = 'telemetry-grid';
  const rows = [0, 1].map(i => {
    const [handle, accel, brake] = values.slice(i * 3, i * 3 + 3).map(Number);
    const card = document.createElement('article'); card.className = 'telemetry-card' + (current ? '' : ' unconfirmed');
    const name = document.createElement('h3'); name.textContent = `P${i + 1} の受信入力`;
    const steering = document.createElement('div'); steering.className = 'steering-value';
    const wheel = document.createElement('span'); wheel.className = 'steering-wheel';
    wheel.setAttribute('aria-hidden', 'true');
    // Static artwork; only the validated numeric angle controls its rotation.
    wheel.innerHTML = '<svg viewBox="0 0 128 128" focusable="false"><path d="M59 5h10l-5 8z" fill="#9aaab8"/><circle cx="64" cy="64" r="55" fill="none" stroke="#34424e" stroke-dasharray="2 6"/><g><circle cx="64" cy="64" r="44" fill="none" stroke="#8092a2" stroke-width="8"/><path d="M23 54l32 11m50-11L73 65m-9 10v32" fill="none" stroke="currentColor" stroke-width="8" stroke-linecap="round"/><circle cx="64" cy="64" r="12" fill="#111923" stroke="currentColor" stroke-width="4"/><path d="M64 20v11" stroke="currentColor" stroke-width="6" stroke-linecap="round"/></g></svg>';
    wheel.querySelector('g').setAttribute('transform', `rotate(${handle} 64 64)`);
    const angle = document.createElement('span');
    angle.textContent = `ハンドル ${handle === 0 ? '中央' : handle > 0 ? '右' : '左'} ${Math.abs(handle)}°`;
    steering.append(wheel, angle);
    card.append(name, steering);
    for (const [label, value, kind] of [['アクセル', accel, 'accel'], ['ブレーキ', brake, 'brake']]) {
      const row = document.createElement('div'); row.className = 'pedal-meter ' + kind;
      const title = document.createElement('span'); title.textContent = label;
      const amount = document.createElement('strong'); amount.textContent = `${(value * 100).toFixed(1)}%`;
      const meter = document.createElement('meter'); meter.min = 0; meter.max = 1; meter.value = value;
      meter.setAttribute('aria-label', `P${i + 1} ${label}`);
      row.append(title, amount, meter); card.append(row);
    }
    return card;
  });
  grid.append(...rows);
  const note = document.createElement('p'); note.className = 'control-note'; note.textContent = '受信値を表示しています。ゲームへの適用状況はプレイヤー画面の入力表示で確認できます。';
  target.replaceChildren(heading, grid, note);
}
function serialStatusLabel(status) {
  return ({ OK:'✓ 正常', EVENT:'◆ 状態変化', ERROR:'✕ エラー', WARN:'⚠ 警告', INVALID:'✕ 解析失敗',
    STALE:'◷ 古い入力', TIMEOUT:'◷ 応答なし', ACK_OK:'✓ 操作完了', ACK_ACCEPTED:'◷ 実行中',
    ACK_FAILED:'✕ 操作失敗', SENT:'✓ 送信済み', UNMATCHED_ACK:'◇ 対応外の応答', SYS:'◇ 機器情報', HELLO:'◇ 装置起動',
    DBG:'◇ 診断情報', IMU:'◇ 姿勢データ', IGNORED:'◇ 対象外', PARTIAL:'⚠ 一部のみ受信',
    INCOMPATIBLE:'✕ 通信方式が不一致', 'Waiting for input':'◷ 入力待ち' })[status] || (status ? '◇ 通信情報' : '—');
}
function renderLog(lines) {
  lines = lines.filter(entry => !isTelemetryEntry(entry));
  const signature = JSON.stringify(lines);
  if (signature === logSignature) return;
  logSignature = signature;
  const log = $('serial-log');
  if (!lines.length) {
    log.innerHTML = '<div class="empty-log">TEL以外の通信履歴を待っています。</div>';
    return;
  }
  const oldTop = log.scrollTop;
  const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 35;
  log.replaceChildren(...lines.slice().reverse().map((entry) => {
    const row = document.createElement('div');
    row.className = 'log-row';
    const time = document.createElement('span'); time.textContent = entry.time || '—';
    const status = document.createElement('span');
    const explanation = explainSerialEntry(entry);
    status.textContent = (entry.direction === 'TX' ? '↑ ' : '↓ ') +
      (explanation.level === 'error' ? '✕ 要確認' : explanation.level === 'warning' ? '⚠ 注意' : serialStatusLabel(entry.status));
    status.className = explanation.level === 'error' ? 'bad' : explanation.level === 'warning' ? 'partial' : 'ok';
    const data = document.createElement('div'); data.className = 'serial-payload';
    const meaning = document.createElement('p'); meaning.className = 'serial-explanation'; meaning.textContent = explanation.text;
    data.append(meaning);
    row.append(time, status, data);
    return row;
  }));
  if (!atBottom) log.scrollTop = oldTop;
}

function renderPlayerProgress(snapshot) {
  const steps = { Vehicle: '車種を選択中', Paint: 'カラーを選択中', Confirm: '決定操作を待機中', Confirmed: '選択確定・画面切替中',
    Accelerate: 'アクセルを踏んで加速', Steer: 'ハンドルを切って旋回', Stop: 'ペダルを離して停止',
    Results: '結果を表示中', Driving: 'レースを走行中', Menu: '中断メニューを表示中', Withdrawn: '途中棄権・相手の走行を待機', Spectating: '相手の走行を観戦中', Finished: '完走・ゴール演出中', DNF: '未完走', Returned: 'タイトルへ復帰済み・相手の復帰を待機',
    Ready: '開始準備完了・相手の開始操作を待機', Complete: '練習完了・相手の完了を待機', TimedOut: '制限時間終了・相手の完了を待機', Waiting: '練習の開始を待機' };
  const players = snapshot?.playerProgress || [1, 2].map(number => ({number}));
  $('player-progress').replaceChildren(...players.map(player => {
    const card = document.createElement('article'); card.className = 'progress-card';
    const title = document.createElement('h3'); title.textContent = `P${player.number} / ${labels[player.state] || '状態待機'}`;
    const detail = document.createElement('p'); detail.textContent = steps[player.step] || details[player.state] || 'プレイヤーの進行状況を待っています';
    const car = document.createElement('p'); car.textContent = player.carName ? `車両：${player.carName}` : '車両：未選択';
    card.append(title, detail, car);
    if (player.state === 'CarSelection' || player.state === 'Tutorial') {
      const time = document.createElement('p'); time.textContent = `残り ${Math.ceil(Math.max(0, player.remaining || 0))} 秒`;
      const progress = document.createElement('progress'); progress.max = 1; progress.value = Math.max(0, Math.min(1, player.progress || 0));
      progress.setAttribute('aria-label', `P${player.number} 現在の操作の達成度`);
      card.append(time, progress);
    }
    return card;
  }));
}
function renderHardware(snapshot) {
  const hardware = snapshot?.hardware;
  const port = snapshot?.serial?.portOpen;
  const known = port && hardware?.sessionOpen && hardware?.recent;
  $('hardware-state').textContent = !snapshot ? '未確認' : known ? rgpState(hardware.state) : port ? '装置の応答待ち' : 'シリアル未接続';
  const sensor = (state) => !known || state === 'Unknown' || !state ? ['unknown', '未確認'] :
    state === 'Ok' ? ['ok', '動作確認済み'] : ['fault', '異常・接続要確認'];
  const pedal = (n) => {
    const input = hardware?.pedals?.find(player => player.number === n);
    if (!known) return [['unknown', '入力未確認'], 'ESP32の接続と応答を待っています'];
    if (hardware.ads === 'Fault') return [['fault', 'センサー異常'], 'ペダル用センサーの接続を確認してください'];
    if (hardware.invalidTelemetry) return [['fault', '入力データ異常'], '受信値・形式が不正です。ケーブルや装置を確認してください'];
    if (!hardware.telemetryEnabled) return [['unknown', '入力配信停止中'], '入力データの配信を開始してください'];
    if (!hardware.inputFresh || !input) return [['fault', '正常な入力を受信できません'], '入力の途絶または装置の準備状態を確認してください'];
    if (![input.accel, input.brake].every(value => Number.isFinite(value) && value >= 0 && value <= 1))
      return [['fault', '入力データ異常'], 'ペダルの入力値が正常な範囲を外れています'];
    return [['ok', '動作確認済み'], `アクセル ${(input.accel * 100).toFixed(0)}% / ブレーキ ${(input.brake * 100).toFixed(0)}%`];
  };
  const cards = [
    ['ESP32 / USBシリアル', !snapshot ? ['unknown','未確認'] : !port ? ['fault','未接続'] : known ? ['ok','通信中'] : ['unknown','ポート開放・応答待ち'],
      !snapshot ? 'Unityの接続を待っています' : `${snapshot.serial.port || 'ポート不明'} / ${rgpState(hardware?.state)}`],
    ...[0, 1].map(index => {
      const imu = hardware?.imus?.[index];
      return [`IMU ${index + 1}（I2C）`, sensor(imu?.status), imu?.address ?
        `アドレス ${imu.address} / 識別値 ${imu.identity || '未確認'}` : '個別の機器通知を待っています'];
    }),
    ['ADS1115（I2C）', sensor(hardware?.ads), hardware?.adsAddress ? `アドレス ${hardware.adsAddress}` : 'ペダル用センサー / アドレス未確認'],
    ...[1, 2].map(n => [`P${n} ペダル`, ...pedal(n)])
  ];
  cards.forEach((card,index)=>{if(snapshot&&!HardwareDiagnostics.required(snapshot,index)){card[1]=['unknown','この入力モードでは未使用'];card[2]='未接続でも現在の走行入力には影響しません';}});
  const faults = cards.filter(([, [level]]) => level === 'fault').length;
  const ready = cards.filter(([, [level]]) => level === 'ok').length;
  const requiredCount = cards.filter((_,index)=>HardwareDiagnostics.required(snapshot,index)).length;
  const keyboard = snapshot?.settings?.inputMode === 'keyboard' || /^keyboard/i.test(snapshot?.inputMode || '');
  const automatic = snapshot?.settings?.bothPlayersAutodrive;
  const summary = $('device-summary');
  const level = !snapshot || keyboard ? 'unknown' : faults ? 'fault' :
    requiredCount > 0 && ready === requiredCount && hardware?.state === 'READY' && hardware?.angleLocked && hardware?.calibrated ? 'ok' : 'unknown';
  summary.className = level;
  summary.textContent = !snapshot ? '確認待ち' : automatic ? '自動運転 / 機器入力は未使用' : keyboard ? '⌨ 機器入力は未使用' : faults ? `⚠ ${faults}件 要確認` :
    level === 'ok' ? '✓ すべて正常' : '◷ 準備・確認中';
  $('device-summary-items').replaceChildren(...cards.map(([name, [state, text]], index) => {
    const chip = document.createElement('span'); chip.className = 'device-chip ' + state;
    chip.textContent = `${state === 'ok' ? '✓' : state === 'fault' ? '!' : '◷'} ${['USB', 'IMU 1', 'IMU 2', 'ADS', 'P1', 'P2'][index]}`;
    chip.setAttribute('aria-label', `${name}：${text}`);
    chip.title = `${name}：${text}`;
    chip.onclick = () => openHardwareDiagnosis(index);chip.setAttribute('role','button');chip.tabIndex=0;chip.onkeydown=event=>{if(event.key==='Enter'||event.key===' '){event.preventDefault();openHardwareDiagnosis(index);}};
    return chip;
  }));
  $('hardware-state').className = 'mini-status ' + level;
  $('hardware-grid').replaceChildren(...cards.map(([name, [level, state], detail], index) => {
    const card = document.createElement('article'); card.className = 'hardware-card ' + level;
    const heading = document.createElement('h3'); heading.textContent = name;
    const value = document.createElement('strong'); value.textContent = state;
    const note = document.createElement('p'); note.textContent = detail;
    const inspect=document.createElement('button');inspect.type='button';inspect.className='secondary-button';inspect.textContent='原因を調べる';inspect.onclick=()=>openHardwareDiagnosis(index);
    card.append(heading, value, note, inspect); return card;
  }));
}
let diagnosticGeneration=0, diagnosticKey=0, diagnosticScan=null;
const diagnosisIcons={
  usb:'M9 18V6m0 0L6 9m3-3 3 3M9 13h7V7m-2 0h4M9 16H5v-4m4 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z',
  wheel:'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Zm-9-9h18M12 12v9m-3-9a3 3 0 0 1 6 0',
  chip:'M7 7h10v10H7zm3-4v4m4-4v4m-4 10v4m4-4v4M3 10h4m-4 4h4m10-4h4m-4 4h4',
  pedal:'m9 3 7 2-3 13-7-2L9 3Zm-1 8 6 2m-7-6 8 2M4 21h16',
  check:'m5 12 4 4L19 6',alert:'M12 4 2 21h20L12 4Zm0 6v4m0 3v1',
  wait:'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Zm0-14v5l3 2',minus:'M5 12h14',
  setup:'M4 7h16M4 17h16M8 4v6m8 4v6',signal:'M4 15v5m5-10v10m5-15v15m5-18v18',
  wiring:'M4 4v6h16V4M12 10v10m-3-3 3 3 3-3'
};
const diagnosisStates={ok:['✓','正常応答'],fault:['!','異常'],warn:['!','要確認'],unknown:['?','未確認'],unused:['−','未使用'],info:['·','情報']};
function diagnosisElement(tag,className,text){
  const element=document.createElement(tag);if(className)element.className=className;if(text!==undefined)element.textContent=text;return element;
}
function diagnosisIcon(name){
  const icon=diagnosisElement('span','diagnosis-icon');icon.setAttribute('aria-hidden','true');
  icon.innerHTML='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="'+(diagnosisIcons[name]||diagnosisIcons.alert)+'"/></svg>';return icon;
}
function renderHardwareDiagnosis(){
  const report=HardwareDiagnostics.analyze(connected?latestSnapshot:null,diagnosticKey,diagnosticScan);
  $('diagnostics-title').textContent=report.title+' の診断';
  const summary=$('diagnostics-summary');summary.className='diagnosis-summary '+report.summary.level;
  const heading=diagnosisElement('div');heading.append(diagnosisElement('strong','',report.summary.title),diagnosisElement('p','',report.summary.note));
  summary.replaceChildren(diagnosisIcon(report.summary.icon),heading);
  // Keep an operator's expanded explanation open while the fresh query completes.
  const expanded=new Set([...$('diagnostics-causes').children].filter(card=>card.querySelector('details')?.open).map(card=>card.dataset.issueId));
  $('diagnostics-causes').replaceChildren(...report.issues.map(issue=>{
    const card=diagnosisElement('article','diagnosis-cause '+issue.level);card.dataset.issueId=issue.id;
    const head=diagnosisElement('div','diagnosis-cause-head');
    head.append(diagnosisIcon(issue.icon),diagnosisElement('h4','',issue.title),diagnosisElement('span','diagnosis-certainty '+(issue.confirmed?'observed':'candidate'),issue.confirmed?'確認済み':'候補'));
    const next=diagnosisElement('p','diagnosis-next');next.append(diagnosisElement('span','','次に'),diagnosisElement('span','',issue.next));
    const details=diagnosisElement('details');details.open=expanded.has(issue.id);
    details.append(diagnosisElement('summary','','説明を開く'),diagnosisElement('p','',issue.detail));
    card.append(head,next,details);return card;
  }));
  if(!report.issues.length){
    const empty=diagnosisElement('div','diagnosis-empty');empty.append(diagnosisIcon(report.summary.icon),diagnosisElement('p','',report.summary.level==='unused'?'この機器の点検は不要です。':report.summary.level==='unknown'?'接続が戻ると原因候補を表示します。':'現在の情報に原因候補はありません。'));$('diagnostics-causes').append(empty);
  }
  function node(index){
    const item=report.nodes[index],state=diagnosisStates[item.level],card=diagnosisElement('div','diagnosis-node '+item.level+(item.selected?' selected':''));
    card.setAttribute('aria-label',`${item.label}：${state[1]}${item.selected?'、診断対象':''}`);
    card.append(diagnosisIcon(item.icon),diagnosisElement('strong','',item.label));
    if(item.sublabel)card.append(diagnosisElement('small','',item.sublabel));
    card.append(diagnosisElement('span','diagnosis-node-state',state.join(' ')));
    if(item.selected)card.append(diagnosisElement('span','diagnosis-selected','診断対象'));return card;
  }
  const root=diagnosisElement('div','diagnosis-root');root.append(node(0));
  const bus=diagnosisElement('div','diagnosis-bus'+(report.sharedBusSuspect?' suspect':''));
  bus.append(diagnosisElement('strong','','I2C 共通バス'+(report.sharedBusSuspect?' · 点検候補':'')),diagnosisElement('span','','VCC / GND / SDA / SCL'));
  const branches=diagnosisElement('div','diagnosis-branches'),ads=diagnosisElement('div','diagnosis-ads'),pedals=diagnosisElement('div','diagnosis-pedals');
  pedals.append(node(4),node(5));ads.append(node(3),diagnosisElement('span','diagnosis-pedal-wire','信号 / 電源 / GND'),pedals);branches.append(node(1),node(2),ads);
  const legend=diagnosisElement('div','diagnosis-legend');
  for(const level of ['ok','fault','unknown','unused'])legend.append(diagnosisElement('span',level,diagnosisStates[level].join(' ')));
  $('diagnostics-topology').replaceChildren(root,bus,branches,legend);
  $('diagnostics-readings').replaceChildren(...report.readings.map(item=>{
    const cell=diagnosisElement('div',item.level);cell.append(diagnosisElement('dt','',item.label),diagnosisElement('dd','',diagnosisStates[item.level][0]+' '+item.value));return cell;
  }));
  for(const name of ['facts','checks'])$('diagnostics-'+name).replaceChildren(...report[name].map(text=>diagnosisElement('li','',text)));
}
async function openHardwareDiagnosis(key){
  diagnosticKey=key;diagnosticScan=null;const generation=++diagnosticGeneration;
  $('diagnostics-details').open=false;
  $('diagnostics-causes').replaceChildren();
  $('diagnostics-close').onclick=()=>{diagnosticGeneration++;$('diagnostics-dialog').close();};
  $('diagnostics-dialog').oncancel=()=>{diagnosticGeneration++;};
  if(!$('diagnostics-dialog').open)$('diagnostics-dialog').showModal();
  renderHardwareDiagnosis();
  const session=latestSnapshot?.sessionId,hardwareGeneration=latestSnapshot?.hardware?.generation;
  if(!latestCanControl||!localMode||!canSendSerial){$('diagnostics-progress').textContent='現在の受信情報で診断しています。新しい照会には操作権限と装置の応答が必要です。';return;}
  const commands=['GET_STATUS','GET_CONFIG'];
  if(!['Game','Countdown','Goal'].includes(latestSnapshot.state))commands.push('SCAN_I2C');
  $('diagnostics-progress').textContent='装置の最新状態を照会しています…';
  try{
    for(const command of commands){
      if(generation!==diagnosticGeneration)return;
      const snapshot=latestSnapshot;
      if(snapshot?.sessionId!==session||snapshot?.hardware?.generation!==hardwareGeneration)throw new Error('接続セッションが変わりました');
      const request={id:newRequestId(),targetSession:session,expectedState:snapshot.state,stateRevision:snapshot.stateRevision,observedAt:snapshot.serverTime,command,args:[]};
      const response=await apiFetch('/api/action/serial',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(request),signal:AbortSignal.timeout(3000)});
      const outcome=await response.json();if(!outcome.ok)throw new Error('装置が照会を受け付けませんでした（'+outcome.reason+'）');
      const end=Date.now()+5000;let done=false;
      while(Date.now()<end&&generation===diagnosticGeneration){
        await new Promise(resolve=>setTimeout(resolve,150));
        const response=await apiFetch('/api/status',{signal:AbortSignal.timeout(2000)});if(!response.ok)throw new Error('Unityの応答がありません');
        const fresh=await response.json();
        if(fresh.sessionId!==session||fresh.hardware?.generation!==hardwareGeneration)throw new Error('装置が再接続されました');
        render(fresh,{canControl:latestCanControl,canInput:latestInputAllowed});
        const result=fresh.hardware?.commands?.find(x=>x.sequence===outcome.serialSequence&&x.command===command);
        if(result&&['OK','DONE'].includes(result.status)&&!(command==='SCAN_I2C'&&result.status!=='DONE')){
          if(command==='SCAN_I2C')diagnosticScan=result.devices||[];
          done=true;break;
        }
        if(result&&!['SENT','ACCEPTED','OK'].includes(result.status))throw new Error(command+'：'+result.status);
      }
      if(generation!==diagnosticGeneration)return;
      if(!done)throw new Error('照会の完了通知を確認できませんでした');
      renderHardwareDiagnosis();
    }
    $('diagnostics-progress').textContent=commands.includes('SCAN_I2C')?'最新状態とI2C検索で診断しました。':'走行中は状態照会のみ実施しました。I2C検索は待機時に実行します。';
  }catch(error){if(generation===diagnosticGeneration){renderHardwareDiagnosis();$('diagnostics-progress').textContent='新しい照会は未完了：'+error.message+'。以下は取得できた範囲の情報です。';}}
}
function setSerialControls(enabled) {
  canSendSerial = !!enabled;
  if (!commandRequest && canSendSerial && /接続を待|再接続を待/.test($('serial-command-status').textContent))
    $('serial-command-status').textContent = '操作を選択してESP32へ送信できます。';
  for (const control of document.querySelectorAll('[data-serial-control]')) control.disabled = !canSendSerial;
  $('serial-send').disabled = !canSendSerial;
}
function renderRaw(lines, gap = false, force = false) {
  rawPending = lines.slice(-500); rawGap = gap;
  $('raw-status').textContent = `${rawPaused ? '表示を一時停止中（受信は継続）' : '直近の送受信を表示'} / ${rawPending.length} 件${gap ? ' / 保持範囲外の生データ欠落あり' : ''}`;
  if (rawPaused && !force) return;
  // Keep receiving while closed, but avoid rebuilding up to 500 rows on the iPad.
  if (!$('raw-dialog').open && !force) return;
  const signature = JSON.stringify(rawPending);
  if (signature === rawSignature && !force) return;
  rawSignature = signature;
  const log = $('raw-log'), oldTop = log.scrollTop;
  const follow = log.scrollHeight - log.scrollTop - log.clientHeight < 35;
  const retained = new Map();
  log.replaceChildren(...rawPending.map(entry => {
    const cached = rawElements.get(entry.id);
    if (cached?.line === entry.line) { retained.set(entry.id, cached); return cached.row; }
    const row = document.createElement('div'); row.className = 'raw-row ' + (entry.direction === 'TX' ? 'tx' : 'rx');
    const time = document.createElement('span'); time.textContent = entry.time || '';
    const direction = document.createElement('span'); direction.textContent = entry.direction || 'RX';
    const code = document.createElement('code'); code.textContent = entry.line;
    row.append(time, direction, code); retained.set(entry.id, {line:entry.line,row}); return row;
  }));
  rawElements = retained;
  if (!rawPending.length) {
    const empty = document.createElement('p'); empty.className = 'empty-log';
    empty.textContent = localMode ? '送受信データを待っています。ESP32の接続を確認してください。' :
      'Firebaseの遠隔監視では生データを配信していません。Unityのローカル画面または接続ブリッジで確認してください。';
    log.append(empty);
  }
  log.scrollTop = follow ? log.scrollHeight : oldTop;
}
function updateCommandFields() {
  const command = $('serial-command').value;
  for (const name of ['calibration', 'stream', 'enabled', 'rate', 'beta'])
    $('command-' + name + '-field').hidden = !({calibration:['CALIBRATE'],stream:['SET_STREAM','SET_RATE'],enabled:['SET_STREAM'],rate:['SET_RATE'],beta:['SET_BETA']}[name].includes(command));
  const debug = [...$('command-stream').options].find(option => option.value === 'DBG');
  debug.disabled = command === 'SET_RATE';
  if (command === 'SET_RATE' && $('command-stream').value === 'DBG') $('command-stream').value = 'TEL';
  const maximum = $('command-stream').value === 'IMU' ? 50 : 100;
  $('command-rate').max = maximum;
  if (Number($('command-rate').value) > maximum) $('command-rate').value = maximum;
  const help = {
    GET_STATUS:'状態とセンサーの動作状況を照会します。', GET_CONFIG:'現在の配信設定を照会します。', PING:'装置からの応答を確認します。',
    ANGLE_ZERO:'両プレイヤーのハンドルを中央にして実行してください。両方の基準をまとめて変更します。',
    CALIBRATE:'両プレイヤーのセンサーを校正します。ジャイロ校正中はハンドルを静止させてください。未対応の対象は装置が拒否します。',
    SCAN_I2C:'I2Cバスを検索します。検索中はペダル・ハンドル入力の配信が一時停止します。',
    SET_STREAM:'TELを停止するとゲームのシリアル入力が中立になります。値の上書きは行いません。',
    SET_RATE:'TELは1〜100 Hz、IMUは1〜50 Hzで指定します。', SET_BETA:'姿勢推定の調整値を0.010〜1.000で指定します。'
  };
  $('serial-command-help').textContent = help[command] || '';
}
function serialCommandArguments() {
  switch ($('serial-command').value) {
    case 'CALIBRATE': return [$('command-calibration').value];
    case 'SET_BETA': return [$('command-beta').value];
    case 'SET_RATE': return [$('command-stream').value, $('command-rate').value];
    case 'SET_STREAM': return [$('command-stream').value, $('command-enabled').value];
    default: return [];
  }
}
function showCommandResult(stage, message, values = []) {
  const title = commandRequest ? rgpName(rgpCommands, commandRequest.command) : '実行結果';
  const signature = JSON.stringify([title, stage, message, values]);
  if (signature === commandResultSignature) return;
  commandResultSignature = signature;
  const stages = {sending:'送信中', waiting:'応答待ち', running:'実行中', success:'完了', error:'失敗', unknown:'結果未確認'};
  $('serial-command-title').textContent = title;
  $('serial-command-badge').textContent = stages[stage];
  $('serial-command-result').className = 'command-result ' + stage;
  $('serial-command-status').textContent = message;
  const done = stage === 'success' ? 3 : stage === 'running' ? 2 : stage === 'waiting' ? 1 : 0;
  $('serial-command-steps').replaceChildren(...['送信', '受付', '完了'].map((text, index) => {
    const step = document.createElement('li');
    step.textContent = `${index + 1}  ${text}`;
    step.className = index < done ? 'complete' : index === done && ['sending','waiting','running'].includes(stage) ? 'current' : '';
    return step;
  }));
  $('serial-command-values').replaceChildren(...values.map(([name, value]) => {
    const row = document.createElement('div');
    const label = document.createElement('dt'); label.textContent = name;
    const detail = document.createElement('dd'); detail.textContent = String(value);
    row.append(label, detail); return row;
  }));
  $('serial-command-last').textContent = commandRequest?.sequence != null ? `要求番号 ${commandRequest.sequence}` : '';
}
function renderCommandResult(snapshot) {
  if (!commandRequest || commandRequest.sequence == null) return;
  if (snapshot.sessionId !== commandRequest.sessionId || snapshot.hardware?.generation !== commandRequest.generation) {
    showCommandResult('unknown', '装置またはUnityの接続セッションが変わりました。この操作の実行結果は確認できません。');
    return;
  }
  const result = snapshot.hardware?.commands?.find(item => item.sequence === commandRequest.sequence && item.command === commandRequest.command);
  if (!result) return; // An older state update may arrive before the command response.
  const d = result.data || [], command = result.command;
  if (result.status === 'SENT') { showCommandResult('waiting', '送信しました。装置の応答を待っています。'); return; }
  if (result.status === 'ACCEPTED') { showCommandResult('running', '装置が操作を受け付けました。完了までお待ちください。'); return; }
  if (result.status === 'TIMEOUT') { showCommandResult('unknown', '装置からの応答・完了通知を確認できませんでした。機器の状態を確認してください。'); return; }
  if (!['OK','DONE'].includes(result.status)) {
    const errors = {INVALID_ARG:'指定した値を装置が受け付けられませんでした。設定値を確認してください。',
      UNSUPPORTED:'この装置は選択した操作に対応していません。', BUSY:'装置は別の処理を実行中です。完了後に操作してください。',
      NOT_READY:'装置の準備ができていません。機器・接続状態を確認してください。', FAILED:'装置で処理に失敗しました。機器の状態を確認してください。'};
    const code = d[0] === 'ERROR' || d[0] === 'WARN' || d[0] === 'FATAL' ? d[2] : d[3];
    const detail = code && Object.hasOwn(rgpErrors, code) ? [['原因', rgpErrors[code]]] : [];
    showCommandResult('error', errors[result.status] || '装置が操作を完了できませんでした。機器の状態を確認してください。', detail);
    return;
  }
  let message = '操作が完了しました。', values = [];
  switch (command) {
    case 'GET_STATUS':
      message = '装置の現在の状態を確認しました。';
      values = [['装置',rgpState(d[3])],['ペダル用センサー',d[4] === '1' ? '正常' : '異常'],
        ['IMU全体',d[5] === '1' ? '正常' : '異常'],['ハンドル基準',d[6] === '1' ? '設定済み' : '未設定'],['校正',d[7] === '1' ? '完了' : '未完了']];
      break;
    case 'GET_CONFIG':
      message = '現在の設定を取得しました。';
      for (let i = 3; i + 1 < d.length; i += 2) {
        if (!Object.hasOwn(rgpSettings, d[i])) continue;
        values.push([rgpName(rgpSettings,d[i]), /_STREAM$/.test(d[i]) ? (d[i+1] === '1' ? '配信中' : '停止中') : /_RATE$/.test(d[i]) ? `${d[i+1]} Hz` : d[i+1]]);
      }
      break;
    case 'PING': message = 'ESP32から応答がありました。通信は正常です。'; break;
    case 'ANGLE_ZERO': message = '両ハンドルの現在位置を中央に設定しました。'; break;
    case 'CALIBRATE':
      message = result.status === 'DONE' ? 'センサーの校正が完了しました。' : '校正の要求を受け付けました。完了通知を待っています。';
      if (result.status !== 'DONE') { showCommandResult('running',message); return; }
      values = [['校正対象', rgpName(rgpTargets,d[2])]]; break;
    case 'SCAN_I2C':
      if (result.status !== 'DONE') { showCommandResult('running','機器を検索しています。完了通知を待っています。'); return; }
      message = Number(d[2]) > 0 ? '接続されている機器を検出しました。' : '検索が完了しました。接続機器は見つかりませんでした。';
      values = [['検出数',`${d[2]}台`],['機器のアドレス',result.devices?.join(' / ') || '検出なし']]; break;
    case 'SET_RATE': message = 'データの送信頻度を変更しました。'; values = [[rgpName(rgpSettings,d[3]+'_RATE'),`${d[4]} Hz`]]; break;
    case 'SET_STREAM': message = 'データの配信設定を変更しました。'; values = [[rgpName(rgpSettings,d[3]+'_STREAM'),d[4] === '1' ? '配信中' : '停止中']]; break;
    case 'SET_BETA': message = '姿勢推定の調整値を変更しました。'; values = [['調整値',d[3]]]; break;
  }
  showCommandResult('success', message, values);
}
async function submitSerialCommand(event) {
  event.preventDefault();
  if (!canSendSerial || !connected || !localMode) return;
  const payload = {command:$('serial-command').value,args:serialCommandArguments()};
  const request = commandRequest = {...payload,sessionId:latestSnapshot.sessionId,generation:latestSnapshot.hardware?.generation};
  setSerialControls(false);
  showCommandResult('sending', 'ESP32へ操作を送信しています…');
  let outcome;
  try {
    if (liveController) outcome = await liveController.command('serial', payload);
    else {
      const body = {...payload,id:newRequestId(),targetSession:latestSnapshot.sessionId,expectedState:latestSnapshot.state,
        stateRevision:latestSnapshot.stateRevision,observedAt:latestSnapshot.serverTime};
      const response = await apiFetch('/api/action/serial', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
      outcome = await response.json();
      await poll();
    }
  } catch { }
  const reasons = {invalid_arguments:'指定した値を確認してください。',session_not_ready:'装置との接続準備を待っています。',commands_pending:'未完了の操作が多いため待機してください。',
    command_pending:'同じ操作が応答待ちまたは実行中です。',device_not_ready:'装置の準備完了、またはハンドル基準の設定待ちのときに実行できます。',serial_disconnected:'シリアル接続・装置の応答を確認してください。',write_failed:'シリアル送信に失敗しました。'};
  if (outcome?.ok) {
    request.sequence = outcome.serialSequence;
    showCommandResult('waiting','送信しました。装置の応答を待っています。');
    renderCommandResult(latestSnapshot);
  } else showCommandResult(outcome ? 'error' : 'unknown', outcome ?
    `送信できませんでした。${reasons[outcome.reason] || 'ゲームや装置の最新状態を確認してください。'}` : '送信結果を確認できません。機器の状態を確認してください。');
  if (connected) setSerialControls(latestCanControl && latestSnapshot.serial.portOpen && latestSnapshot.hardware?.sessionOpen && latestSnapshot.hardware?.recent);
}

async function apiFetch(path, options = {}) {
  return fetch(API + path, { cache: 'no-store', ...(API ? { targetAddressSpace: 'loopback' } : {}), ...options });
}

async function poll() {
  try {
    const response = await apiFetch('/api/status');
    if (!response.ok) throw new Error('HTTP ' + response.status);
    render(await response.json());
    $('transport-status').textContent = 'ローカル / HTTP';
    if (Date.now() - lastFrameAt >= 2000 && !frameBusy) {
      lastFrameAt = Date.now();
      void refreshFrames();
    }
  } catch {
    setConnection(false);
  }
}

async function refreshFrames() {
  if (frameBusy || !connected || activePage !== 'race') return;
  frameBusy = true;
  const generation = frameGeneration;
  try {
    await Promise.all([1, 2].map(async (n) => {
      let url = null;
      try {
        const response = await apiFetch(`/api/player/${n}/frame`, { signal: AbortSignal.timeout(5000) });
        if (!response.ok) throw new Error('Camera unavailable');
        url = URL.createObjectURL(await response.blob());
        const decoded = new Image();
        decoded.src = url;
        await decoded.decode();
        if (!connected || generation !== frameGeneration) return;
        const img = $('feed-' + n);
        const old = frameUrls[n - 1];
        img.src = url;
        frameUrls[n - 1] = url;
        url = null;
        img.hidden = false;
        $('placeholder-' + n).hidden = true;
        if (old) URL.revokeObjectURL(old);
      } catch {
        // A transient capture failure keeps the last successfully decoded picture visible.
        if (generation === frameGeneration && !frameUrls[n - 1]) {
          $('feed-' + n).hidden = true;
          $('placeholder-' + n).hidden = false;
        }
      } finally {
        if (url) URL.revokeObjectURL(url);
      }
    }));
  } finally { frameBusy = false; }
}

function renderFeatureSettings(settings) {
  if (settingsBusy) return;
  const features = settings?.featuresAvailable && Array.isArray(settings.features) ? settings.features : [];
  const signature = JSON.stringify(features.map(feature => [feature.id, feature.label]));
  if (signature !== featureSignature) {
    featureSignature = signature;
    featureElements.clear();
    const rows = features.map(feature => {
      const label = document.createElement('label'); label.className = 'feature-option';
      const input = document.createElement('input'); input.type = 'checkbox';
      input.id = 'settings-feature-' + feature.id;
      input.dataset.setting = 'feature'; input.dataset.settingArg = feature.id;
      const caption = document.createElement('span'); caption.textContent = feature.label;
      label.append(input, caption); featureElements.set(feature.id, input);
      return label;
    });
    $('settings-features').replaceChildren(...rows);
  }
  for (const feature of features) featureElements.get(feature.id).checked = !!feature.enabled;
  $('settings-high-speed-unavailable').hidden = !settings?.featuresAvailable || features.some(feature => feature.id === 'HighSpeedDrive');
  $('settings-feature-count').textContent = features.length ?
    `${features.filter(feature => feature.enabled).length} / ${features.length} 項目がON` :
    operatorStatus?.busy ? 'ゲーム設定は更新後に再取得します。ビルドは継続中です。' : 'F8設定を取得できません。Unity の接続・バージョンを確認してください。';
  $('settings-rules').textContent = settings?.featuresAvailable ?
    `${settings.activePreset === 'Quick' ? 'クイックレース（練習なし）' : settings.activePreset === 'NoTutorial' ? '通常2周（練習なし）' : '通常レース（練習あり）'} / ${latestSnapshot?.goalLap ?? '—'}周 / 制限時間180秒` : '';
  $('settings-rule-help').textContent = settings?.featuresAvailable ?
    settings.activePreset === 'Quick' ? '両プレイヤーの開始操作後、練習とコース紹介を省略してレースを開始します。結果は15秒で退出案内へ進みます。' :
    settings.activePreset === 'NoTutorial' ? '両プレイヤーの開始操作後、練習を省略して2周レースへ進みます。コース紹介は通常どおりです。' :
    '練習開始からレース開始まで最大60秒。結果は15秒後に退出案内へ切り替わります。' : '';
  const modes = { Standard: '通常（練習あり・2周）', Quick: 'クイック（練習なし・1周）', NoTutorial: 'チュートリアルなし（2周）' };
  const mode = $('settings-raceMode');
  const supportedModes = Array.isArray(settings?.raceModes) ? settings.raceModes : ['Standard', 'Quick'];
  for (const option of Array.from(mode.options || [])) option.disabled = !supportedModes.includes(option.value);
  if (document.activeElement !== mode) mode.value = settings?.selectedPreset ?? 'Standard';
  $('settings-race-mode-status').textContent = !settings?.raceModeAvailable ? 'ゲームの種類の選択には対応したRaceアプリへ更新してください。' :
    settings.hasQueuedPreset ? `現在: ${modes[settings.activePreset]}。次の試合から ${modes[settings.selectedPreset]} に切り替わります。` :
    `選択中: ${modes[settings.selectedPreset]}。選択はアプリ終了まで保持します。`;

}

function renderMaintenance(settings) {
  const known = settings?.maintenanceAvailable === true && typeof settings.maintenanceActive === 'boolean';
  if (known && lastMaintenanceActive !== settings.maintenanceActive) $('maintenance-feedback').textContent = '';
  if (known) lastMaintenanceActive = settings.maintenanceActive;
  const active = known ? settings.maintenanceActive : lastMaintenanceActive === true;
  const current = connected && known;
  $('maintenance-panel').classList.toggle('is-active', active);
  $('maintenance-status').textContent = current ? active ? 'メンテナンス中' : '通常表示' : active ? 'メンテナンス中（最後の確認）' : '状態未確認';
  $('maintenance-description').textContent = !current ?
    (active ? '接続が確認できません。ゲーム側のメンテナンス表示は保持されます。再接続後に状態を確認してください。' : 'Unity の接続・バージョンを確認してください。切替にはローカルまたはTailscaleの操作担当者権限が必要です。') :
    active ? 'P1・P2・観戦画面に表示中。進行・入力・ゲーム音を停止しています。解除すると停止した段階から再開し、ペダルの解放を待ちます。' :
    'P1・P2・観戦画面を一括で切り替えます。作動中は進行・入力・ゲーム音を停止し、再起動後も保持します。';
  $('maintenance-start').hidden = active;
  $('maintenance-stop').hidden = !active;
}

let statisticsData = null, statisticsPolling = false, statisticsSequence = 0;
const statisticsLimits = new Map();
function statisticsCount(value) { return Number.isFinite(value) && value >= 0 ? Math.floor(value).toLocaleString('ja-JP') : '—'; }
function statisticsTime(value) {
  if (!Number.isFinite(value) || value <= 0) return '—';
  const ms = Math.floor(value);
  return `${Math.floor(ms / 60000)}:${String(Math.floor(ms / 1000) % 60).padStart(2, '0')}.${String(ms % 1000).padStart(3, '0')}`;
}
function statisticsCell(value, tag = 'td') { const cell = document.createElement(tag); cell.textContent = value; return cell; }
function renderStatistics(data, message = '') {
  statisticsData = data;
  $('statistics-today-plays').textContent = statisticsCount(data?.todayPlays);
  $('statistics-today-ranked').textContent = statisticsCount(data?.todayRanked);
  $('statistics-day').textContent = data?.day ? data.day + ' · 端末の現地日付' : '日付を確認できません';
  $('statistics-status').textContent = message || (data ? '保存済みのプレイ記録を表示しています。10秒ごとに更新します。' : '集計データを取得できません。常駐Dashboardへの接続を確認してください。');
  const warnings = Array.isArray(data?.warnings) ? data.warnings : [];
  $('statistics-warnings').hidden = !warnings.length;
  $('statistics-warnings').replaceChildren(...warnings.map(value => statisticsCell(value, 'li')));
  const boards = Array.isArray(data?.boards) ? data.boards : [];
  const sections = boards.map(board => {
    const section = document.createElement('section'); section.className = 'statistics-board';
    const heading = document.createElement('h3'); heading.textContent = board.label || '保存済みコース'; section.append(heading);
    if (!board.goalLaps) section.append(statisticsCell('過去の記録のため、周回数などの条件名は保存されていません。', 'p'));
    const rows = Array.isArray(board.rows) ? board.rows : [];
    if (!rows.length) { section.append(statisticsCell('この条件の完走記録はまだありません。', 'p')); return section; }
    section.append(statisticsCell('本日のベスト ' + statisticsTime(rows[0].milliseconds), 'p'));
    const wrap = document.createElement('div'); wrap.className = 'table-wrap';
    const table = document.createElement('table'); const head = document.createElement('thead');
    const header = document.createElement('tr'); ['順位', 'プレイヤー', '総タイム', '完走時刻'].forEach(label => header.append(statisticsCell(label, 'th')));
    head.append(header); const body = document.createElement('tbody');
    const limit = statisticsLimits.get(board.id) || 20;
    rows.slice(0, limit).forEach(entry => {
      const row = document.createElement('tr');
      row.append(statisticsCell(statisticsCount(entry.rank) + '位'), statisticsCell(`第${statisticsCount(entry.playNumber)}レース P${entry.playerNumber}`), statisticsCell(statisticsTime(entry.milliseconds)), statisticsCell(entry.finishedAt || '—'));
      body.append(row);
    });
    table.append(head, body); wrap.append(table); section.append(wrap);
    if (rows.length > limit) { const more = document.createElement('button'); more.type = 'button'; more.dataset.statisticsMore = board.id; more.textContent = `さらに20件表示（全${rows.length}件）`; section.append(more); }
    return section;
  });
  $('statistics-rankings').replaceChildren(...(sections.length ? sections : [statisticsCell(data ? '本日のランキング記録はまだありません。' : 'ランキングを取得できません。', 'p')]));
  const period = $('statistics-period').value || '30';
  const cutoff = data?.day && period !== 'all' ? Date.parse(data.day + 'T00:00:00Z') - (Number(period) - 1) * 86400000 : -Infinity;
  const days = (Array.isArray(data?.days) ? data.days : []).filter(day => Date.parse(day.day + 'T00:00:00Z') >= cutoff);
  const maximum = Math.max(1, ...days.map(day => Number.isFinite(day.plays) ? day.plays : 0));
  $('statistics-days').replaceChildren(...days.map(day => {
    const row = document.createElement('tr');
    const barCell = document.createElement('td'); const meter = document.createElement('meter'); meter.min = 0; meter.max = maximum; meter.value = day.plays; meter.setAttribute('aria-label', day.day + ': ' + statisticsCount(day.plays) + '人'); barCell.append(meter);
    row.append(statisticsCell(day.day), statisticsCell(statisticsCount(day.plays) + '人'), barCell, statisticsCell(day.legacyFinishes > 0 ? `開始記録 ${statisticsCount(day.starts)}人 ＋ 過去の完走 ${statisticsCount(day.legacyFinishes)}人` : day.starts > 0 ? 'プレイ開始の記録' : '記録なし')); return row;
  }));
  $('statistics-history-note').textContent = data ? `${days.length}日分の保存済み履歴を表示しています。履歴のない日は人数を推定していません。` : '履歴を取得できません。';
}
function showMoreStatistics(board) { statisticsLimits.set(board, (statisticsLimits.get(board) || 20) + 20); renderStatistics(statisticsData); }
async function pollStatistics() {
  if (statisticsPolling) return;
  if (!localMode) { renderStatistics(null, 'プレイ集計は運営用の常駐Dashboardから確認してください。'); return; }
  const sequence = ++statisticsSequence; statisticsPolling = true; $('statistics-refresh').disabled = true;
  try {
    const response = await apiFetch('/api/statistics', { signal: AbortSignal.timeout(8000) });
    if (!response.ok) throw new Error('statistics');
    const data = await response.json();
    if (sequence === statisticsSequence) renderStatistics(data);
  } catch {
    renderStatistics(statisticsData, statisticsData ? '集計の更新に失敗しました。最後に取得できた記録を表示しています。' : '集計を取得できません。常駐Dashboardを更新して接続を確認してください。');
  } finally { statisticsPolling = false; $('statistics-refresh').disabled = false; }
}

function renderOperatorUpdate() {
  if (!operatorStatus || !localMode) return;
  $('operator-update-status').textContent = operatorStatus.message || '更新状況を確認しています。';
  $('operator-update-log').textContent = operatorStatus.logFolder ? 'ログ: ' + operatorStatus.logFolder : '';
  const progress = operatorStatus.progress;
  $('operator-build-progress').hidden = !progress;
  if (progress) {
    const total = Number.isFinite(progress.totalSteps) && progress.totalSteps > 0 ? progress.totalSteps : 6;
    const done = Number.isFinite(progress.completedSteps) ? Math.max(0, Math.min(total, progress.completedSteps)) : 0;
    $('operator-progress-bar').max = total; $('operator-progress-bar').value = done;
    $('operator-progress-label').textContent = `工程 ${done} / ${total} 完了 · ${progress.stage || '確認中'}`;
    const cache = progress.cache === 'reused' ? '前回のキャッシュを再利用' : progress.cache === 'cold' ? '初回読み込み（次回からキャッシュを再利用）' : '';
    $('operator-build-task').textContent = [progress.task, cache].filter(Boolean).join(' · ');
    $('operator-build-activity').hidden = !operatorStatus.busy || operatorStatus.phase !== 'building';
    const seconds = value => Number.isFinite(value) ? Math.max(0, Math.floor(value)) : 0;
    const elapsed = seconds(progress.elapsedSeconds);
    $('operator-build-timing').textContent = `経過 ${Math.floor(elapsed / 60)}分${elapsed % 60}秒 · ログ更新 ${seconds(progress.quietSeconds)}秒前`;
    $('operator-build-quiet').hidden = progress.logQuiet !== true;
    $('operator-build-quiet').textContent = progress.logQuiet ? 'しばらくログ更新がありません。長時間のコンパイル中の可能性があります。必要なら更新をキャンセルできます。' : '';
    const log = $('operator-build-log');
    const previousTop = log.scrollTop;
    const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 35;
    const lines = Array.isArray(progress.logLines) ? progress.logLines.filter(line => typeof line === 'string') : [];
    const text = lines.length ? lines.join('\n') : 'ログの出力を待っています。';
    if (log.textContent !== text) { log.textContent = text; log.scrollTop = atBottom ? log.scrollHeight : previousTop; }
  }
  $('operator-update').disabled = operatorBusy || !operatorStatus.canControl || !operatorStatus.available || operatorStatus.busy;
  const cancel = $('operator-cancel');
  cancel.hidden = !operatorStatus.busy;
  cancel.disabled = operatorBusy || !operatorStatus.canControl || !operatorStatus.canCancel;
  cancel.textContent = operatorStatus.phase === 'cancelling' ? 'キャンセル処理中…' : operatorStatus.canCancel ? (operatorStatus.phase === 'waiting-race' ? '更新をキャンセル' : 'ビルドをキャンセル') : !operatorStatus.phase ? '更新サービスの更新が必要です' : '切り替え中はキャンセルできません';
  if (!settingsBusy && (operatorStatus.busy || ['failed', 'cancelled'].includes(operatorStatus.phase)))
    $('settings-status').textContent = operatorStatus.message;

}
async function pollOperatorUpdate() {
  if (!localMode || operatorPolling) return;
  operatorPolling = true;
  try {
    const response = await apiFetch('/api/operator-update', { signal: AbortSignal.timeout(5000) });
    if (response.ok) { operatorStatus = await response.json(); renderOperatorUpdate(); }
    else if (operatorStatus) { operatorStatus = {message:'更新サービスに接続できません。管理画面の常駐プログラムを確認してください。'}; renderOperatorUpdate(); }
  } catch {
    if (operatorStatus) { operatorStatus = {message:'更新サービスに接続できません。管理画面の常駐プログラムを確認してください。'}; renderOperatorUpdate(); }
  } finally { operatorPolling = false; }
}
async function submitOperatorUpdate(control) {
  if (control.disabled || operatorBusy || !operatorStatus?.canControl || !operatorStatus.available) return;
  operatorBusy = true; renderOperatorUpdate();
  const id = newRequestId();
  try {
    const response = await apiFetch('/api/operator-update', { method:'POST', headers:{'Content-Type':'application/json'},
      body:JSON.stringify({id}), signal:AbortSignal.timeout(12000) });
    const result = await response.json();
    operatorStatus = {...operatorStatus, message:result.message, logFolder:result.logFolder, requestId:result.requestId, progress:null, available:false, busy:!!result.ok, phase:result.ok?'fetching':'failed'};
    renderOperatorUpdate();
    $('settings-status').textContent = result.message;
  } catch {
    $('settings-status').textContent = '更新開始を確認できません。進行状況とログを確認してください。';
  } finally { operatorBusy = false; control.blur(); await pollOperatorUpdate(); }
}

async function cancelOperatorUpdate() {
  if (operatorBusy || !operatorStatus?.canControl || !operatorStatus.canCancel || !operatorStatus.requestId) return;
  const requestId = operatorStatus.requestId;
  operatorBusy = true; renderOperatorUpdate();
  try {
    const response = await apiFetch('/api/operator-update', { method:'POST', headers:{'Content-Type':'application/json'},
      body:JSON.stringify({id:newRequestId(),command:'cancel',requestId}), signal:AbortSignal.timeout(12000) });
    const result = await response.json();
    if (operatorStatus?.requestId !== requestId) return;
    operatorStatus = {...operatorStatus,message:result.message,canCancel:false,phase:result.ok?'cancelling':operatorStatus.phase};
    renderOperatorUpdate();
    $('settings-status').textContent = result.message;
  } catch {
    $('settings-status').textContent = 'キャンセルの結果を確認できません。最新の進行状況を確認してください。';
  } finally { operatorBusy = false; await pollOperatorUpdate(); }
}
function unavailableSettingsMessage() {
  if (operatorStatus?.busy || ['failed','cancelled'].includes(operatorStatus?.phase)) return operatorStatus.message;
  if (operatorStatus?.phase === 'completed') return '新しいRaceの接続を待っています。更新結果とログは「ゲームの更新」で確認できます。';
  return '設定を取得できません。Unity の接続・バージョンを確認してください。';
}

function renderSettings(settings, canControl) {
  renderMaintenance(settings);
  $('operator-update-status').textContent = connected ? settings?.updateStatus ?? '更新機能のあるバージョンで起動してください。' : 'Unity の接続を待っています。更新中は接続が切れ、新しいアプリの起動後に再接続します。';
  renderFeatureSettings(settings);
  const enabled = connected && localMode && canControl && !!settings && !settingsBusy;
  document.querySelectorAll('[data-setting]').forEach(control => {
    const command = control.dataset.setting;
    let available = true;
    if (['inputMode', 'steeringMode', 'constantPedal', 'constantPaused', 'serialDebug'].includes(command))
      available = !!settings?.inputAvailable;
    if (['constantPedal', 'constantPaused'].includes(command)) available &&= settings?.steeringMode === 'ConstantPedal';
    if (['feature', 'allFeaturesOff'].includes(command)) available = !!settings?.featuresAvailable;
    if (command === 'updateAndRun') available = settings?.updateAvailable === true && settings?.updateBusy !== true;
    if (command === 'maintenance') available = settings?.maintenanceAvailable === true;
    if (command === 'repositionWindows') available = settings?.windowRepositionAvailable === true;
    if (command === 'testRace') available = !!settings?.canStartTestRace;
    if (command === 'raceMode') available = settings?.raceModeAvailable === true;
    if (command === 'outputDelay') available = Array.isArray(settings?.outputDelayMs);
    if (command === 'device') available = !!settings?.outputAvailable?.[Number(control.dataset.settingArg)];
    if (command === 'refreshDevices') available = !!settings?.audioRefreshAvailable;
    control.disabled = !enabled || !available;
  });
  renderOperatorUpdate();
  $('settings-reposition-note').textContent = settings?.windowRepositionAvailable === true ?
    'P1・P2を割り当て済みの外部ディスプレイ、観戦画面を起動時のディスプレイへ戻し、全画面で表示します。未接続の画面は再接続時に復帰します。' :
    '再配置には対応したMac版Raceアプリ（Metal）を起動してください。初回配置には独立した外部ディスプレイ2台が必要です。';
  if (settingsBusy) return;
  for (const name of ['master', 'bgm', 'engine', 'effects', 'constantPedal']) {
    const control = $('settings-' + name);
    if (document.activeElement !== control) control.value = Math.round((settings?.[name] ?? 0) * 100);
    $('settings-' + name + '-value').textContent = settings ? control.value + '%' : '—';
  }
  for (const name of ['inputMode', 'steeringMode', 'constantPaused', 'serialDebug', 'featurePanel']) {
    const control = $('settings-' + name);
    if (document.activeElement === control) continue;
    if (['constantPaused', 'serialDebug', 'featurePanel'].includes(name)) control.checked = !!settings?.[name];
    else control.value = settings?.[name] ?? '';
  }
  for (let player = 0; player < 2; player++) {
    const delay = $('settings-delay-' + player);
    if (document.activeElement !== delay) delay.value = settings?.outputDelayMs?.[player] ?? 0;
    $('settings-delay-' + player + '-value').textContent = delay.value + ' ms';
    const select = $('settings-output-' + player);
    if (document.activeElement !== select) {
      const options = [['', '出力なし'], ...(settings?.deviceKeys || []).map((key, index) => [key, settings.deviceNames[index]])];
      const signature = JSON.stringify([options, settings?.selectedDevices]);
      if (select.dataset.options !== signature) {
        select.replaceChildren(...options.map(([key, label]) => {
          const option = document.createElement('option'); option.value = key; option.textContent = label;
          option.disabled = !!key && key === settings?.selectedDevices?.[1 - player];
          return option;
        }));
        select.dataset.options = signature;
      }
      select.value = settings?.selectedDevices?.[player] ?? '';
    }
    $('settings-output-' + player + '-status').textContent = settings?.outputStatus?.[player] ?? '未接続';
  }
  if (!settingsBusy && (!settings || !canControl || !localMode))
    $('settings-status').textContent = !settings ? unavailableSettingsMessage() :
      !localMode ? 'この設定はローカルまたはTailscaleの管理画面で操作できます。' : '状態更新待ち、または閲覧専用のため変更できません。';
  else if (!settingsBusy && /取得できません|閲覧専用|接続を待|管理画面で操作/.test($('settings-status').textContent))
    $('settings-status').textContent = '変更はゲームへ即時反映されます。';
}

async function submitSetting(control) {
  if (control.dataset.setting === 'updateAndRun' && operatorStatus) { await submitOperatorUpdate(control); return; }
  if (!connected || !localMode || !latestCanControl || !latestSnapshot?.settings || settingsBusy || control.disabled) return;
  const command = control.dataset.setting;
  let args;
  if (command === 'feature') args = [control.dataset.settingArg, String(control.checked)];
  else if (command === 'volume') args = [control.dataset.settingArg, String(Number(control.value) / 100)];
  else if (command === 'outputDelay') args = [control.dataset.settingArg, String(Number(control.value))];
  else if (command === 'device') args = [control.dataset.settingArg, control.value];
  else if (command === 'testRace') args = control.dataset.settingArg === 'both' ? ['both'] : [];
  else if (['refreshDevices', 'allFeaturesOff', 'updateAndRun', 'repositionWindows'].includes(command)) args = [];
  else if (command === 'constantPedal') args = [String(Number(control.value) / 100)];
  else args = [control.type === 'checkbox' ? String(control.checked) : control.value];
  const requestedSession = latestSnapshot.sessionId;
  const payload = { command, args, settingsRevision: latestSnapshot.settingsRevision };
  settingsBusy = true;
  renderSettings(latestSnapshot.settings, false);
  $('settings-status').textContent = '設定を反映中…';
  if (command === 'maintenance') $('maintenance-feedback').textContent = '切り替えを反映中…';
  let result;
  try {
    if (liveController) result = await liveController.command('settings', payload);
    else {
      const request = { ...payload, id: newRequestId(), targetSession: latestSnapshot.sessionId,
        expectedState: latestSnapshot.state, stateRevision: latestSnapshot.stateRevision, observedAt: latestSnapshot.serverTime };
      try {
        const response = await apiFetch('/api/action/settings', { method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(request), signal: AbortSignal.timeout(8000) });
        result = await response.json();
      } catch {
        // Recover the original result without executing the setting a second time.
        const response = await apiFetch('/api/command/' + request.id, { signal: AbortSignal.timeout(4000) });
        if (response.ok) result = await response.json();
      }
      await poll();
    }
  } catch { }
  finally {
    settingsBusy = false;
    if (result?.settings && latestSnapshot?.sessionId === requestedSession && result.settingsRevision >= latestSnapshot.settingsRevision)
      latestSnapshot = { ...latestSnapshot, settings: result.settings, settingsRevision: result.settingsRevision };
    // Release focused controls so the acknowledged Unity values can replace drafts.
    control.blur();
    renderSettings(connected ? latestSnapshot?.settings : null, connected && latestCanControl);
    $('settings-status').textContent = result?.ok ? (command === 'maintenance' ? (args[0] === 'true' ? '全ゲーム画面をメンテナンス表示にしました。' : 'メンテナンスを解除しました。') : command === 'testRace' ? 'テストレースを開始しました。' : command === 'repositionWindows' ? 'ウィンドウを既定のディスプレイへ全画面で再配置しました。未接続の画面は再接続時に復帰します。' : '設定を反映しました。') : result ?
      result.reason === 'window_layout_unavailable' ? '再配置できませんでした。外部ディスプレイ2台の接続とRaceアプリのバージョンを確認してください。' :
      result.reason === 'audio_unavailable' ? '出力機器を開けませんでした。接続を確認して再検出してください。' :
      command === 'updateAndRun' && result.reason && !['state_changed','settings_changed'].includes(result.reason) ? result.reason :
      command === 'updateAndRun' ? result.settings?.updateStatus ?? 'ゲーム状態が変わりました。タイトル・結果・メンテナンス画面で実行してください。' :
      '設定またはゲーム状態が変わりました。最新の表示を確認して操作してください。' :
      '反映結果を確認できません。最新の設定を確認してください。操作は再送していません。';
    if (command === 'maintenance') $('maintenance-feedback').textContent = $('settings-status').textContent;
  }
}

function renderRemoteInputs(control, canInput) {
  latestInputAllowed = connected && localMode && canInput && !!control?.available;
  for (let player = 0; player < 2; player++) {
    const local = remotePlayers[player], remote = control?.players?.find(item => item.number === player + 1);
    const currentLease = local.generation == null || (remote?.generation ?? -1) >= local.generation;
    const occupied = remote?.active && remote.owner !== inputControllerId && (!local.active || currentLease);
    if (local.active && (!latestInputAllowed || !remote?.available || occupied ||
        (local.generation != null && currentLease && !remote?.active) ||
        local.anchor?.sessionId !== latestSnapshot?.sessionId || local.anchor?.stateRevision !== latestSnapshot?.stateRevision))
      stopRemoteInput(player);
    $('keyboard-take-' + player).disabled = !latestInputAllowed || !remote?.available || !!occupied || local.active;
    $('keyboard-stop-' + player).disabled = !local.active;
    document.querySelectorAll('[data-input-hold], [data-input-pulse]').forEach(button => {
      if (Number(button.dataset.inputPlayer) !== player) return;
      button.disabled = !latestInputAllowed || (button.dataset.inputPulse === 'reset' ? !remote?.canRecover : !local.active);
      if (button.dataset.inputHold) button.setAttribute('aria-pressed', String([...local.pointers.values()].includes(button.dataset.inputHold)));
    });
    document.querySelectorAll('[data-menu-action]').forEach(button => {
      if (Number(button.dataset.menuPlayer) === player) button.disabled = !latestInputAllowed || !remote?.menuOpen;
    });
    const status = $('keyboard-status-' + player);
    if (!latestInputAllowed) status.textContent = !control ? 'Unity の接続・バージョンを確認してください。' : '閲覧専用、または状態の更新待ちです。';
    else if (occupied) status.textContent = '別の管理画面が操作中です。';
    else if (local.active) status.textContent = '遠隔操作中：押しているボタンをゲームへ入力します。';
    else if (remote?.menuOpen) status.textContent = 'ESCメニューが開いています。下のメニュー操作を使えます。';
    else if (!remote?.available) status.textContent = 'この段階ではプレイヤー入力を受け付けません。';
    else if (!status.dataset.error) status.textContent = '操作を開始すると実機・キーボードより優先します。';
  }
  $('keyboard-escape').disabled = !latestInputAllowed || !control?.canEscape;
  $('keyboard-release-all').disabled = !remotePlayers.some(player => player.active);
}

function inputRequest(player, command, buttons = []) {
  const local = remotePlayers[player], epoch = local.epoch;
  const dispatch = () => {
    if (command !== 'release' && (!local.active || local.epoch !== epoch)) return;
    return performInputRequest(player, command, buttons);
  };
  if (command === 'release') return dispatch(); // Never wait behind a stalled press to release.
  const result = local.tail.catch(() => {}).then(dispatch);
  local.tail = result.catch(() => {});
  return result;
}

async function performInputRequest(player, command, buttons) {
  const local = remotePlayers[player];
  const snapshot = command === 'release' ? local.anchor : latestSnapshot;
  if (!snapshot?.sessionId) return;
  const request = { id:newRequestId(), targetSession:snapshot.sessionId, expectedState:snapshot.state,
    stateRevision:snapshot.stateRevision, observedAt:snapshot.serverTime, command,
    args:[String(player), inputControllerId, String(++local.sequence), ...buttons] };
  const response = await apiFetch('/api/action/input', { method:'POST', headers:{'Content-Type':'application/json'},
    body:JSON.stringify(request), signal:AbortSignal.timeout(800) });
  return response.json();
}

async function pumpRemoteInput(player) {
  const local = remotePlayers[player];
  if (!local.active || local.inFlight || !latestInputAllowed) return;
  const epoch = local.epoch;
  local.inFlight = true;
  try {
    const result = await inputRequest(player, local.started ? 'hold' : 'begin', local.started ? [...new Set(local.pointers.values())] : []);
    if (local.epoch !== epoch) return;
    if (result?.ok) local.started = true;
    if (result?.ok && result.inputControl) {
      const acknowledged = result.inputControl.players?.find(item => item.number === player + 1);
      if (acknowledged) local.generation = acknowledged.generation;
    }
    if (!result?.ok) {
      stopRemoteInput(player);
      const status = $('keyboard-status-' + player); status.dataset.error = 'true';
      status.textContent = result?.reason === 'input_busy' ? '別の管理画面が操作中です。' : '入力を受け付けられません。状態を確認して開始し直してください。';
    }
  } catch {
    if (local.epoch === epoch) {
      stopRemoteInput(player);
      const status = $('keyboard-status-' + player); status.dataset.error = 'true';
      status.textContent = '入力の通信が途切れました。Unity側でも約1秒で解除します。';
    }
  } finally { local.inFlight = false; }
}

function beginRemoteInput(player) {
  if (!latestInputAllowed || $('keyboard-take-' + player).disabled) return;
  inputControllerId ||= newRequestId();
  const local = remotePlayers[player];
  local.active = true; local.epoch++; local.started = false; local.generation = null; local.pointers.clear(); local.anchor = {...latestSnapshot};
  $('keyboard-status-' + player).dataset.error = '';
  renderRemoteInputs(latestSnapshot.inputControl, true);
  void pumpRemoteInput(player);
}

function stopRemoteInput(player) {
  const local = remotePlayers[player];
  if (!local.active) return;
  local.active = false; local.epoch++; local.pointers.clear();
  void inputRequest(player, 'release').catch(() => {});
}
function stopAllRemoteInputs() { for (let player = 0; player < 2; player++) stopRemoteInput(player); }

async function pulseRemoteInput(player, button) {
  if (button === 'reset') {
    if (latestInputAllowed && latestSnapshot?.inputControl?.players?.find(item => item.number === player + 1)?.canRecover)
      await sendKeyboardMenu('recover', player);
    return;
  }
  if (!latestInputAllowed || !remotePlayers[player].active) return;
  try {
    const result = await inputRequest(player, 'pulse', [button]);
    if (!result?.ok) throw new Error('input rejected');
    $('keyboard-status-' + player).textContent = button === 'reset' ? '復帰入力を送りました。' : 'スタート入力を送りました。';
  } catch {
    stopRemoteInput(player);
    $('keyboard-status-' + player).textContent = '入力結果を確認できません。入力は再送していません。';
  }
}

async function sendKeyboardMenu(command, player) {
  if (!latestInputAllowed) return;
  if (command !== 'recover') stopAllRemoteInputs();
  const snapshot = latestSnapshot;
  const request = { id:newRequestId(), targetSession:snapshot.sessionId, expectedState:snapshot.state,
    stateRevision:snapshot.stateRevision, observedAt:snapshot.serverTime, command, args:player == null ? [] : [String(player)] };
  let result;
  try {
    const response = await apiFetch('/api/action/keyboardMenu', { method:'POST', headers:{'Content-Type':'application/json'},
      body:JSON.stringify(request), signal:AbortSignal.timeout(4000) });
    result = await response.json();
  } catch {
    try { const response = await apiFetch('/api/command/' + request.id, {signal:AbortSignal.timeout(2000)});
      if (response.ok) result = await response.json(); } catch { }
  }
  const message = result?.ok ? (command === 'recover' ? '車両をコースへ復帰しました。' : 'メニュー操作を実行しました。') : result ?
    '現在の状態では操作できません。ゲーム画面を確認してください。' : '結果を確認できません。操作は再送していません。';
  $('keyboard-menu-status').textContent = message;
  if (command === 'recover') $('home-recovery-status').textContent = `P${player + 1}：${message}`;
}

document.querySelectorAll('[data-action]').forEach((button) => {
  button.addEventListener('click', async () => {
    if (!localMode || !connected || !actionAvailable(button.dataset.action, latestSnapshot)) return;
    if (liveController) { await liveController.command(button.dataset.action); return; }
    button.disabled = true;
    $('control-note').textContent = 'Unity に操作を送信中…';
    try {
      const request = { id: newRequestId(), targetSession: latestSnapshot.sessionId, expectedState: latestSnapshot.state, stateRevision: latestSnapshot.stateRevision, observedAt: latestSnapshot.serverTime };
      const response = await apiFetch('/api/action/' + button.dataset.action, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
      if (!response.ok) throw new Error('操作を受け付けられませんでした');
      await poll();
    } catch {
      $('control-note').textContent = '操作に失敗しました。接続とゲーム状態を確認してください。';
      button.disabled = false;
    }
  });
});

document.querySelectorAll('[data-page-link]').forEach(link => link.addEventListener('click', event => {
  event.preventDefault();
  const page = dashboardRoute('#' + link.dataset.pageLink);
  if (location.hash !== '#' + page) location.hash = page;
  showDashboardPage(page, true);
  window.scrollTo({top:0,behavior:'instant'});
}));
window.addEventListener('hashchange', () => {
  if (location.hash === '#page-title') $('page-title').focus();
  else showDashboardPage(dashboardRoute(location.hash), true);
});
showDashboardPage(dashboardRoute(location.hash));
renderHome(null);

$('raw-pause').addEventListener('click', () => {
  rawPaused = !rawPaused;
  $('raw-pause').textContent = rawPaused ? '表示を再開' : '表示を一時停止';
  $('raw-pause').setAttribute('aria-pressed', String(rawPaused));
  renderRaw(rawPending, rawGap, !rawPaused);
});
$('raw-open').addEventListener('click', () => {
  $('raw-dialog').showModal();
  renderRaw(rawPending, rawGap, !rawPaused);
  if (!rawPaused) $('raw-log').scrollTop = $('raw-log').scrollHeight;
});
$('raw-close').addEventListener('click', () => $('raw-dialog').close());
$('raw-dialog').addEventListener('click', (event) => {
  const bounds = $('raw-dialog').getBoundingClientRect();
  if (event.target === $('raw-dialog') &&
    (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) $('raw-dialog').close();
});
$('raw-dialog').addEventListener('close', () => $('raw-open').focus());
$('serial-command').addEventListener('change', updateCommandFields);
$('command-stream').addEventListener('change', updateCommandFields);
$('serial-command-form').addEventListener('submit', submitSerialCommand);
updateCommandFields();
setSerialControls(false);
renderPlayerProgress(null); renderHardware(null); updateActions(null, false);

function updateClock() {
  $('clock').textContent = new Date().toLocaleTimeString('ja-JP', { hour12: false });
  if (connected && latestSnapshot) renderSerialAlerts(latestSnapshot.serial, latestSnapshot.inputMode, latestSnapshot);
}
updateClock();
setInterval(updateClock, 1000);
if (localMode) {
  void pollOperatorUpdate(); setInterval(pollOperatorUpdate, 2000);
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.altKey && (event.code === 'KeyU' || event.key?.toLowerCase() === 'u')) {
      event.preventDefault(); void submitSetting($('operator-update'));
    }
  });
  import('./live.js').then(({ startLive }) => startLive({ render, setConnection, renderTelemetry, refreshFrames, apiFetch, requestId: newRequestId }))
    .then(controller => { liveController = controller; })
    .catch(() => {
      if (location.origin === 'http://127.0.0.1:8765') { poll(); setInterval(poll, 1000); }
      else { setConnection(false); $('live-detail').textContent = '接続ブリッジに接続できません。Unityと補助ブリッジを確認してください。'; }
    });
} else {
  $('transport-status').textContent = 'Firebase / 定期更新';
  document.body.classList.add('remote');
  import('./remote.js').then(({ startRemote }) => startRemote({ render, setConnection }))
    .catch(() => { $('remote-status').textContent = '遠隔監視の読み込みに失敗しました。ページを再読み込みしてください。'; });
}

$('statistics-refresh').addEventListener('click', () => void pollStatistics());
$('statistics-period').addEventListener('change', () => renderStatistics(statisticsData));
$('statistics-rankings').addEventListener('click', event => { if (event.target.dataset.statisticsMore) showMoreStatistics(event.target.dataset.statisticsMore); });
setInterval(() => { if (activePage === 'statistics') void pollStatistics(); }, 10000);

$('operator-cancel').addEventListener('click', () => void cancelOperatorUpdate());

document.querySelectorAll('[data-setting]').forEach(control => {
  control.addEventListener(control.tagName === 'BUTTON' ? 'click' : 'change', () => void submitSetting(control));
  if (control.type === 'range') control.addEventListener('input', () => {
    const name = control.dataset.setting === 'volume' ? control.dataset.settingArg : 'constantPedal';
    $('settings-' + name + '-value').textContent = control.value + '%';
  });
});
renderSettings(null, false);

$('settings-features').addEventListener('change', event => {
  if (event.target.dataset.setting === 'feature') void submitSetting(event.target);
});

// Pointer capture supports mouse, pen and simultaneous touches; cancellation always releases movement.
document.querySelectorAll('[data-input-take]').forEach(button => button.addEventListener('click', () => beginRemoteInput(Number(button.dataset.inputPlayer))));
document.querySelectorAll('[data-input-stop]').forEach(button => button.addEventListener('click', () => {
  stopRemoteInput(Number(button.dataset.inputPlayer)); renderRemoteInputs(latestSnapshot?.inputControl, latestInputAllowed);
}));
document.querySelectorAll('[data-input-pulse]').forEach(button => button.addEventListener('click', () => void pulseRemoteInput(Number(button.dataset.inputPlayer), button.dataset.inputPulse)));
document.querySelectorAll('[data-input-hold]').forEach(button => {
  const player = Number(button.dataset.inputPlayer), local = remotePlayers[player];
  const release = id => { if (local.pointers.delete(id)) { void pumpRemoteInput(player); renderRemoteInputs(latestSnapshot?.inputControl, latestInputAllowed); } };
  button.addEventListener('pointerdown', event => {
    if (button.disabled || event.button !== 0) return;
    event.preventDefault(); button.setPointerCapture(event.pointerId);
    local.pointers.set(event.pointerId, button.dataset.inputHold); void pumpRemoteInput(player);
    renderRemoteInputs(latestSnapshot?.inputControl, latestInputAllowed);
  });
  for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) button.addEventListener(event, value => release(value.pointerId));
  button.addEventListener('keydown', event => {
    if (!['Space','Enter'].includes(event.code) || button.disabled || event.repeat) return;
    event.preventDefault(); local.pointers.set('key:' + event.code, button.dataset.inputHold); void pumpRemoteInput(player);
  });
  button.addEventListener('keyup', event => release('key:' + event.code));
  button.addEventListener('blur', () => { release('key:Space'); release('key:Enter'); });
  button.addEventListener('contextmenu', event => event.preventDefault());
});
document.querySelectorAll('[data-menu-action]').forEach(button => button.addEventListener('click', () => void sendKeyboardMenu(button.dataset.menuAction, Number(button.dataset.menuPlayer))));
$('keyboard-escape').addEventListener('click', () => void sendKeyboardMenu('escape'));
$('keyboard-release-all').addEventListener('click', () => { stopAllRemoteInputs(); renderRemoteInputs(latestSnapshot?.inputControl, latestInputAllowed); });
window.addEventListener('blur', stopAllRemoteInputs);
window.addEventListener('pagehide', stopAllRemoteInputs);
document.addEventListener('visibilitychange', () => { if (document.visibilityState !== 'visible') stopAllRemoteInputs(); });
setInterval(() => { for (let player = 0; player < 2; player++) void pumpRemoteInput(player); }, 200);
renderRemoteInputs(null, false);
