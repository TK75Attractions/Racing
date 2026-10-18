#!/usr/bin/env python3
"""Independent, read-only Unity health monitor. No dashboard/browser required."""
from __future__ import annotations

import argparse
import http.client
import json
import math
import os
from pathlib import Path
import signal
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime


@dataclass
class Settings:
    webhook_url: str = field(default="", repr=False)
    webhook_format: str = "discord"
    name: str = "Racing"
    status_url: str = "http://127.0.0.1:8765/api/status"
    poll_seconds: float = 5
    request_timeout_seconds: float = 3
    hold_seconds: float = 30
    recovery_seconds: float = 30
    reminder_seconds: float = 1800
    stale_seconds: float = 15
    error_window_seconds: float = 60
    error_threshold: int = 5
    preparation_seconds: float = 180
    arm_after_first_snapshot: bool = True
    state_file: str = "monitor.state.json"
    pause_file: str = "monitor.paused"

    @classmethod
    def load(cls, path: Path, dry_run: bool = False) -> Settings:
        values = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(values, dict) or values.keys() - cls.__dataclass_fields__.keys():
            raise ValueError("設定項目を確認してください。")
        settings = cls(**values)
        for name in ("poll_seconds", "request_timeout_seconds", "hold_seconds", "recovery_seconds",
                     "reminder_seconds", "stale_seconds", "error_window_seconds", "preparation_seconds"):
            value = getattr(settings, name)
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
                raise ValueError(f"{name} は正の秒数にしてください。")
        if type(settings.error_threshold) is not int or settings.error_threshold < 1:
            raise ValueError("error_threshold は1以上の整数にしてください。")
        if type(settings.arm_after_first_snapshot) is not bool:
            raise ValueError("arm_after_first_snapshot は true / false にしてください。")
        if settings.webhook_format not in ("discord", "slack", "json"):
            raise ValueError("webhook_format は discord / slack / json です。")
        if not isinstance(settings.name, str) or not settings.name.strip() or len(settings.name) > 80:
            raise ValueError("name は1〜80文字にしてください。")
        if not isinstance(settings.status_url, str) or not isinstance(settings.webhook_url, str):
            raise ValueError("URLを文字列で指定してください。")
        source = urllib.parse.urlsplit(settings.status_url)
        _ = source.port
        if source.scheme != "http" or source.hostname not in ("127.0.0.1", "localhost", "::1") or source.username or source.password:
            raise ValueError("status_url はゲームPCのループバックHTTPにしてください。")
        if settings.webhook_url:
            target = urllib.parse.urlsplit(settings.webhook_url)
            _ = target.port
            if target.scheme != "https" or not target.hostname or target.username or target.password or target.fragment:
                raise ValueError("Webhook URL は認証情報・フラグメントを含まないHTTPS URLにしてください。")
        elif not dry_run:
            raise ValueError("通知先の webhook_url を設定してください（試運転は --dry-run）。")
        for name in ("state_file", "pause_file"):
            value = getattr(settings, name)
            if not isinstance(value, str) or not value:
                raise ValueError(f"{name} を指定してください。")
            setattr(settings, name, str((path.parent / value).resolve()))
        return settings


@dataclass(frozen=True)
class Issue:
    key: str
    message: str
    action: str
    hold: float | None = None


@dataclass
class Incident:
    issue: Issue
    first_seen: float
    last_sent: float | None = None
    clear_since: float | None = None


class Detector:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.counter_session = None
        self.errors = None
        self.error_samples: list[tuple[float, int]] = []
        self.event_cursor = 0
        self.event_samples: list[float] = []

    def inspect(self, snapshot: dict, now: float) -> list[Issue]:
        if not isinstance(snapshot, dict) or not isinstance(snapshot.get("serial"), dict) or not isinstance(snapshot.get("hardware"), dict):
            raise ValueError("監視に必要な状態データがありません。")
        timestamp = snapshot.get("serverTime")
        if type(timestamp) not in (int, float) or not math.isfinite(timestamp) or abs(now - timestamp / 1000) > self.settings.stale_seconds:
            raise ValueError("Unityの状態更新が停止しています。")
        serial, hardware = snapshot["serial"], snapshot["hardware"]
        entries = serial.get("lines") or []
        imus = hardware.get("imus") or []
        if (not isinstance(entries, list) or any(not isinstance(entry, dict) or type(entry.get("id")) is not int for entry in entries)
                or not isinstance(imus, list) or any(not isinstance(imu, dict) for imu in imus)
                or type(serial.get("portOpen")) is not bool
                or any(type(hardware.get(name)) is not bool for name in (
                    "sessionOpen", "recent", "angleLocked", "calibrated", "inputFresh", "invalidTelemetry", "telemetryEnabled"))):
            raise ValueError("機器状態の形式を確認できません。")
        issues: list[Issue] = []
        def add(key, message, action, hold=None):
            issues.append(Issue(key, message, action, hold))

        session = (snapshot.get("sessionId"), hardware.get("generation"))
        count = serial.get("errors", 0)
        if type(count) is not int or count < 0:
            raise ValueError("解析エラー数を確認できません。")
        new_session = self.counter_session != session
        if new_session or self.errors is None or count < self.errors:
            self.error_samples.clear()
        elif count > self.errors:
            self.error_samples.append((now, count - self.errors))
        self.counter_session, self.errors = session, count
        self.error_samples = [(at, amount) for at, amount in self.error_samples if now - at < self.settings.error_window_seconds]
        if new_session:
            self.event_samples.clear()
            self.event_cursor = max((entry.get("id", 0) for entry in entries), default=0)
        else:
            for entry in entries:
                if entry.get("id", 0) > self.event_cursor and entry.get("status") in ("ERROR", "FATAL", "INCOMPATIBLE", "ACK_FAILED", "TIMEOUT", "WARN"):
                    self.event_samples.append(now)
            self.event_cursor = max(self.event_cursor, max((entry.get("id", 0) for entry in entries), default=0))
        self.event_samples = [at for at in self.event_samples if now - at < self.settings.error_window_seconds]
        # Keyboard/debug sessions do not require physical devices.
        mode = str(snapshot.get("inputMode", "")).lower()
        if "keyboard" in mode:
            self.error_samples.clear()
            self.event_samples.clear()
            return issues
        if "serial" not in mode:
            add("input.unavailable", "入力モードを確認できません。", "Unityの入力設定とInputManagerを確認してください。")
            return issues
        recent_errors = sum(amount for _, amount in self.error_samples)
        if recent_errors >= self.settings.error_threshold:
            add("serial.parse", f"直近{self.settings.error_window_seconds:g}秒で解析エラーが{recent_errors}件発生しています。",
                "USBケーブル・通信設定・ESP32の送信内容を確認してください。")
        if len(self.event_samples) >= self.settings.error_threshold:
            add("serial.errors", f"直近{self.settings.error_window_seconds:g}秒で装置・通信のエラーや警告が{len(self.event_samples)}件発生しています。",
                "機器の状態と通信履歴を確認してください。操作失敗・応答なしの通知も対象です。")
        if not serial.get("portOpen"):
            add("serial.port", "ESP32のシリアルポートに接続できません。", "ESP32の電源・USBケーブル・ポート設定を確認してください。")
            return issues
        if not hardware.get("sessionOpen") or not hardware.get("recent"):
            add("serial.response", "ESP32の新しい応答を確認できません。", "ESP32の電源・USB接続・ファームウェアの動作を確認してください。")
            return issues
        state = hardware.get("state")
        if state == "FATAL":
            add("device.fatal", "ESP32が継続できない異常を通知しています。", "装置のエラー履歴と配線を確認し、原因を解消してから再起動してください。")
        elif state != "READY":
            add("device.preparing", "ESP32の初期化・校正・復旧が完了していません。", "センサー接続と校正状態を確認してください。", self.settings.preparation_seconds)
        if hardware.get("ads") == "Fault":
            add("sensor.ads", "ペダル用センサーに異常があります。", "ADS1115の電源とI2C配線を確認してください。")
        faults = [imu for imu in imus if imu.get("status") == "Fault"]
        for index, imu in enumerate(faults):
            address = imu.get("address")
            address = address if address in ("0x68", "0x69") else str(index + 1)
            add("sensor.imu." + address, f"姿勢センサー {address} に異常があります。", "該当するハンドルのIMUの電源とI2C配線を確認してください。")
        if hardware.get("imu") == "Fault" and not faults:
            add("sensor.imu", "姿勢センサーに異常があります。", "両ハンドルのIMUの電源とI2C配線を確認してください。")
        if state == "READY":
            if hardware.get("ads") != "Ok" or hardware.get("imu") != "Ok" or len(imus) < 2 or any(imu.get("status") != "Ok" for imu in imus):
                add("sensor.unconfirmed", "センサー全体の正常動作を確認できません。", "機器状態を照会し、ADS1115と両方のIMUを確認してください。")
            if not hardware.get("angleLocked") or not hardware.get("calibrated"):
                add("device.reference", "ハンドルの角度基準またはセンサー校正が未完了です。", "ハンドルを中央にして、基準調整・校正を確認してください。")
            if hardware.get("invalidTelemetry"):
                add("input.invalid", "ペダル・ハンドルの入力データが不正です。", "ケーブルと装置を確認し、受信データの値・形式を確認してください。")
            elif not hardware.get("telemetryEnabled"):
                add("input.paused", "ペダル・ハンドル入力の配信が停止しています。", "意図した停止でなければ、入力データの配信を開始してください。")
            elif not hardware.get("inputFresh"):
                add("input.stale", "正常なペダル・ハンドル入力が途絶えています。", "装置の準備状態と入力データの配信を確認してください。")
        return issues


class Monitor:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.detector = Detector(settings)
        self.armed = not settings.arm_after_first_snapshot
        self.incidents: dict[str, Incident] = {}
        self.load_state()

    def observe(self, snapshot: dict | None, now: float) -> None:
        try:
            if snapshot is None:
                raise ValueError("Unityに接続できません。")
            issues = self.detector.inspect(snapshot, now)
            self.armed = True
            complete = True
        except (ValueError, TypeError, KeyError, AttributeError):
            issues = [Issue("unity.unavailable", "Unityの接続または状態更新を確認できません。", "ゲームPCでUnityが動作しているか、停止・フリーズしていないか確認してください。")]
            complete = False
        if not self.armed:
            return
        active = {issue.key for issue in issues}
        for issue in issues:
            if issue.key not in self.incidents:
                self.incidents[issue.key] = Incident(issue, now)
            else:
                self.incidents[issue.key].issue = issue
                self.incidents[issue.key].clear_since = None
        for key, incident in list(self.incidents.items()):
            # A lost upstream is not evidence that hardware recovered.
            if not complete and key != "unity.unavailable":
                incident.clear_since = None
                continue
            if key in active:
                continue
            if incident.clear_since is None:
                incident.clear_since = now
            if incident.last_sent is None:
                del self.incidents[key]

    def pending(self, now: float) -> list[tuple[str, str]]:
        result = []
        unavailable = self.incidents.get("unity.unavailable")
        for key, incident in self.incidents.items():
            if unavailable and unavailable.clear_since is None and key != "unity.unavailable":
                continue
            if incident.clear_since is not None:
                if now - incident.clear_since >= self.settings.recovery_seconds:
                    result.append((key, "recovered"))
            elif now - incident.first_seen >= (incident.issue.hold or self.settings.hold_seconds):
                if incident.last_sent is None or now - incident.last_sent >= self.settings.reminder_seconds:
                    result.append((key, "ongoing" if incident.last_sent is not None else "attention"))
        return result

    def delivered(self, events: list[tuple[str, str]], now: float) -> None:
        for key, kind in events:
            if kind == "recovered":
                self.incidents.pop(key, None)
            else:
                self.incidents[key].last_sent = now

    def save_state(self) -> None:
        path = Path(self.settings.state_file)
        path.parent.mkdir(parents=True, exist_ok=True)
        value = {"version": 1, "armed": self.armed, "incidents": {
            key: {"message": item.issue.message, "action": item.issue.action, "hold": item.issue.hold,
                  "first_seen": item.first_seen, "last_sent": item.last_sent, "clear_since": item.clear_since}
            for key, item in self.incidents.items()}}
        # The webhook secret and raw device payload never enter this file.
        descriptor, temporary = tempfile.mkstemp(dir=path.parent, prefix=".monitor-", suffix=".tmp")
        try:
            with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
                json.dump(value, stream, ensure_ascii=False)
            os.replace(temporary, path)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)

    def load_state(self) -> None:
        try:
            value = json.loads(Path(self.settings.state_file).read_text(encoding="utf-8"))
            if value.get("version") != 1:
                return
            restored = {}
            for key, item in value["incidents"].items():
                if any(item[name] is not None and (type(item[name]) not in (int, float) or not math.isfinite(item[name]))
                       for name in ("first_seen", "last_sent", "clear_since")):
                    raise ValueError("Invalid incident time")
                restored[key] = Incident(Issue(key, item["message"], item["action"], item["hold"]), item["first_seen"], item["last_sent"], None)
            self.incidents = restored
            self.armed = value["armed"] is True or self.armed
        except FileNotFoundError:
            pass
        except (OSError, ValueError, KeyError, TypeError, AttributeError):
            print("監視状態を復元できません。新しい状態で監視を開始します。", file=sys.stderr)


def payload(settings: Settings, monitor: Monitor, events: list[tuple[str, str]], now: float) -> dict:
    labels = {"attention": "⚠ 要確認", "ongoing": "⚠ 異常継続", "recovered": "✓ 検出条件が解消"}
    lines = [f"[{settings.name}] 運用監視", datetime.fromtimestamp(now, timezone.utc).astimezone().isoformat(timespec="seconds")]
    # Summaries are generated locally; arbitrary ESP32 text and private port paths are excluded.
    for key, kind in events:
        incident = monitor.incidents[key]
        lines += [f"{labels[kind]}", f"対象（検出時）: {incident.issue.message}" if kind == "recovered" else incident.issue.message]
        if kind != "recovered":
            lines += [f"確認事項: {incident.issue.action}", f"検知から {max(0, int(now - incident.first_seen))} 秒"]
    text = "\n".join(lines)
    if len(text) > 1900:
        text = text[:1840] + "\n…ほかの異常も検出しています。ゲームPCで状態を確認してください。"
    if settings.webhook_format == "discord":
        return {"content": text, "allowed_mentions": {"parse": []}}
    if settings.webhook_format == "slack":
        return {"text": text}
    return {"name": settings.name, "timestamp": now, "message": text,
            "events": [{"key": key, "status": kind, "message": monitor.incidents[key].issue.message,
                        "action": monitor.incidents[key].issue.action} for key, kind in events]}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Transport:
    def __init__(self, settings: Settings):
        self.settings = settings
        # The source is loopback; never route it through an HTTP proxy.
        self.reader = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        self.writer = urllib.request.build_opener(NoRedirect())
        self.retry_at = 0.0
        self.failures = 0
        self.disabled = False

    def snapshot(self) -> dict | None:
        try:
            request = urllib.request.Request(self.settings.status_url, headers={"Accept": "application/json"})
            with self.reader.open(request, timeout=self.settings.request_timeout_seconds) as response:
                data = response.read(2_000_001)
                if len(data) > 2_000_000:
                    return None
                return json.loads(data)
        except (urllib.error.URLError, OSError, ValueError, http.client.HTTPException):
            return None

    def send(self, body: dict, now: float) -> bool:
        if self.disabled or now < self.retry_at:
            return False
        url = self.settings.webhook_url
        if self.settings.webhook_format == "discord":
            parts = urllib.parse.urlsplit(url)
            query = dict(urllib.parse.parse_qsl(parts.query))
            query["wait"] = "true"
            url = urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(query)))
        request = urllib.request.Request(url, data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
                                         headers={"Content-Type": "application/json", "User-Agent": "RacingMonitor/1.0"}, method="POST")
        try:
            with self.writer.open(request, timeout=self.settings.request_timeout_seconds) as response:
                if not 200 <= response.status < 300:
                    raise OSError("Webhook rejected")
            self.failures = 0
            return True
        except urllib.error.HTTPError as error:
            if error.code in (400, 401, 403, 404, 410):
                self.disabled = True
                print(f"Webhook設定を確認してください（HTTP {error.code}）。設定修正後に監視を再起動してください。", file=sys.stderr)
                return False
            retry = 0.0
            if error.code == 429:
                try:
                    retry = float(error.headers.get("Retry-After", "0"))
                except (ValueError, TypeError):
                    try:
                        retry = max(0, parsedate_to_datetime(error.headers["Retry-After"]).timestamp() - now)
                    except (ValueError, TypeError, KeyError, OverflowError):
                        pass
                try:
                    content = json.loads(error.read(4096))
                    retry = max(retry, float(content.get("retry_after", 0)))
                except (ValueError, TypeError, AttributeError, OSError):
                    pass
            self.failed(now, retry)
        except (urllib.error.URLError, OSError, ValueError, http.client.HTTPException):
            self.failed(now)
        return False

    def failed(self, now: float, retry: float = 0) -> None:
        self.failures = min(self.failures + 1, 10)
        delay = max(min(5 * 2 ** (self.failures - 1), 300), retry if math.isfinite(retry) else 0)
        self.retry_at = now + delay
        # Never print exceptions or response bodies: they can contain the webhook token.
        print(f"Webhook送信に失敗しました。{delay:g}秒後以降に最新の状態で再試行します。", file=sys.stderr)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("monitor.local.json"))
    parser.add_argument("--dry-run", action="store_true", help="通知を送らず、検知結果を表示する（状態ファイルは変更しない）")
    parser.add_argument("--once", action="store_true", help="状態を1回だけ確認する")
    args = parser.parse_args(argv)
    try:
        settings = Settings.load(args.config.resolve(), args.dry_run)
    except (OSError, ValueError, TypeError):
        print("監視設定を読み込めません。monitor.example.jsonをコピーし、設定内容を確認してください。", file=sys.stderr)
        return 2
    if args.dry_run:
        return run(settings, args)
    try:
        with InstanceLock(Path(settings.state_file + ".lock")):
            return run(settings, args)
    except OSError:
        print("同じ状態ファイルを使う監視が実行中か、保存先に書き込めません。", file=sys.stderr)
        return 2


class InstanceLock:
    """One sender per state file; the OS releases the lock even after a crash."""
    def __init__(self, path: Path):
        self.path = path
        self.stream = None

    def __enter__(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.stream = self.path.open("a+b")
        try:
            if os.name == "nt":
                import msvcrt
                if self.stream.tell() == 0:
                    self.stream.write(b"0")
                    self.stream.flush()
                self.stream.seek(0)
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            self.stream.close()
            raise
        return self

    def __exit__(self, *_):
        self.stream.close()


def run(settings: Settings, args) -> int:
    monitor, transport = Monitor(settings), Transport(settings)
    stop = threading.Event()
    for event in (signal.SIGINT, signal.SIGTERM):
        signal.signal(event, lambda *_: stop.set())
    print("運用監視を開始しました。" + ("試運転のためWebhook送信は行いません。" if args.dry_run else "初回の正常な状態取得後に通知を有効化します。" if settings.arm_after_first_snapshot else "通知を有効化しています。"), flush=True)
    paused = False
    while not stop.is_set():
        if Path(settings.pause_file).exists():
            if not paused:
                print("メンテナンス中：監視と通知を一時停止しています。", flush=True)
                monitor.incidents.clear()
                monitor.armed = not settings.arm_after_first_snapshot
            paused = True
        else:
            if paused:
                print("メンテナンスを終了して監視を再開します。", flush=True)
                monitor.detector = Detector(settings)
            paused = False
            now = time.time()
            monitor.observe(transport.snapshot(), now)
            events = monitor.pending(now)
            if args.dry_run:
                print(json.dumps({"armed": monitor.armed, "issues": list(monitor.incidents), "notification": payload(settings, monitor, events, now) if events else None}, ensure_ascii=False), flush=True)
                if events:
                    monitor.delivered(events, now)
            elif events and transport.send(payload(settings, monitor, events, now), now):
                monitor.delivered(events, now)
                print(f"通知しました（{len(events)}件）。", flush=True)
        if not args.dry_run:
            try:
                monitor.save_state()
            except OSError:
                print("監視状態を保存できません。保存先の権限・空き容量を確認してください。", file=sys.stderr)
        if args.once:
            break
        stop.wait(settings.poll_seconds)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
