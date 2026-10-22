const localMode = location.origin !== 'https://tk75attractions.github.io';
const API = '';
let liveController = null;
let latestSnapshot = null;
let logSignature = '';
let rawPaused = false, rawSignature = '', rawPending = [], rawGap = false;
let rawElements = new Map();
let canSendSerial = false;
let latestCanControl = false;
let settingsBusy = false;
const $ = (id) => document.getElementById(id);
const labels = { Title: 'タイトル', CarSelection: '車選択', Tutorial: 'チュートリアル', Countdown: 'カウントダウン', Game: 'レース中', Goal: 'ゴール処理中', Result: '結果表示' };
const details = { Title: '開始操作を待っています', CarSelection: '参加者が車を選んでいます', Tutorial: '参加者が操作を練習しています', Countdown: 'スタートまでまもなく', Game: 'レース進行中', Goal: '完走処理中', Result: '次のレースを選択できます' };
const actions = { start: 'Title', tutorial: 'CarSelection', countdown: 'Tutorial', result: 'Game', retry: 'Result', title: 'Result' };
function actionAvailable(action, snapshot) {
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

function setConnection(isConnected) {
  connected = isConnected;
  const badge = $('connection');
  badge.classList.toggle('online', isConnected);
  badge.classList.toggle('offline', !isConnected);
  badge.querySelector('span').textContent = isConnected ? (localMode ? 'UNITY 接続中' : '遠隔監視中') : (localMode ? 'UNITY 未接続' : '遠隔未接続');
  $('footer-status').textContent = isConnected ? 'Unity からデータを受信中' : 'Unity との接続を確認中';
  if (!isConnected) {
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
  const order = ['Title', 'CarSelection', 'Tutorial', 'Countdown', 'Game', 'Goal', 'Result'];
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

function render(snapshot, { canControl = localMode } = {}) {
  setConnection(true);
  // An HTTP acknowledgement can arrive before the next WebSocket snapshot.
  if (latestSnapshot && latestSnapshot.sessionId === snapshot.sessionId && latestSnapshot.settingsRevision > snapshot.settingsRevision)
    snapshot = { ...snapshot, settings: latestSnapshot.settings, settingsRevision: latestSnapshot.settingsRevision };
  latestSnapshot = snapshot;
  latestCanControl = canControl;
  renderSettings(snapshot.settings, canControl);
  currentState = snapshot.state;
  updateFlow(snapshot.state);
  $('game-state').textContent = labels[snapshot.state] || snapshot.state;
  $('state-detail').textContent = snapshot.state === 'Countdown' ? `あと ${Math.ceil(snapshot.countdown)} 秒` : (details[snapshot.state] || '状態を確認中');
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
  $('input-mode').textContent = /keyboard/i.test(snapshot.inputMode || '') ? '⌨ キーボード' :
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
  if (localMode) $('control-note').textContent = snapshot.state === 'Game' ? '「結果を表示」はレースを終了し、現在の記録で結果画面へ進みます。' : '現在のゲーム状態に応じて操作できます。';
  renderSerialAlerts(snapshot.serial, snapshot.inputMode, snapshot);
  renderLog(snapshot.serial.lines || []);
  renderTelemetry(snapshot.telemetry || null);
  renderParticipants(snapshot);
  renderPlayerProgress(snapshot);
  renderHardware(snapshot);
  renderRaw(snapshot.rawLines || [], snapshot.rawGap || false);
  setSerialControls(canControl && snapshot.serial.portOpen && snapshot.hardware?.sessionOpen && snapshot.hardware?.recent);
  renderCommandResult(snapshot);
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
    cell(driverLabel(entry.number), entry.isCpu ? 'CPU' : '人間');
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
  const requestAt = d.indexOf('REQ');
  const recovery = after.find(({frame: next}) => {
    if (!next) return false;
    const n = next.data;
    if (command) return next.type === 'ACK' && n[1] === command && n[2] === 'OK';
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
  if (recovery) return recovery.entry.receivedAt || Date.now();
  // Snapshots cover recovery events that have already left the replay window.
  if (!known || (item.entry.receivedAt && snapshot.serverTime < item.entry.receivedAt)) return null;
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
  if (signature === serialAlertSignature) return;
  serialAlertSignature = signature;
  $('serial-alerts').hidden = !alerts.length;
  $('serial-alert-list').replaceChildren(...alerts.map(item => {
    const row = document.createElement('div'); row.className = 'serial-alert ' + item.level + (item.resolvedAt != null ? ' resolved' : '');
    const time = document.createElement('span'); time.className = 'alert-time'; time.textContent = item.entry.time || '—';
    const text = document.createElement('p'); text.textContent = (item.resolvedAt != null ? '解決済み · ' : '未解決 · ') + item.text;
    row.append(time, text); return row;
  }));
}

function isTelemetryEntry(entry) {
  return entry.status !== 'INVALID' && /^@TEL,/.test(entry.line || '') &&
    /\*[0-9A-F]{4}$/.test(entry.line) && rgpCrc(entry.line.slice(1, -5)) === entry.line.slice(-4);
}
function renderTelemetry(entry) {
  const target = $('latest-tel');
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
    Driving: 'レースを走行中', Menu: '中断メニューを表示中', Withdrawn: '途中棄権・相手の走行を待機', Spectating: '相手の走行を観戦中', Finished: '完走・ゴール演出中', DNF: '未完走', Returned: 'タイトルへ復帰済み・相手の復帰を待機',
    Complete: '練習完了・相手の完了を待機', TimedOut: '制限時間終了・相手の完了を待機', Waiting: '練習の開始を待機' };
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
  const faults = cards.filter(([, [level]]) => level === 'fault').length;
  const ready = cards.filter(([, [level]]) => level === 'ok').length;
  const keyboard = /keyboard/i.test(snapshot?.inputMode || '');
  const summary = $('device-summary');
  const level = !snapshot || keyboard ? 'unknown' : faults ? 'fault' :
    ready === cards.length && hardware?.state === 'READY' && hardware?.angleLocked && hardware?.calibrated ? 'ok' : 'unknown';
  summary.className = level;
  summary.textContent = !snapshot ? '確認待ち' : keyboard ? '⌨ 機器入力は未使用' : faults ? `⚠ ${faults}件 要確認` :
    level === 'ok' ? '✓ すべて正常' : '◷ 準備・確認中';
  $('device-summary-items').replaceChildren(...cards.map(([name, [state, text]], index) => {
    const chip = document.createElement('span'); chip.className = 'device-chip ' + state;
    chip.textContent = `${state === 'ok' ? '✓' : state === 'fault' ? '!' : '◷'} ${['USB', 'IMU 1', 'IMU 2', 'ADS', 'P1', 'P2'][index]}`;
    chip.setAttribute('aria-label', `${name}：${text}`);
    chip.title = `${name}：${text}`;
    return chip;
  }));
  $('hardware-state').className = 'mini-status ' + level;
  $('hardware-grid').replaceChildren(...cards.map(([name, [level, state], detail]) => {
    const card = document.createElement('article'); card.className = 'hardware-card ' + level;
    const heading = document.createElement('h3'); heading.textContent = name;
    const value = document.createElement('strong'); value.textContent = state;
    const note = document.createElement('p'); note.textContent = detail;
    card.append(heading, value, note); return card;
  }));
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
      const body = {...payload,id:crypto.randomUUID(),targetSession:latestSnapshot.sessionId,expectedState:latestSnapshot.state,
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
  if (frameBusy || !connected) return;
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

function renderSettings(settings, canControl) {
  const enabled = connected && localMode && canControl && !!settings && !settingsBusy;
  document.querySelectorAll('[data-setting]').forEach(control => {
    const command = control.dataset.setting;
    let available = true;
    if (['inputMode', 'steeringMode', 'constantPedal', 'constantPaused', 'serialDebug'].includes(command))
      available = !!settings?.inputAvailable;
    if (['constantPedal', 'constantPaused'].includes(command)) available &&= settings?.steeringMode === 'ConstantPedal';
    if (command === 'device') available = !!settings?.outputAvailable?.[Number(control.dataset.settingArg)];
    if (command === 'refreshDevices') available = !!settings?.audioRefreshAvailable;
    control.disabled = !enabled || !available;
  });
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
    $('settings-status').textContent = !settings ? '設定を取得できません。Unity の接続・バージョンを確認してください。' :
      !localMode ? 'この設定はローカルまたはTailscaleの管理画面で操作できます。' : '状態更新待ち、または閲覧専用のため変更できません。';
  else if (!settingsBusy && /取得できません|閲覧専用|接続を待|管理画面で操作/.test($('settings-status').textContent))
    $('settings-status').textContent = '変更はゲームへ即時反映されます。';
}

async function submitSetting(control) {
  if (!connected || !localMode || !latestCanControl || !latestSnapshot?.settings || settingsBusy || control.disabled) return;
  const command = control.dataset.setting;
  let args;
  if (command === 'volume') args = [control.dataset.settingArg, String(Number(control.value) / 100)];
  else if (command === 'device') args = [control.dataset.settingArg, control.value];
  else if (command === 'refreshDevices') args = [];
  else if (command === 'constantPedal') args = [String(Number(control.value) / 100)];
  else args = [control.type === 'checkbox' ? String(control.checked) : control.value];
  const requestedSession = latestSnapshot.sessionId;
  const payload = { command, args, settingsRevision: latestSnapshot.settingsRevision };
  settingsBusy = true;
  renderSettings(latestSnapshot.settings, false);
  $('settings-status').textContent = '設定を反映中…';
  let result;
  try {
    if (liveController) result = await liveController.command('settings', payload);
    else {
      const request = { ...payload, id: crypto.randomUUID(), targetSession: latestSnapshot.sessionId,
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
    $('settings-status').textContent = result?.ok ? '設定を反映しました。' : result ?
      result.reason === 'audio_unavailable' ? '出力機器を開けませんでした。接続を確認して再検出してください。' :
      '設定またはゲーム状態が変わりました。最新の表示を確認して操作してください。' :
      '反映結果を確認できません。最新の設定を確認してください。操作は再送していません。';
  }
}

document.querySelectorAll('[data-action]').forEach((button) => {
  button.addEventListener('click', async () => {
    if (!localMode || !connected || !actionAvailable(button.dataset.action, latestSnapshot)) return;
    if (liveController) { await liveController.command(button.dataset.action); return; }
    button.disabled = true;
    $('control-note').textContent = 'Unity に操作を送信中…';
    try {
      const request = { id: crypto.randomUUID(), targetSession: latestSnapshot.sessionId, expectedState: latestSnapshot.state, stateRevision: latestSnapshot.stateRevision, observedAt: latestSnapshot.serverTime };
      const response = await apiFetch('/api/action/' + button.dataset.action, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request) });
      if (!response.ok) throw new Error('操作を受け付けられませんでした');
      await poll();
    } catch {
      $('control-note').textContent = '操作に失敗しました。接続とゲーム状態を確認してください。';
      button.disabled = false;
    }
  });
});

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
  import('./live.js').then(({ startLive }) => startLive({ render, setConnection, renderTelemetry, refreshFrames, apiFetch }))
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

document.querySelectorAll('[data-setting]').forEach(control => {
  control.addEventListener(control.tagName === 'BUTTON' ? 'click' : 'change', () => void submitSetting(control));
  if (control.type === 'range') control.addEventListener('input', () => {
    const name = control.dataset.setting === 'volume' ? control.dataset.settingArg : 'constantPedal';
    $('settings-' + name + '-value').textContent = control.value + '%';
  });
});
renderSettings(null, false);
