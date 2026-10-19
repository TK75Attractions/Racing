# CabinetMacMount / MacBook Pro 13インチ2020

2026-10-19更新。全高2150mmと公式Mac寸法を反映した構造案です。

- PDF: `output/pdf/macbook-recessed-mount.pdf`（A3横・3ページ）
- 再生成: `build_mount_proposal.py`（Python + reportlab）
- 寸法: `mount-parameters.json`

## 寸法

筐体全高2150mmより1モデル単位=10mm。筐体本体幅1206mm、椅子を含む全長2652.8mmはモデルからの比例換算です。
窓の開口は360×240mm、上端は天端から500mm（床1650mm）、下端は床1410mm。
表板480×480×厚12mm。内空400×450×170mm。

MacBook Pro 13インチ2020は幅304.1×奥行212.4×閉じた高さ15.6mm。Intel 2ポート・4ポート版、M1版で共通です。
開き130°、画面は鉛直から観客側へ20°、本体は水平から下へ60°。
公式仕様が示さないヒンジ軸、ベゼル、開いた部分の厚さ配分と固定部は表示モデル・現物調整の寸法です。

全高以外の筐体寸法は現物の個別実測ではありません。支柱と金物の接合は実物に合わせて決定してください。

## Apple公式資料

- https://support.apple.com/ja-jp/111339
- https://support.apple.com/ja-jp/111981
- https://support.apple.com/ja-jp/111893

旧版は `/Users/kenichirowada/Documents/Codex/Recovered/CabinetMacMount` に保存したままです。

観戦窓の塗装：左ピンク・右ブルーの枠幅20mm。その外側に黒い余白35mm。開口寸法・取付位置は上記のままです。
