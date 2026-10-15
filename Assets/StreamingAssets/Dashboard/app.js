const localMode = location.origin === 'http://127.0.0.1:8765';
const API = localMode ? '' : 'http://127.0.0.1:8765';
const $ = (id) => document.getElementById(id);
const labels = { Title: 'タイトル', CarSelection: '車選択', Tutorial: '練習', Countdown: 'カウントダウン', Game: 'レース中', Goal: 'ゴール処理中', Result: '結果表示' };
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
  const order = ['Title', 'Countdown', 'Game', 'Goal', 'Result'];
  const position = order.indexOf(state === 'CarSelection' || state === 'Tutorial' ? 'Title' : state);
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
  renderParticipants(snapshot);
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
    case 'TEL': {
      if (d.length < 6 || d.slice(0, 6).some((v, i) => !/^-?\d+(?:\.\d+)?$/.test(v) || !Number.isFinite(Number(v)) ||
          (i % 3 === 0 ? Math.abs(Number(v)) > 3600 : Number(v) < 0 || Number(v) > 1)))
        return result('入力データの6項目を確認できないため、値を説明できません。', 'error');
      const player = (index) => {
        const [angle, accel, brake] = d.slice(index * 3, index * 3 + 3).map(Number);
        const steering = angle === 0 ? 'ハンドル中央' : `${angle > 0 ? '右' : '左'}${Math.abs(angle).toLocaleString('ja-JP', { maximumFractionDigits: 6 })}°`;
        const percent = (v) => (v * 100).toLocaleString('ja-JP', { maximumFractionDigits: 3 });
        return `P${index + 1}：${steering}、アクセル${percent(accel)}%、ブレーキ${percent(brake)}%`;
      };
      text = `${player(0)} ／ ${player(1)}`;
      if (status === 'OLD_TEL') text += '（重複または古い入力のため反映しません）';
      if (status === 'NOT_READY') text += '（装置・セッションの準備が整っていないため反映しません）';
      break;
    }
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
  const alerts = (serial.lines || []).map(entry => ({ entry, ...explainSerialEntry(entry) }))
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
    if (!localMode || !connected || actions[button.dataset.action] !== currentState) return;
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
if (localMode) {
  poll();
  setInterval(poll, 1000);
} else {
  document.body.classList.add('remote');
  import('./remote.js').then(({ startRemote }) => startRemote({ render, setConnection }))
    .catch(() => { $('remote-status').textContent = '遠隔監視の読み込みに失敗しました。ページを再読み込みしてください。'; });
}
