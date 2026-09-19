# マルチプレイ検証

## 自動検証

Unity Editorでプロジェクトを閉じてから、PowerShellで次を実行します。

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Validate-Multiplayer.ps1
```

Unityの場所が標準と異なる場合は、`-UnityEditorPath`で指定します。

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Validate-Multiplayer.ps1 `
  -UnityEditorPath "D:\Unity\Editor\Unity.exe"
```

スクリプトは次の順序で検証します。

1. 全スクリプトのコンパイル
2. EditModeテスト
   - 共有シリアルの4列入力とプレイヤー別の`nan`判定
   - 旧2列入力と拡張4列入力
   - 異常なシリアル行の拒否
   - 1位・2位の到着順
   - 1位後40秒の待機
   - タイムアウト後のDNF
   - 重複完走通知の無視
3. PlayModeスモークテスト
   - P1/P2の車両生成
   - 個別入力源の割り当て
   - 3秒カウントダウン中の走行禁止
   - P2カメラ／UIのDisplay 1割り当て
   - 両画面のタイトル、カウントダウン、残り時間、結果の一致
   - 1位車両の停止と衝突判定無効化

ログとNUnit形式の結果は `Logs/MultiplayerValidation` に出力されます。

## 実機検証

自動検証では物理的なUSB切断、ESP32の起動時間、GPUから各モニターまでの出力は再現できません。リリース候補では次を確認します。

| 項目 | 合格条件 |
|---|---|
| USB検出 | 共有マイコンを接続したポートで4列入力を検出する |
| 再接続 | レース開始前の抜き差し後、入力が復帰する |
| 入力独立性 | P1の操作がP2車両へ、P2の操作がP1車両へ影響しない |
| モニター割り当て | Display 0はP1、Display 1はP2を常時追従する |
| 共通画面 | タイトルとリザルトの文言・順位・タイムが両画面で一致する |
| カウントダウン | 3、2、1の間は両車両が動かず、GO以降のみ走行できる |
| 1位完走 | 1位車両が停止し、2位車両が接触せず通過できる |
| 2位完走 | 40秒以内の完走タイムが2位として表示される |
| タイムアウト | 1位から40秒後に未完走側がDNFとなる |
| 長時間運転 | 連続10レースでシリアルスレッド、カメラ、車両が重複しない |

実機試験では、`InputManager > Is Debug Mode`を無効にし、1台のマイコンが `<P1ペダル>,<P1ハンドル>,<P2ペダル>,<P2ハンドル>` を改行区切りで送る状態で実行します。

P1の実機入力だけを使ってデバッグする場合は、`InputManager > Is P1 Serial P2 Keyboard Debug Mode` を有効にします。P1はシリアルの先頭2列、P2は矢印キー（アクセル／ブレーキ／ハンドル）、Right Ctrl（リセット）、Right Shift（Ready）で操作します。
