const $ = (id) => document.getElementById(id);

export async function startRemote({ render, setConnection }) {
  const panel = $('remote-login');
  const form = $('remote-login-form');
  const account = $('remote-account');
  const status = $('remote-status');
  panel.hidden = false;
  $('cameras-title').textContent = 'プレイヤー状態';
  setConnection(false);

  let config;
  try {
    const response = await fetch('./firebase-config.json', { cache: 'no-store' });
    if (!response.ok) throw new Error('config');
    config = await response.json();
  } catch {
    status.textContent = 'Firebase の公開設定を読み込めませんでした。管理者に確認してください。';
    form.hidden = true;
    return;
  }
  if (!['apiKey', 'authDomain', 'databaseURL', 'projectId', 'appId'].every((key) => config[key])) {
    status.textContent = 'Firebase プロジェクトの設定待ちです。管理者が公開設定を登録すると利用できます。';
    form.hidden = true;
    return;
  }

  let sdk;
  try {
    const [app, auth, database] = await Promise.all([
      import('https://www.gstatic.com/firebasejs/12.19.0/firebase-app.js'),
      import('https://www.gstatic.com/firebasejs/12.19.0/firebase-auth.js'),
      import('https://www.gstatic.com/firebasejs/12.19.0/firebase-database.js')
    ]);
    sdk = { app, auth, database };
  } catch {
    status.textContent = 'Firebase のライブラリを読み込めませんでした。通信を確認してください。';
    return;
  }

  const firebaseApp = sdk.app.initializeApp(config);
  const auth = sdk.auth.getAuth(firebaseApp);
  const db = sdk.database.getDatabase(firebaseApp);
  try {
    await sdk.auth.setPersistence(auth, sdk.auth.browserSessionPersistence);
  } catch {
    status.textContent = 'ブラウザのセッション保存を利用できません。設定を確認してください。';
    return;
  }

  let stopLive = null;
  let stopConnection = null;
  let stopClockOffset = null;
  let stopOperator = null;
  let stopAck = null;
  let latest = null;
  let serverOffset = 0;
  let databaseConnected = false;
  let lastSession = null;
  let lastSequence = -1;
  let showingFresh = false;
  let operator = false;
  let busy = false;
  let pendingId = null;
  let pendingTimer = null;
  let lastOutcome = '';

  function disableControls() {
    document.querySelectorAll('[data-action]').forEach((button) => { button.disabled = true; });
  }

  function clearListeners() {
    if (stopLive) stopLive();
    if (stopConnection) stopConnection();
    if (stopClockOffset) stopClockOffset();
    if (stopOperator) stopOperator();
    if (stopAck) stopAck();
    stopLive = stopConnection = stopClockOffset = stopOperator = stopAck = null;
    if (pendingTimer) clearTimeout(pendingTimer);
    pendingTimer = null;
    pendingId = null;
    busy = false;
    operator = false;
    lastOutcome = '';
    latest = null;
    lastSession = null;
    lastSequence = -1;
    databaseConnected = false;
    showingFresh = false;
    setConnection(false);
  }

  function updateFreshness() {
    if (!auth.currentUser || !latest) return;
    const age = Date.now() + serverOffset - latest.updatedAt;
    if (!databaseConnected || !Number.isFinite(age) || age > 30000 || age < -30000) {
      setConnection(false);
      showingFresh = false;
      status.textContent = databaseConnected ? 'Unity からの更新が止まっています。' : 'Firebase へ再接続しています。';
      return;
    }
    if (age > 15000) {
      disableControls();
      const badge = $('connection');
      badge.classList.remove('online');
      badge.classList.add('offline');
      badge.querySelector('span').textContent = '更新停止';
      showingFresh = false;
      status.textContent = '最後の更新から 15 秒以上経過しました。';
      return;
    }
    if (!showingFresh) render(latest, { canControl: operator && !busy });
    showingFresh = true;
    status.textContent = operator ? 'Unity の状態を受信中です。進行操作を実行できます。' :
      'Unity の状態を受信中です。このアカウントは閲覧専用です。';
    $('control-note').textContent = lastOutcome || (busy ? 'Unity の応答を待っています…' :
      operator ? '現在のゲーム状態に応じて遠隔操作できます。' : '操作するには担当者 UID の登録が必要です。');
  }

  document.querySelectorAll('[data-action]').forEach((button) => {
    button.addEventListener('click', async () => {
      if (!operator || busy || !auth.currentUser || !latest || !databaseConnected) return;
      const expected = { start: 'Title', result: 'Game', retry: 'Result', title: 'Result' }[button.dataset.action];
      const age = Date.now() + serverOffset - latest.updatedAt;
      if (latest.state !== expected || age < -30000 || age > 15000) return;

      busy = true;
      lastOutcome = '';
      const commandId = crypto.randomUUID().replaceAll('-', '');
      pendingId = commandId;
      showingFresh = false;
      updateFreshness();
      try {
        await sdk.database.set(sdk.database.ref(db, 'control/request'), {
          id: commandId,
          action: button.dataset.action,
          targetSession: latest.sessionId,
          targetSeq: latest.seq,
          expectedState: latest.state,
          createdAt: sdk.database.serverTimestamp()
        });
        if (pendingId !== commandId) return;
        pendingTimer = setTimeout(() => {
          busy = false;
          pendingId = null;
          pendingTimer = null;
          lastOutcome = 'Unity からの応答を確認できませんでした。状態を確認してから再操作してください。';
          showingFresh = false;
          updateFreshness();
        }, 30000);
      } catch {
        busy = false;
        pendingId = null;
        lastOutcome = '命令を送信できませんでした。状態が更新された可能性があります。再確認してください。';
        showingFresh = false;
        updateFreshness();
      }
    });
  });

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const button = form.querySelector('button');
    button.disabled = true;
    status.textContent = 'サインインしています…';
    try {
      await sdk.auth.signInWithEmailAndPassword(auth, $('remote-email').value.trim(), $('remote-password').value);
      $('remote-password').value = '';
    } catch {
      status.textContent = 'サインインできませんでした。閲覧用アカウントを確認してください。';
    } finally {
      button.disabled = false;
    }
  });

  $('remote-signout').addEventListener('click', () => sdk.auth.signOut(auth));

  sdk.auth.onAuthStateChanged(auth, (user) => {
    clearListeners();
    form.hidden = !!user;
    account.hidden = !user;
    $('remote-user').textContent = user?.email || '';
    if (!user) {
      status.textContent = '閲覧用アカウントでサインインしてください。';
      return;
    }
    status.textContent = 'Firebase の状態を待っています。';
    stopConnection = sdk.database.onValue(sdk.database.ref(db, '.info/connected'), (snapshot) => {
      databaseConnected = snapshot.val() === true;
      updateFreshness();
    });
    stopClockOffset = sdk.database.onValue(sdk.database.ref(db, '.info/serverTimeOffset'), (snapshot) => {
      serverOffset = Number(snapshot.val()) || 0;
      updateFreshness();
    });
    stopLive = sdk.database.onValue(sdk.database.ref(db, 'live'), (snapshot) => {
      const value = snapshot.val();
      if (!value) {
        setConnection(false);
        status.textContent = 'まだ Unity から状態が送信されていません。';
        return;
      }
      if (value.schemaVersion !== 1 || typeof value.sessionId !== 'string' ||
          !Number.isInteger(value.seq) || !Number.isFinite(value.updatedAt) ||
          !Array.isArray(value.players) || !value.serial ||
          (value.serial.lines != null && !Array.isArray(value.serial.lines))) {
        setConnection(false);
        status.textContent = '受信データの形式が合いません。管理者に確認してください。';
        return;
      }
      if (lastSession === value.sessionId && value.seq < lastSequence) return;
      lastSession = value.sessionId;
      lastSequence = value.seq;
      value.serial.lines ||= [];
      latest = value;
      showingFresh = false;
      updateFreshness();
    }, () => {
      setConnection(false);
      status.textContent = 'データの閲覧権限がありません。管理者に UID の登録を依頼してください。';
    });
    stopOperator = sdk.database.onValue(sdk.database.ref(db, `roles/operators/${user.uid}`), (snapshot) => {
      operator = snapshot.val() === true;
      if (stopAck) { stopAck(); stopAck = null; }
      if (operator) {
        stopAck = sdk.database.onValue(sdk.database.ref(db, 'control/ack'), (ackSnapshot) => {
          const ack = ackSnapshot.val();
          if (!ack || ack.id !== pendingId) return;
          if (pendingTimer) clearTimeout(pendingTimer);
          pendingTimer = null;
          pendingId = null;
          busy = false;
          lastOutcome = ack.result === 'applied' ? '操作を Unity が実行しました。' :
            'Unity が操作を受け付けませんでした。現在の状態を確認してください。';
          showingFresh = false;
          updateFreshness();
        });
      } else {
        disableControls();
      }
      showingFresh = false;
      updateFreshness();
    }, () => {
      operator = false;
      disableControls();
      $('control-note').textContent = '操作権限を確認できませんでした。';
    });
  });

  setInterval(updateFreshness, 1000);
}
