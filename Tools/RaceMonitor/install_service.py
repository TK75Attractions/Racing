#!/usr/bin/env python3
"""Explicitly install/uninstall the monitor as a macOS login agent."""
import argparse
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys

from monitor import Settings

LABEL = "com.racing.monitor"


def service_plist(python: str, app: Path, logs: Path) -> dict:
    return {
        "Label": LABEL,
        "ProgramArguments": [python, "-u", str(app / "monitor.py"), "--config", str(app / "monitor.local.json")],
        "WorkingDirectory": str(app), "RunAtLoad": True, "KeepAlive": True, "ThrottleInterval": 30,
        "StandardOutPath": str(logs / "monitor.log"), "StandardErrorPath": str(logs / "monitor-error.log"),
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("monitor.local.json"))
    parser.add_argument("--uninstall", action="store_true")
    args = parser.parse_args(argv)
    if sys.platform != "darwin":
        print("この常駐設定はmacOS用です。", file=sys.stderr)
        return 2
    library = Path.home() / "Library"
    app = library / "Application Support" / "RacingMonitor"
    logs = library / "Logs" / "RacingMonitor"
    plist = library / "LaunchAgents" / (LABEL + ".plist")
    scope = "gui/" + str(os.getuid())
    try:
        if plist.exists():
            with plist.open("rb") as stream:
                previous = plistlib.load(stream)
            if str(app / "monitor.py") not in previous.get("ProgramArguments", []):
                raise ValueError("別の常駐設定があります。")
        if not args.uninstall:
            config = args.config.resolve()
            Settings.load(config)  # Validate before stopping an existing monitor.
            # Install a stable copy so cleanup of a Codex worktree cannot remove the service.
            app.mkdir(parents=True, exist_ok=True, mode=0o700)
            logs.mkdir(parents=True, exist_ok=True, mode=0o700)
            plist.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["/bin/launchctl", "bootout", scope + "/" + LABEL], capture_output=True, check=False)
        if args.uninstall:
            plist.unlink(missing_ok=True)
            print("監視の常駐を解除しました。設定・状態・ログはLibrary内に残しています。")
            return 0
        shutil.copyfile(Path(__file__).with_name("monitor.py"), app / "monitor.py")
        if config != app / "monitor.local.json":
            # Credentials belong in the protected configuration, never in launchd's plist.
            destination = app / "monitor.local.json"
            descriptor = os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
            os.fchmod(descriptor, 0o600)
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(config.read_bytes())
            destination.chmod(0o600)
        document = service_plist(sys.executable, app, logs)
        with plist.open("wb") as stream:
            plistlib.dump(document, stream)
        plist.chmod(0o600)
        result = subprocess.run(["/bin/launchctl", "bootstrap", scope, str(plist)], capture_output=True, check=False)
        if result.returncode:
            print("常駐起動に失敗しました。LaunchAgentとPythonの実行権限を確認してください。", file=sys.stderr)
            return 1
        print("ログイン時に運用監視を起動します。")
        print("設定・一時停止ファイル: " + str(app))
        print("ログ: " + str(logs))
        return 0
    except (OSError, ValueError, TypeError, plistlib.InvalidFileException):
        print("常駐設定を確認できません。通知設定・保存先・既存のLaunchAgentを確認してください。", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
