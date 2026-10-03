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
  let latest = null;
  let serverOffset = 0;
  let databaseConnected = false;
  let lastSession = null;
  let lastSequence = -1;
  let showingFresh = false;

  function clearListeners() {
    if (stopLive) stopLive();
    if (stopConnection) stopConnection();
    if (stopClockOffset) stopClockOffset();
    stopLive = stopConnection = stopClockOffset = null;
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
      const badge = $('connection');
      badge.classList.remove('online');
      badge.classList.add('offline');
      badge.querySelector('span').textContent = '更新停止';
      showingFresh = false;
      status.textContent = '最後の更新から 15 秒以上経過しました。';
      return;
    }
    if (!showingFresh) render(latest);
    showingFresh = true;
    status.textContent = 'Unity からの状態を受信しています。映像とゲーム操作はローカル画面専用です。';
  }

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
  });

  setInterval(updateFreshness, 1000);
}
