# Race Monitor

ブラウザー不要の運用エラー監視・Webhook通知。Python 3.10以降、標準ライブラリのみで動作します。

Unity Editor起動時に自動で待機し、Play中は1秒ごとに状態を確認して、エラーが5秒継続すると通知します。Webhook URLの設定が必要です。

設定・検知条件・起動・Macの常駐設定は[運用マニュアル](../../Documentation/RaceMonitor.md)を参照してください。Webhook URLを含む `monitor.local.json` はGitに追加しないでください。
