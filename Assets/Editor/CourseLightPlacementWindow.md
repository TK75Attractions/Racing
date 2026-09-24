# コースオブジェクト配置ツール

## 使い方

1. `Window > Racing > コースオブジェクト配置` または `Racing > Placement > Open Course Placement Tool` を開きます。
2. 配置カタログからPrefabを選択します。標準カタログにはネオン行燈、矢印ガードレール、矢印案内看板、黄黒の進入禁止ポールが入っています。
3. 必要ならY回転、面合わせ、配置先を指定し、`配置モードを開始` を押します。
4. Sceneビューで地面やColliderの上をクリックして配置します。終了はEscキー、またはウィンドウの終了ボタンです。選択中のPrefabですぐ配置を始める場合は `Racing > Placement > Start Placement Mode` を使えます。

Colliderがある場所ではその表面に置きます。何もない場所では設定した高さの水平面に置きます。配置はUndo/Redoに対応します。

## Prefabの追加

Projectウィンドウで `Assets/Settings/CourseLightCatalog.asset` を選び、InspectorのEntriesに要素を追加して表示名とProject内のPrefabを設定します。PrefabのルートPivotを地面に置く位置に設定してください。光源、標識、その他のコースオブジェクトを同じ手順で登録できます。追加した項目はウィンドウの一覧に現れます。

標準Prefabをカタログに追加するには `Racing > Placement > Add Standard Course Prefabs to Catalog` を実行します。既存の項目を保ち、登録されていない標準Prefabだけを追加します。

Prefab Modeでは配置できません。Sceneを開いてから使用してください。
