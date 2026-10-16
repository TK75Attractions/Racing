const localMode = location.origin !== 'https://tk75attractions.github.io';
const API = '';
let liveController = null;
let latestSnapshot = null;
let logSignature = '';
let rawPaused = false, rawSignature = '', rawPending = [], rawGap = false;
let rawElements = new Map();
let canSendSerial = false;
const $ = (id) => document.getElementById(id);
const labels = { Title: 'タイトル', CarSelection: '車選択', Tutorial: 'チュートリアル', Countdown: 'カウントダウン', Game: 'レース中', Goal: 'ゴール処理中', Result: '結果表示' };
const details = { Title: '開始操作を待っています', CarSelection: '参加者が車を選んでいます', Tutorial: '参加者が操作を練習しています', Countdown: 'スタートまでまもなく', Game: 'レース進行中', Goal: '完走処理中', Result: '次のレースを選択できます' };
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
  badge.querySelector('span').textContent = isConnected ? (localMode ? 'UNITY 接続中' : '遠隔監視中') : (localMode ? 'UNITY 未接続' : '遠隔未接続');
  $('footer-status').textContent = isConnected ? 'Unity からデータを受信中' : 'Unity との接続を確認中';
  if (!isConnected) {
    currentState = null;
    $('game-state').textContent = '接続待機';
    $('state-detail').textContent = localMode ? 'Unity を起動してください' : '遠隔データを待っています';
    $('race-time').textContent = '00:00.0';
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
    $('serial-command-last').textContent = '';
    updateFlow(null);
    renderParticipants(null);
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

function render(snapshot, { canControl = localMode } = {}) {
  setConnection(true);
  latestSnapshot = snapshot;
  currentState = snapshot.state;
  updateFlow(snapshot.state);
  $('game-state').textContent = labels[snapshot.state] || snapshot.state;
  $('state-detail').textContent = snapshot.state === 'Countdown' ? `あと ${Math.ceil(snapshot.countdown)} 秒` : (details[snapshot.state] || '状態を確認中');
  $('race-time').textContent = formatTime(snapshot.raceTime);
  setGoalLap(snapshot.goalLap || '--');
  $('serial-count').textContent = (snapshot.serial.received ?? 0).toLocaleString('ja-JP');
  $('parse-errors').textContent = (snapshot.serial.errors ?? 0).toLocaleString('ja-JP');
  $('parse-errors').classList.toggle('error-count', snapshot.serial.errors > 0);
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
  for (const button of document.querySelectorAll('[data-action]')) button.disabled = !canControl || actions[button.dataset.action] !== currentState;
  if (localMode) $('control-note').textContent = snapshot.state === 'Game' ? '「結果を表示」はレースを終了し、現在の記録で結果画面へ進みます。' : '現在のゲーム状態に応じて操作できます。';
  renderSerialAlerts(snapshot.serial, snapshot.inputMode);
  renderLog(snapshot.serial.lines || []);
  if (snapshot.telemetry) renderTelemetry(snapshot.telemetry);
  renderParticipants(snapshot);
  renderPlayerProgress(snapshot);
  renderHardware(snapshot);
  renderRaw(snapshot.rawLines || [], snapshot.rawGap || false);
  setSerialControls(canControl && snapshot.serial.portOpen && snapshot.hardware?.sessionOpen && snapshot.hardware?.recent);
  $('serial-command-last').textContent = snapshot.hardware?.lastCommand ?
    `装置への直近要求：${snapshot.hardware.lastCommand} / ACK待ち ${snapshot.hardware.pending || 0}件（完了通知は下の通信履歴）` : '';
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
  else if (['NO_RESPONSE', 'WHOAMI_FAILED', 'CONNECTION_LOST'].includes(d[2]) && /^0x[0-9A-F]{2}$/.test(detail[0] || '')) text += ` 対象 ${detail[0]}。`;
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
function renderSerialAlerts(serial, inputMode = '') {
  const unique = new Map([...(serial.lines || []), ...(serial.alerts || [])].map(entry => [entry.id, entry]));
  const alerts = [...unique.values()].sort((a, b) => a.id - b.id).map(entry => ({ entry, ...explainSerialEntry(entry) }))
    .filter(item => item.level !== 'info').reverse().slice(0, 8);
  if (serial.portOpen === false && /serial|シリアル/i.test(inputMode) && !/disabled/i.test(inputMode))
    alerts.unshift({ entry: { time: '現在' }, level: 'warning', text: 'シリアルポートが未接続です。USB接続とUnityのポート設定を確認してください。' });
  if (serial.errors > 0 && !alerts.some(item => item.entry.status === 'INVALID'))
    alerts.push({ entry: { time: '累計' }, level: 'warning', text: `解析エラーが${serial.errors.toLocaleString('ja-JP')}件記録されています。詳細が履歴から消えている場合はUnity側のログも確認してください。` });
  const signature = JSON.stringify(alerts);
  if (signature === serialAlertSignature) return;
  serialAlertSignature = signature;
  $('serial-alerts').hidden = !alerts.length;
  $('serial-alert-list').replaceChildren(...alerts.map(item => {
    const row = document.createElement('div'); row.className = 'serial-alert ' + item.level;
    const time = document.createElement('span'); time.className = 'alert-time'; time.textContent = item.entry.time || '—';
    const text = document.createElement('p'); text.textContent = item.text;
    row.append(time, text); return row;
  }));
}

function isTelemetryEntry(entry) {
  return entry.status !== 'INVALID' && /^@TEL,/.test(entry.line || '') &&
    /\*[0-9A-F]{4}$/.test(entry.line) && rgpCrc(entry.line.slice(1, -5)) === entry.line.slice(-4);
}
function renderTelemetry(entry) {
  const target = $('latest-tel');
  if (!entry || !isTelemetryEntry(entry)) { target.textContent = 'TELの受信を待っています。'; return; }
  const values = entry.line.slice(1, -5).split(',').slice(3, 9);
  if (values.length !== 6 || values.some((v, i) => !Number.isFinite(Number(v)) || (i % 3 === 0 ? Math.abs(Number(v)) > 3600 : Number(v) < 0 || Number(v) > 1))) { target.textContent = 'TELの受信値を確認できません。'; return; }
  const rows = [0, 1].map(i => {
    const [handle, accel, brake] = values.slice(i * 3, i * 3 + 3).map(Number);
    return `P${i + 1}  HANDLE ${handle}° / ACCEL ${(accel * 100).toFixed(1)}% / BRAKE ${(brake * 100).toFixed(1)}%\n    ハンドル ${handle === 0 ? '中央' : handle > 0 ? '右' : '左'} ${Math.abs(handle)}°、アクセル ${(accel * 100).toFixed(1)}%、ブレーキ ${(brake * 100).toFixed(1)}%`;
  });
  target.textContent = `${entry.time || ''}  最新の受信値（ゲームへの適用状態は上の入力表示を参照）\n${rows.join('\n')}\n${entry.line}`;
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
    const time = document.createElement('span'); time.textContent = entry.time;
    const status = document.createElement('span');
    status.textContent = (entry.direction ? entry.direction + ' ' : '') + entry.status;
    const explanation = explainSerialEntry(entry);
    status.className = explanation.level === 'error' ? 'bad' : explanation.level === 'warning' ? 'partial' : 'ok';
    const data = document.createElement('div'); data.className = 'serial-payload';
    const meaning = document.createElement('p'); meaning.className = 'serial-explanation'; meaning.textContent = explanation.text;
    const raw = document.createElement('code'); raw.textContent = entry.line;
    data.append(meaning, raw);
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
  const cards = [
    ['ESP32 / USBシリアル', !snapshot ? ['unknown','未確認'] : !port ? ['fault','未接続'] : known ? ['ok','通信中'] : ['unknown','ポート開放・応答待ち'],
      !snapshot ? 'Unityの接続を待っています' : `${snapshot.serial.port || 'ポート不明'} / ${hardware?.state || '状態未確認'}`],
    ['MPU6050 / IMU（I2C）', sensor(hardware?.imu), hardware?.imuAddress ? `アドレス ${hardware.imuAddress} / 識別値 ${hardware.imuIdentity || '不明'}（型式は装置の構成を確認）` : 'IMU全体の通知。型式・アドレスは未確認'],
    ['ADS1115（I2C）', sensor(hardware?.ads), hardware?.adsAddress ? `アドレス ${hardware.adsAddress} / 全ADSセンサーの状態` : '全ADSセンサーの状態。アドレスは未確認'],
    ...[1,2].map(n => [`P${n} アクセル・ブレーキ`, ['unknown','物理接続：未確認'], '個別の断線検出情報なし。最新の入力値は下のTEL欄で確認'])
  ];
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
  if (canSendSerial && /接続を待|再接続を待/.test($('serial-command-status').textContent))
    $('serial-command-status').textContent = '操作を選択してESP32へ送信できます。';
  for (const control of document.querySelectorAll('[data-serial-control]')) control.disabled = !canSendSerial;
  $('serial-send').disabled = !canSendSerial;
}
function renderRaw(lines, gap = false, force = false) {
  rawPending = lines.slice(-500); rawGap = gap;
  $('raw-status').textContent = `${rawPaused ? '表示を一時停止中（受信は継続）' : '直近の送受信を表示'} / ${rawPending.length} 件${gap ? ' / 保持範囲外の生データ欠落あり' : ''}`;
  if (rawPaused && !force) return;
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
async function submitSerialCommand(event) {
  event.preventDefault();
  if (!canSendSerial || !connected || !localMode) return;
  const payload = {command:$('serial-command').value,args:serialCommandArguments()};
  setSerialControls(false);
  $('serial-command-status').textContent = 'ESP32へ要求を送信中…';
  let outcome;
  try {
    if (liveController) outcome = await liveController.command('serial', payload);
    else {
      const request = {...payload,id:crypto.randomUUID(),targetSession:latestSnapshot.sessionId,expectedState:latestSnapshot.state,
        stateRevision:latestSnapshot.stateRevision,observedAt:latestSnapshot.serverTime};
      const response = await apiFetch('/api/action/serial', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(request)});
      outcome = await response.json();
      await poll();
    }
  } catch { }
  const reasons = {invalid_arguments:'引数が不正です。',session_not_ready:'RGPセッションの確立を待っています。',commands_pending:'未完了の要求が多いため待機してください。',
    command_pending:'同じ種類の要求がACK待ちです。',device_not_ready:'装置がREADYまたはWAIT_ANGLEのときに実行できます。',serial_disconnected:'シリアル接続・装置の応答を確認してください。',write_failed:'シリアル送信に失敗しました。'};
  $('serial-command-status').textContent = outcome?.ok ?
    `要求番号 ${outcome.serialSequence} を送信しました。装置のACKと完了通知を通信履歴で確認してください。` :
    outcome ? `送信できませんでした。${reasons[outcome.reason] || 'ゲームや装置の最新状態を確認してください。'}` : '送信結果を確認できません。再送せず、通信履歴を確認してください。';
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
    if (!localMode || !connected || actions[button.dataset.action] !== currentState) return;
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
  renderRaw(rawPending, rawGap, !rawPaused);
});
$('serial-command').addEventListener('change', updateCommandFields);
$('command-stream').addEventListener('change', updateCommandFields);
$('serial-command-form').addEventListener('submit', submitSerialCommand);
updateCommandFields();
setSerialControls(false);
renderPlayerProgress(null); renderHardware(null);

function updateClock() { $('clock').textContent = new Date().toLocaleTimeString('ja-JP', { hour12: false }); }
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
