# CPU・P2自動運転のペース調整（2026-10-22）

機能本体 `e4a7ff0`。Unity 6000.3.9f1、SampleScene、本番車両、CPU2台。従来の高速能力を維持して人間のペース・進捗差に追従する。詳細は [仕様](../../Documentation/AdaptiveAiPace.md)。

[Coreテスト](core-test-results.xml): 131件全通過。低速でも攻撃／抜き返しの時間を作ること、先行時の減速、高速相手／大きな遅れでの最高速維持、P2追従、停止・被弾時の観測保持、時刻刻み・不正値を検査。

## 試走方法

人間の代役は車体・コース形状・前方車を観察し、0.18／0.22秒間隔の判断、物理ハンドル360度/秒、ペダル3/秒、操舵ノイズを含む入力モデル。目標18m/sの2人、16／30m/sの2人、50／48m/sの2人、18m/sのP1＋実際のP2自動運転を走らせた。物理計測は各約110秒の観測窓であり、2周完走の計測ではない。人間の車の位置・速度・チェックポイント・結果を強制しない。CPUの既存の復帰機能は通常どおり使用する。

ON／OFFで同じ運転方針・seedを使用するが、交通に反応して入力が変わるため同一軌跡や決定的な比較にはならない。初期25秒を除き、両車が走行中の進捗差を計測。CPUは割り当てた人間との距離、P2自動運転を含むケースは全AIについてP1との距離を使う。平均速度・最高速度は観測窓全体。道路上の対面距離ではなく周回を含む進捗距離。

## P1＋P2自動運転

| 車両 | 従来OFF: P1との平均絶対距離 | 調整ON: 同距離 | ONで40m以内だった割合 |
| --- | ---: | ---: | ---: |
| P2自動運転 | 1309.7m | 19.2m | 100% |
| CPU 1 | 634.1m | 12.6m | 100% |
| CPU 2 | 1288.5m | 20.1m | 94.3% |

ON時の平均速度はP1 58.0／P2 56.2／CPU 1 57.3／CPU 2 59.8 km/h。P2の実際の `usedAutodrive=true` を確認。CPU 2はコース外2.2秒・既存の自動復帰1回があり、それも含む値。人間2台とCPU 1は逸脱・復帰なし。

[ONの結果](On/adaptive-auto-7204/summary.json) / [ONのtrace](On/adaptive-auto-7204/trace.csv) / [OFFの結果](Off/adaptive-auto-7204/summary.json)。実行中の例外は両方0。

## 人間2人

- 低速2人: 平均P1 63.7／P2 62.8／CPU 1 62.6／CPU 2 62.1 km/h。CPUは初期25秒後の全サンプルで相手の40m以内。CPU 2は前後関係の入れ替わりも記録。従来OFFも2人の交通でCPUが追従し、40m以内100%だったため、このケース単体では従来より改善したとは判断しない。
- 速度差のある2人: P1 51.9／P2 100.8に対し、CPU 1 48.3／CPU 2 97.5 km/h。別々の相手に応じたペースを確認。
- 高速2人: CPU 2は最高176.4 km/hを記録。CPU 1は142.4 km/h。全体を固定低速にしていないことを確認。相手の完走による割り当て変更時には、距離指標が不連続に変わる。

これら3ケースの全車でコース逸脱・復帰なし、実行中の例外0。

[ONの集計](On/metrics.json) / [OFFの集計](Off/metrics.json) / [ONの3ケース実行レポート](On/report.json)。各ケースの `summary.json` と `trace.csv` を対応するフォルダに保存。空車で開始してしまった最初の自動運転試験は無効として除外し、開始手順と参加車両の検査を直して別プロセスで取り直した。

## 再実行

```sh
bash Tools/Validate-Rgp.sh
RACING_BALANCE_SKILL_SUITE=1 RACING_ADAPTIVE_PACE_SUITE=1 \
RACING_BALANCE_RUN=adaptive-slow-7201,adaptive-split-7202,adaptive-fast-7203 \
RACING_BALANCE_WINDOW_SECONDS=110 RACING_BALANCE_TIME_SCALE=8 \
RACING_BALANCE_OUTPUT=Logs/AdaptiveAiPace/On \
Unity -batchmode -projectPath "$PWD" -executeMethod SteeringComfortValidation.RunCourseBatch \
  -logFile /tmp/adaptive-on.log
```

P2自動運転は `RACING_BALANCE_RUN=adaptive-auto-7204` で別途実行。従来比較には `RACING_ADAPTIVE_PACE=off` を追加し、出力先を変更する。`Tools/AnalyzeAdaptiveAiPace.py <出力フォルダ> --output <metrics.json>` で距離・速度を再集計できる。新機能のF8切り替えがON／OFF比較に使われる。本番の日別ランキングや外部公開へ試験記録を登録しない。
