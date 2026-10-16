// One transport for the complete dashboard. Firebase remains an explicit, separate URL.
export function mergeHistory(previous, incoming, maximum = 500) {
  const byId = new Map(previous.map(entry => [entry.id, entry]));
  for (const entry of incoming) byId.set(entry.id, entry);
  return [...byId.values()].sort((a, b) => a.id - b.id).slice(-maximum);
}

export async function startLive({ render, setConnection, renderTelemetry, refreshFrames, apiFetch }) {
  const infoResponse = await apiFetch('/api/connection', { signal: AbortSignal.timeout(4000) });
  if (!infoResponse.ok) throw new Error('Relay unavailable');
  const info = await infoResponse.json();
  let socket, retry = 500, serialSession = '', session = '', cursor = 0, rawCursor = 0;
  let alerts = [];
  let rawHistory = [], rawGap = false;
  let history = [], snapshot = null, telemetry = null, lastMessageAt = 0, lastStateAt = 0;
  let dirty = false, busy = false, alive = false, gap = false;
  let lastFrames = 0, rtt = null;
  const $ = id => document.getElementById(id);
  $('transport-status').textContent = info.remote ? 'Tailscale / WebSocket' : 'ローカル / WebSocket';
  if (!info.remote && info.remoteUrl) {
    $('share-dashboard').hidden = false;
    $('share-url').value = info.remoteUrl;
    $('share-copy').addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(info.remoteUrl); $('share-status').textContent = 'コピーしました。別端末でTailscaleを接続して開いてください。'; }
      catch { $('share-url').select(); $('share-status').textContent = 'URLを選択しました。コピーして別端末で開いてください。'; }
    });
  }
  function draw() {
    if (!dirty) return;
    dirty = false;
    if (snapshot && alive) {
      const current = Date.now() - lastStateAt < 3000;
      render({ ...snapshot, telemetry, rawLines: rawHistory, rawGap, serial: { ...snapshot.serial, lines: history, alerts } }, { canControl: info.canControl && current && !busy });
      renderTelemetry(telemetry);
      $('live-detail').textContent = `${current ? '状態更新中' : 'Unityの状態更新が停止しています'} / RTT ${rtt ?? '—'} ms${gap ? ' / 保持範囲外のログ欠落あり' : ''}`;
      if (!current) $('control-note').textContent = 'Unityの状態更新を待っています。操作は停止しています。';
      else if (!info.canControl) $('control-note').textContent = '閲覧専用です。操作担当者のみレースを管理できます。';
    }
  }
  function disconnect(attempt) {
    if (attempt !== socket) return;
    alive = false; socket = null;
    setConnection(false);
    $('live-detail').textContent = '再接続中…';
    try { attempt.close(); } catch { }
    setTimeout(connect, retry + Math.random() * 200);
    retry = Math.min(retry * 2, 10000);
  }
  function connect() {
    const endpoint = new URL('/api/events', location.href);
    endpoint.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    endpoint.search = new URLSearchParams({ serialSession, after: String(cursor), rawAfter: String(rawCursor) });
    socket = new WebSocket(endpoint);
    const attempt = socket;
    const connectDeadline = setTimeout(() => { if (!alive) disconnect(attempt); }, 8000);
    socket.onmessage = event => {
      if (attempt !== socket) return;
      let packet;
      try { packet = JSON.parse(event.data); } catch { disconnect(attempt); return; }
      if (!packet.sessionId || !packet.serialSession || !Array.isArray(packet.lines)) { disconnect(attempt); return; }
      if (session !== packet.sessionId || serialSession !== packet.serialSession) {
        history = []; rawHistory = []; rawGap = false; rawCursor = 0; alerts = []; telemetry = null; gap = false;
        if (session !== packet.sessionId) { snapshot = null; lastStateAt = 0; }
      }
      session = packet.sessionId; serialSession = packet.serialSession;
      cursor = packet.cursor;
      rawCursor = packet.rawCursor ?? rawCursor;
      rawHistory = mergeHistory(rawHistory, packet.rawLines || []);
      rawGap ||= packet.rawGap;
      history = mergeHistory(history, packet.lines);
      alerts = packet.alerts || [];
      if (packet.snapshot) { snapshot = packet.snapshot; lastStateAt = Date.now(); }
      if (packet.telemetry) telemetry = packet.telemetry;
      gap ||= packet.gap;
      clearTimeout(connectDeadline);
      lastMessageAt = Date.now(); alive = true; retry = 500; dirty = true;
    };
    socket.onclose = () => {
      clearTimeout(connectDeadline);
      disconnect(attempt);
    };
    socket.onerror = () => { clearTimeout(connectDeadline); disconnect(attempt); };
  }
  connect();
  setInterval(() => {
    if (alive && Date.now() - lastMessageAt > 4000) { disconnect(socket); return; }
    if (alive && snapshot && Date.now() - lastStateAt > 3000) dirty = true;
    draw();
    if (alive && snapshot && Date.now() - lastStateAt < 3000 && Date.now() - lastFrames > 2000) {
      lastFrames = Date.now(); void refreshFrames();
    }
  }, 33);
  async function measure() {
    const started = performance.now();
    try { const response = await apiFetch('/api/connection', { signal: AbortSignal.timeout(3000) });
      if (response.ok) { rtt = Math.round(performance.now() - started); dirty = true; }
    } catch { }
  }
  void measure(); setInterval(measure, 10000);
  return {
    async command(action, payload = {}) {
      if (!alive || !snapshot || busy || !info.canControl || Date.now() - lastStateAt > 3000) return;
      busy = true; dirty = true; draw();
      const request = { ...payload, id: crypto.randomUUID(), targetSession: snapshot.sessionId,
        expectedState: snapshot.state, stateRevision: snapshot.stateRevision, observedAt: snapshot.serverTime };
      if (action !== 'serial') $('control-note').textContent = 'Unity に操作を送信中…';
      let outcome;
      try {
        const response = await apiFetch('/api/action/' + action, { method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(request), signal: AbortSignal.timeout(8000) });
        outcome = await response.json();
      } catch {
        // Never replay a race operation on reconnect. Query the original ID instead.
        try { const response = await apiFetch('/api/command/' + request.id, { signal: AbortSignal.timeout(4000) });
          if (response.ok) outcome = await response.json(); } catch { }
      }
      busy = false; dirty = true; draw();
      if (action !== 'serial') $('control-note').textContent = outcome?.ok ? '操作を実行しました。' : outcome ?
        '状態が変わったため操作を受け付けられませんでした。最新の状態を確認してください。' :
        '実行結果を確認できません。操作は再送していません。レース状態を確認してください。';
      return outcome;
    }
  };
}
