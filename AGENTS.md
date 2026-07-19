# AGENTS.md

このファイルは、Codex がこのリポジトリで作業する際のガイダンスを提供します。

## プロジェクト概要

Lattice Deformation Tool は Unity 2022.3 以降向けのエディタ拡張で、VRChat アバターやワールドのメッシュに対して非破壊的なラティス変形を提供します。NDMF (Non-Destructive Modular Framework) と連携し、ビルド時にのみ変形を適用します。

## プロジェクト構造

```
├── Editor/              # Unity エディタ拡張コード
│   ├── Localization/    # 多言語対応（日本語/英語/韓国語/中国語）
│   ├── WeightTransfer/  # ボーンウェイト再計算モジュール
│   │   └── BurstSolver/ # Burst 対応の疎行列/線形ソルバ
│   └── VRChat/          # VRChat 固有の機能
├── Tests/Editor/        # EditMode テスト（レイヤースタック挙動など）
│   └── Fixtures/HistoricalReleases/ # 公開14リリースで実保存した移行fixture
├── Runtime/             # ランタイムコンポーネント（MonoBehaviour, ScriptableObject）
├── Tools~/HistoricalFixtures/ # 隔離Unityプロジェクトで履歴fixtureを再生成するツール
└── package.json         # VPM パッケージ定義
```

### 統合 EditorTool アーキテクチャ

Scene ビュー上の変形ツールは、単一の `MeshDeformerTool`（`EditorTool`）から 3 つのハンドラに委譲する構成:

- `MeshDeformerTool.cs`: 唯一の `[EditorTool]`。アクティブレイヤーの種類とサブモードに応じて適切なハンドラを起動
- `BrushToolHandler` (`BrushLayerTool.cs`): ブラシ変形ハンドラ
- `VertexSelectionHandler` (`VertexSelectionTool.cs`): 頂点選択・変換ハンドラ
- `LatticeToolHandler` (`LatticeLayerTool.cs`): ラティス制御点ハンドラ
- `MeshDeformerToolOverlay` (`MeshDeformerTool.cs`): 統合 Overlay UI（レイヤー選択、サブモード切替、各ハンドラの `DrawOverlayGUI` を呼び出し）

ハンドラは `Activate(LatticeDeformer)` / `Deactivate()` / `OnToolGUI(EditorWindow, LatticeDeformer)` / `DrawOverlayGUI(LatticeDeformer)` のインターフェースを持つ。

### ブラシ変形ツール（Brush Deformer）

頂点単位のブラシベース変形ツール。ラティスツールと並行して使用可能。

**Runtime:**
- `BrushDeformer.cs`: 頂点ごとの変位ベクトル (`Vector3[]`) を保持し、Burst Jobs で適用

**Editor:**
- `BrushDeformerEditor.cs`: Inspector UI（メッシュソース、変形データ管理、リビルドオプション）
- `BrushLayerTool.cs` (`BrushToolHandler`): ブラシ編集ハンドラ（`MeshDeformerTool` から委譲）
  - **ブラシモード**: Normal（法線方向）、Move（スクリーン方向）、Smooth（ラプラシアン平滑化）、Mask（頂点マスク）
  - **設定**: 半径、強度、減衰タイプ（Smooth/Linear/Constant/Sphere/Gaussian）
  - **表面距離（Surface Distance）**: ユークリッド距離の代わりに測地線（表面）距離を使用するフォールオフモード。Dijkstra アルゴリズムでメッシュ隣接グラフ上の最短経路を計算し、重なった面への影響の漏れを防止
  - **ミラー編集**: X/Y/Z 軸対称。Normal/Smooth/Mask に加えて Move ブラシもミラー側へ反転移動量を適用
  - **操作**: Alt+スクロールで半径、Shift+スクロールで強度調整
- `GeodesicDistanceCalculator.cs`: 測地線距離計算（Dijkstra ベースの表面距離フォールオフ用）
- SkinnedMeshRenderer の Move ブラシは任意で、ポーズ上の renderer-local 移動量を頂点ごとの blended skinning matrix で逆変換し、rest-space 変位として保存できる。不正 weight・bind pose 不足・特異行列は従来の local-space 変位へ安全に fallback する
- 旧 `BrushDeformer` コンポーネント向けの NDMF Preview/Bake は現在登録されていない。Inspector の明示移行から、変位と再構築設定を専用 `DeformerGroup` / Brush レイヤーへコピーできる。移行後も旧コンポーネントは削除せず、無効化したバックアップとして保持する
- 旧 `BrushDeformer` の複数選択移行は1つの原子的な操作として扱い、1件でも失敗したら全対象を Undo で戻し、移行前のsource meshからruntime previewを再構築する。既存 `LatticeDeformer` とのsource不一致は、初期化を伴うpublic group APIへ触れる前にfail-fastで拒否する

**頂点マスク（Vertex Mask）:**
- `LatticeLayer` に `_vertexMask` (`float[]`) を保持。各頂点の編集可能度を 0.0（保護）〜 1.0（編集可能）で管理
- Mask ブラシモードで塗布（デフォルトは保護を塗る、Invert で保護を消す）
- Brush Overlay のモード選択から Mask モードを直接選択可能。Clear Mask でアクティブレイヤーのマスクを初期化
- Normal/Move/Smooth ブラシモードでは、マスク値に応じて変形量が自動的にスケーリングされる
- `TryApplyBrushLayerContribution` でもマスクが適用され、ビルド時の出力にも反映
- ミラー編集にも対応
- Scene ビューで保護された頂点を赤、編集可能な頂点を緑で可視化

**貫通検出（Penetration Detection）:**
- `ClearanceQuery.cs` (`Editor/MeshDeformer/Utilities/`): 参照メッシュのworld-space三角形からBVHを構築し、最近傍triangle index・最近傍点・barycentric coordinate・補間法線・距離・符号付きclearanceを返す共通Query基盤
  - `ReferenceNormal` は開いたメッシュでも補間法線基準の片側clearanceを返す。`ClosedMesh` は閉じたtopologyだけray parityで内外判定し、開いたメッシュでは結果の `SignMode` を `ReferenceNormal` として明示的にfallbackする
  - `ClearanceQueryCache` はMeshRendererのshared mesh、SkinnedMeshRendererの現在poseをBakeしたmesh、world Transformをhashし、形状またはTransformが変わった場合だけBVHを再構築する。対象側もMeshRenderer / SkinnedMeshRendererをworld-spaceへ統一してQueryできる
  - BVHのAABB枝刈りにより、通常Queryで対象頂点ごとの全triangle走査を行わない
- `PenetrationDetector.cs` (`Editor/MeshDeformer/Utilities/`): `ClearanceQuery` の符号付き結果を利用して、変形後の頂点が参照メッシュを貫通しているか検出する互換Facade
  - ブラシツールの Visualization セクション「Show Penetration」トグルで有効化
  - 参照メッシュ（Renderer）を ObjectField で指定。SkinnedMeshRenderer / MeshRenderer に対応
  - 貫通頂点は赤色のドットで Scene ビューにハイライト表示
  - 表示結果は変形状態・参照メッシュ・関連 Transform をキーとしてキャッシュする
  - 初回検出は全頂点の総当たりで、参照 SkinnedMeshRenderer の現在ポーズをベイクして扱わない制約は残る

**クリアランスヒートマップ:**
- `ClearanceHeatmap.cs` (`Editor/MeshDeformer/Utilities/`): `ClearanceQuery` 結果を貫通・警告・目標未満・安全へ分類し、最小clearance、最大貫通深度、違反頂点数、評価頂点数を集計する。しきい値はworld-space meterで保持し、Inspectorではmm表示する
- `LatticeDeformer` ごとに参照Renderer、Query mode、表示mode、警告/目標距離、表示stride、更新間隔をserializeする。ヒートマップは検出専用でMesh・Layer・BlendShapeを変更しない
- Scene View描画は「貫通のみ」「警告範囲を含む」「全体分布」を切り替え、NDMF preview proxyが存在する場合はproxy meshを評価してInspectorへ評価対象を明示する。参照/対象の無効化、Undo/Redo、設定変更時は古い表示を破棄する

**複数ConditionクリアランスScan:**
- `ClearanceScanSet.cs` (`Runtime/MeshDeformer/`): 明示的なCondition順を保持する再利用可能asset。AnimationClip/sample time/relative animation root、対象・参照BlendShape、relative Transform pose override、Condition固有の警告/目標距離を保存する
- `ClearanceScanRunner.cs` (`Editor/MeshDeformer/Utilities/`): 1 Editor updateにつき1 Conditionを評価し、進捗・Cancelを提供する。各Conditionの統計・頂点clearance・NDMF proxy利用有無と、頂点ごとのworst Conditionを決定的に集計する。評価meshは頂点buffer+topologyのidentity hashをscan開始時から維持し、NDMF proxyへ対象Renderer/Bone poseとBlendShape weightを同期する
- Scan開始時にAvatar root配下と外部Preview proxyのTransform/active state、Renderer enabled/shared mesh、SkinnedMeshRendererの全BlendShape weight、Animator設定をsnapshotし、完了・Cancel・Condition例外時に復元する。Condition間でUndoを伴う利用者編集を検出した場合はscanを中止してその編集を保持する。無効Conditionは個別errorとして記録し次へ進む。結果Conditionは明示操作でSceneへ再適用でき、Restoreでscan前状態へ戻す
**Fit Correction:**
- `FitCorrectionGenerator.cs` (`Editor/MeshDeformer/Utilities/`): クリアランス評価から不足量を参照面のworld-space法線方向へ補正し、元Meshや既存Layerを変更せず専用Brushレイヤーとして追加する
- 対象範囲は貫通のみ・警告距離以下・目標距離未満から選択し、最大移動量もworld-spaceで制限する。生成後は改善数と未解決数を再評価して表示する
- 生成レイヤーには参照Renderer、Query mode、対象範囲、警告/目標距離、最大移動量を保存する。古い評価、頂点数不一致、無効な参照、rest poseでないSkinnedMeshRendererでは生成をfail-closedにする
- 形状保護制約としてactive layerのVertex Mask、open boundary固定、connected component分離、mesh adjacencyだけを使うsurface-aware smoothing、平滑化後のclearance再投影、`SymmetryVertexMap`による明示的な対称補正を個別に切り替えられる。Mask/boundary/max moveをclearance再投影より優先し、未解決頂点は隠さず報告する
- Scene Viewでは生成前のworld-space移動をPreviewでき、生成Brushレイヤーには使用したconstraintとMask snapshotも保存する。全constraintを無効にした場合は基本Fit Correctionと同じ結果を維持する

**クリアランスQAレポート:**
- `ClearanceQaReport.cs` (`Editor/MeshDeformer/Utilities/`): 現在のHeatmapまたは複数Condition Scan結果をschema v1のJSONとMarkdownへ変換する。Scanレポートは評価時点のtarget/reference/topologyと、Clip/sample/root/BlendShape/Transform overrideを含むCondition定義を不変snapshotとして保持し、package/Unity version、UTC評価時刻、Query mode、しきい値、Condition統計・error、worst Conditionとともに出力する
- 対象Mesh互換性はvertex/triangle/submesh countと、vertex座標を含めずsubmesh topology/index bufferからSHA-256で計算したTopology hashで識別する。共有用JSON/Markdownへ頂点座標、index配列、per-vertex clearance、変形deltaを出力しない
- JSONとMarkdownは同一directory内のtemporary fileへ先に完全出力し、既存ファイルのbackupを取ってから置換する。片方の置換に失敗した場合は両方をrollbackし、不完全な既存レポートを残さない。同じschemaとTopology hashのレポートだけを比較対象とする

### 頂点選択ツール（Vertex Selection Tool）

頂点を直接選択して Move/Rotate/Scale 変換を適用するツール。ブラシレイヤーの変位データを操作する。

**Editor:**
- `VertexSelectionTool.cs` (`VertexSelectionHandler`): 頂点選択・変換ハンドラ（`MeshDeformerTool` から委譲）
  - **選択方式**: クリック選択、Shift+クリックで追加、Ctrl+クリックでトグル、矩形ドラッグ選択
  - **変換モード**: Move（移動）、Rotate（回転）、Scale（スケール）
  - **プロポーショナル編集**: 選択頂点周囲の頂点にも減衰付きで影響。Smooth/Linear/Constant 減衰
  - **操作**: W/E/R で変換モード切替、Alt+スクロールでプロポーショナル半径調整
  - Vertex Selection Move も Move ブラシと同じ rest-space 逆変換 option を共有し、MeshRenderer には影響しない

### DeformerGroup アーキテクチャ

`LatticeDeformer` は複数の `DeformerGroup` を持ち、各グループが独立したレイヤースタックと BlendShape 出力設定を管理する。

```
LatticeDeformer
├── _groups: List<DeformerGroup>    // 複数グループ
├── _activeGroupIndex: int          // アクティブグループ
└── Deform() が全グループを処理

DeformerGroup [Serializable]
├── _name, _enabled
├── _layers: List<LatticeLayer>     // グループ内レイヤー
├── _activeLayerIndex: int
├── _blendShapeOutput, _blendShapeName, _blendShapeCurve
```

**グループの動作:**
- `BlendShapeOutput == Disabled`: レイヤー合成結果を頂点に直接適用（複数グループは加算合成）
- `BlendShapeOutput == OutputAsBlendShape`: レイヤー合成結果を独自の BlendShape として出力
- 直接変形グループと BlendShape グループの混在が可能
- 1コンポーネントから複数の BlendShape を同時生成可能

**Facade API:**
- 既存API（`Layers`, `ActiveLayerIndex`, `BlendShapeOutput` 等）は `ActiveGroup` への委譲で後方互換を維持
- 新API: `Groups`, `ActiveGroupIndex`, `ActiveGroup`, `AddGroup()`, `RemoveGroup()`

### MeshDeformerProfile

- `MeshDeformerProfile` (`ScriptableObject`) は Group / Layer / Mask / Brush displacement / BlendShape出力設定を複数コンポーネント間で共有する
- `LatticeDeformer.DataSource` が `Profile` の場合、Preview/BakeはProfileから作成した非シリアライズの独立コピーを使用し、Prefabへ変形payloadを重複保存せずProfileを意図せず変更しない
- `SaveToProfile()` でインスタンスの現在データをProfileへ明示保存し、`CopyProfileToEmbedded()` でProfileから編集可能な内蔵データへ複製する
- Profile保存時はSource Mesh本体を埋め込まず、頂点・index・triangle・submesh・bindpose数、BlendShape signature、頂点/index topology hash、任意のAsset GUID/local file IDを互換性メタデータとして記録する
- Profile適用時は `ExactMatch` / `CompatibleSourceDiffers` / `TopologyMismatch` / `InsufficientMetadata` を区別し、Topology不一致はコンポーネント・Renderer・Profileを変更せず拒否する。互換性メタデータを持たない旧Profileは警告付きで適用でき、再保存時に現行メタデータを付与する

**ラティスレイヤー合成:**
- 中立制御点からのオフセットフィールドを補間し、ソース頂点へ加算する
- Bounds 外では境界へクランプされたオフセットのみを評価するため、中立状態は頂点位置にかかわらず恒等変形を保つ
- `Trilinear` は既存の8制御点キャッシュを高速経路として使い、`CubicBernstein` は頂点ごとにキャッシュした各軸の Bernstein 基底を全制御点へ tensor-product 評価する。2分割軸は一次基底になるため trilinear と一致する
- 公開 `0.0.1`〜`1.4.0` の `CubicBernstein` は実際には8点補間で評価されていたため、移行時に該当 `LatticeAsset` の `_legacyTrilinearInterpolation` を有効化して旧出力を維持する。新規assetだけが現行 Bernstein 評価を使用する

### BlendShape 出力・読み込み

グループごとの変形デルタを BlendShape フレームとして出力、または既存 BlendShape をブラシレイヤーとして読み込む機能。

**出力 (`BlendShapeOutputMode`)** — グループレベル設定:
- `Disabled`（デフォルト）: 従来通り頂点に直接変形を適用
- `OutputAsBlendShape`: グループ内レイヤーの合成変形を1つの BlendShape として出力。頂点はソース位置のまま保持
- `BlendShapeName`: 出力名（有効化時に空なら `gameObject.name` で自動補完）
- `BlendShapeCurve` (`AnimationCurve`): BlendShape の補間カーブ。常に100フレームをカーブからサンプリング
- `BlendShapeComposition` は既存互換の `Single` に加えて `Progressive` / `Crossfade` を選択できる。ProgressiveはGroup内の有効Layer差分を順に累積し、Crossfadeは隣接Layer状態だけを補間する。いずれも100フレーム上で `BlendShapeCurve` をstage進行として評価する
- Inspector UI の「BlendShape Output」独立 Foldout セクション内に配置。テストモードで SkinnedMeshRenderer 上の重みをプレビュー可能
- NDMF ビルドパイプラインは `Object.Instantiate()` で BlendShape データを保持
- レイヤー単位でも `BlendShapeOutput` / `BlendShapeName` / `BlendShapeCurve` を設定可能。レイヤー出力を有効にしたレイヤーはグループ合成から除外され、個別 BlendShape として出力される
- Progressive / Crossfadeの候補にはGroup合成へ参加するLayerだけを使い、個別BlendShape出力Layerは候補からも除外する。出力無効Groupではcomposition設定にかかわらず従来どおり直接加算する
- 生成 BlendShape には、メッシュ再計算オプションに応じて法線/タンジェントデルタも付与される
- 公開 `1.2.1`〜`1.4.0` はレイヤーの出力mode/nameを保存していたが、実際の `Deform` はレイヤーを分離せずグループ出力だけを生成し、生成shapeの法線/タンジェントdeltaも書かなかった。出力設定が有効な旧assetは `_legacyPublishedBlendShapeSemantics` を保持してこの実挙動を再現し、この互換flagを持たないassetだけが上記の現行レイヤー出力を使う

**読み込み**:
- `LatticeDeformer.ImportBlendShapeAsLayer(int blendShapeIndex)`: ソースメッシュの BlendShape をアクティブグループのブラシレイヤーとしてインポート
- `LatticeDeformer.ImportBlendShapeAllFramesAsGroup(int blendShapeIndex)`: multi-frame BlendShapeを専用Crossfadeグループへ展開し、各frameを独立したBrushレイヤーとしてインポートする。元frameの順序とweightはレイヤーの非表示metadataへ保持する
- 全frame由来の有効レイヤーが厳密昇順のweight metadataを維持している間は、生成BlendShapeを100分割へ再サンプルせず、元のframe数・weightで直接出力する。各レイヤーの変位は独立編集でき、zero-delta frameも候補として保持する
- `LatticeDeformer.GetSourceBlendShapeNames()`: 利用可能な BlendShape 名一覧を取得
- Inspector UI の「Import BlendShape」ドロップダウンで「単一フレーム」と「全フレーム」を選択できる

### レイヤー左右分割・反転（L/R Split & Flip）

VRChat アバターの対称ワークフロー向けのレイヤー操作機能。

- `SymmetryVertexMapCache`: メッシュ・軸・中心オフセット・許容距離ごとの対称頂点マップを空間ハッシュで構築してキャッシュする共通基盤
  - 中心軸上の頂点は自己対応にし、非対称頂点は用途ごとにスキップまたは自己処理を選択する
  - Brush Mirror、Vertex Selection の対称選択、Brush Layer Flip が同じマップを利用する
- `LatticeDeformer.SplitLayerByAxis(int layerIndex, int axis, bool keepPositiveSide)`: 指定軸で片側の変形データをゼロにリセット
  - ブラシレイヤー: ソースメッシュ頂点座標の正負で判定し、対象側の変位をゼロクリア
  - ラティスレイヤー: グリッド中点で分割し、対象側の制御点をデフォルト位置にリセット
- `LatticeDeformer.FlipLayerByAxis(int layerIndex, int axis)`: 指定軸で変形データを反転
  - ブラシレイヤー: ミラー頂点ペアを探索（1mm 許容）し、変位を交換＋軸成分反転
  - ラティスレイヤー: 制御点オフセットを軸対称にスワップ＋軸成分反転
- Inspector UI の「L/R Operations」セクションに Split L / Split R / Flip X / Flip Y / Flip Z ボタンを配置
- ローカライゼーション: 5言語（en/ja/ko/zh-Hans/zh-Hant）対応済み

**レイヤーコピー＆ペースト:**
- Inspector の Duplicate ボタン横に Copy / Paste ボタンを配置
- `JsonUtility` で `LatticeLayer` をシリアライズし、静的フィールド (`s_copiedLayerJson`) に保持
- Paste 時は `JsonUtility.FromJsonOverwrite` でデシリアライズし、`LatticeDeformer.InsertLayer()` で追加
- 異なる `LatticeDeformer` インスタンス間でもコピー可能（同一エディタセッション内）

### レイヤー構造マイグレーション

3段階のマイグレーションチェーン（`_layerModelVersion` で管理）:

- **v0→v2** (`TryMigrateLegacyBaseToLayerStructure`): 旧バージョンの単一 `_settings` を `_layers` リストに移行
  - 旧 `_settings` を `Lattice Layer`（weight=1）として先頭レイヤーに取り込み
  - グリッド分割や Bounds/Interpolation は各レイヤーの独立設定
- **v2→v3** (`TryMigrateLayersToGroupStructure`): フラットな `_layers` + コンポーネントレベル BlendShape 設定を `DeformerGroup` に移行
  - 既存 `_layers` を1つの `DeformerGroup` にラップ
  - コンポーネントレベルの `_blendShapeOutput`/`_blendShapeName`/`_blendShapeCurve` をグループに移動
  - master ブランチ（v0）からも feature branch（v2）からも自動対応

**`LatticeAsset` シリアライズ:**
- シリアライズバージョンは v1。旧バージョンの全ゼロ配列だけを未初期化 sentinel として中立制御点へ移行し、v1 の意図的な全ゼロ制御点は保持する
- シリアライザーコールバック内の初期化は managed 処理のみで行い、Jobs/Burst をスケジュールしない

#### 変形データ互換性（最重要）

- 公開リリースの移行 manifest は `0.0.1 → 0.0.2 → 0.0.3 → 0.0.4 → 0.0.5 → 0.0.6 → 1.0.0 → 1.0.1 → 1.1.0 → 1.2.0 → 1.2.1 → 1.3.0 → 1.3.1 → 1.4.0 → current` の順序を維持する
- schema に変更がないリリースも明示的な no-op とし、**1公開リリースにつき1 migration step** を順番に実行する。version はそのstep全体の成功後にのみ更新する
- 各stepは原子的かつ冪等にする。future version、壊れたpayload、source/配列のcount不一致などを推測で補正せず fail-closed とし、失敗時はversionとraw変形データを一切変更しない
- fail-closed でいうraw変形データは Unity のdeserialization callback完了後のpayloadを指す。`LatticeAsset` v0の全ゼロ未初期化sentinelを中立点へ直す処理はnested callback契約としてcomponent migrationのpreflightより先に行われる
- 既存の段階移行テストは過去契約として変更しない。各公開リリースで実際に保存したfixtureと期待snapshotは独立したテストへ追加する
- direct upgrade とmanifest順のstepwise upgradeが同じ結果になること、および inactive Prefab を保存・再読み込みしてもデータ・version・active選択が保持されることをリリースゲートにする
- `1.2.1`〜`1.4.0` で実在した `_groups` + stale `_layers` + `_layerModelVersion=2` のhybrid payloadは `_groups` を出力上の正本とする。旧flat layerは削除・二重適用せず、`1.2.1→1.3.0` stepで無効な `Recovered Legacy Flat Layers` groupへ移して調査・手動復旧可能な形で保持する
- conceptual-v2 flat layerの非selected null holeは既存契約どおり除去してactiveを決定的にremapするが、group/current payloadのnull group・null layer・null listはfail-closedとする。公開`1.2.1`〜`1.4.0`の`RemoveLayer`が生成した「非空groupでraw active layer indexがlayer countと完全一致する」既知patternだけは、該当release step内で旧末尾選択へ原子的にcanonicalizeする。それ以外の範囲外indexは補正しない
- 公開`1.2.1`〜`1.4.0`の上記`RemoveLayer`実挙動は、各tagのRuntimeでactive末尾layerを実際に削除して保存した独立fixture kind `lattice-remove-active-last` で固定する。raw one-past-end index、旧getterが返した末尾選択、移行後canonical index、golden出力、direct/stepwise/save-reloadの一致を必須gateとする
- markerが残る `0.0.1` World-spaceデータは `_applySpace=1` とWorld座標の制御点をcurrent互換状態として保持し、変形のたびにownerの最新 `worldToLocalMatrix` で評価して旧挙動を維持する
- 公開YAMLにはexact release markerがないため、同形のsingle-settings schema (`0.0.1` Local〜`1.2.0`) は最古識別可能な `V0_0_1`、group schema (`1.2.1`〜`1.4.0`) は `V1_2_1` と分類し、そこから全boundaryを順に実行する。exact tag provenanceはfixture manifestだけが保持する
- 既知制約: `0.0.1` の World-space marker (`_applySpace`) は `0.0.2` で削除された。`0.0.2` 以降で既に保存されmarkerを失ったassetはLocal/Worldを自動判別できないため、推測移行せずバックアップからの復元または明示的な手動判断を必要とする
- `Tests/Editor/Fixtures/HistoricalReleases/` は上記14タグそれぞれのtag時点のRuntimeをUnity 2022.3.22f1で実行し、inactive/disabled Prefab、source mesh、旧 `Deform(false)` の期待snapshot、tagのpeeled SHA・package version・Unity/generator/runner hashを含むmanifestを保存する。生成物の`.meta` GUIDは`sha256-v1:tag/relative-asset-path`、Prefab local fileIDは`sha256-v1:tag/relative-prefab/class/ordinal`で決定的に正規化し、同一入力の再生成は全corpusがbyte-identicalでなければならない。このcorpusと `HistoricalReleaseFixtureTests.cs` の全検証を移行変更の必須release gateとする

## 開発ガイドライン

### コーディング規約

- **言語**: C# (.NET Standard 2.1 互換)
- **命名規則**: Unity/C# 標準に従う
  - クラス名・メソッド名: PascalCase
  - プライベートフィールド: `_camelCase` または `m_camelCase`
  - ローカル変数・パラメータ: camelCase
- **名前空間**: `net._32ba.LatticeDeformationTool` を使用

### Editor vs Runtime の分離

- `Editor/` フォルダ内のコードは Unity Editor でのみ動作
- `Runtime/` フォルダ内のコードはビルド後も含まれる
- Editor 専用 API (`UnityEditor` 名前空間) は Editor フォルダ内でのみ使用可能

### NDMF 連携

- プレビュー機能は `IRenderFilter` を実装
- プレビューはソース BlendShape の Weight だけが変化した場合にプロキシ Mesh を in-place 更新し、終了時は upstream から受け取ったプロキシ Mesh を Renderer に復元する
- ビルドパイプラインは NDMF プラグインとして登録
- 変形処理は非破壊的に行い、元メッシュは変更しない
- ボーンウェイト再計算はビルド時に自動実行（オプション）

### ボーンウェイト再計算（Weight Transfer）

SIGGRAPH Asia 2023 論文 "Robust Skin Weights Transfer via Weight Inpainting" に着想を得た実装。論文の full mixed energy をそのまま解くものではなく、cotangent harmonic approximation を使用する：
- **Stage 1**: 変形後の頂点位置から元メッシュ上の最近傍点を探索し、距離・法線閾値でウェイトを転写
- **Stage 2**: 転写できなかった頂点にラプラシアンベースの補間（Inpainting）を適用
- 既知頂点として採用する confidence は finite かつ `>= 0.5`、BoneWeight は finite・非負・有効なbone indexかつ正規化可能な値に限定する。無効・未解決の頂点は安全なsource weightまたはbone 0へfallbackし、NaN/Infinityを出力しない
- NDMF/public境界では非finiteの設定・頂点・法線やbind pose欠落を事前検証し、solver/jobへ渡さず明示的な失敗結果を返す。退化triangleは補間経路を作らず、同値weightはbone index順で決定的に選ぶ
- 設定は `LatticeDeformer` の Inspector UI で調整可能

**パフォーマンス最適化:**
- `MeshSpatialQuery.cs`: Burst Jobs (`IJobParallelFor`) による並列空間クエリ
- `WeightInpainting.cs`: Burst 実装の疎行列 (CSR) + BiCGStab 反復法ソルバー。反復中のscalar scratchを再利用し、NativeArrayは確保途中の例外を含め必ず解放する
- O(1) ルックアップ用の Dictionary インデックスマップ
- 処理時間: ~48秒 → ~400ms (100倍以上高速化)

### ローカライゼーション

- UI テキストは `Editor/Localization/LatticeLocalization.cs` で管理
- 日本語と英語、韓国語、中国語(簡体字/繁体字) に対応
- 新しいテキスト追加時は必ず全言語での翻訳を追加すること

### パフォーマンス考慮事項

- `Unity.Mathematics` と `Unity.Burst` を活用した高速な数値計算
- `Unity.Collections` の NativeArray を使用したメモリ効率の良い処理
- エディタ操作中はプレビューの更新頻度に注意

## ビルドとテスト

このプロジェクトは Unity パッケージとして提供されます。テストするには：

1. Unity 2022.3 以降で VCC プロジェクトを開く
2. パッケージを `Packages/` フォルダに配置または VPM 経由でインストール
3. NDMF 1.9.0 以降が必要

### EditMode テスト

- `Tests/Editor/MeshDeformerLayerStackTests.cs` にレイヤースタックの回帰テストを追加済み
  - `AddLayer_CreatesNeutralLayerWithoutChangingActiveLayerSettings`
  - `LayerWeight_OffsetsVerticesFromNeutralDelta`
  - `BrushLayer_AppliesWeightedVertexDisplacement`
  - `LatticeAndBrushLayers_AreComposed`
  - `BlendShapeOutput_ProducesCorrectDeltaFrames`
  - `BlendShapeOutput_DisabledMode_AppliesDirectly`
  - `LayerBlendShapeOutput_ProducesIndependentShapeAndExcludesFromGroupOutput`
  - `GeneratedBlendShape_RecalculateNormals_WritesNormalDeltas`
  - `ImportBlendShapeAsLayer_CreatesMatchingBrushLayer`
  - `GetSourceBlendShapeNames_ReturnsCorrectNames`
- UnityMCP で対象アセンブリのみ実行する例:
  - `unity-mcp raw run_tests '{"mode":"EditMode","assemblyNames":["net.32ba.lattice-deformation-tool.tests.editor"],"includeDetails":true}'`
- カバレッジは `pwsh -File Tools~/Run-Coverage.ps1 -ProjectPath <UnityProject> -EnforceLineCoverage` で実行する。Unity Test Framework 1.4.x の複数 assembly 指定はセミコロン区切り、package の PDB path filter は `**/Packages/net.32ba.lattice-deformation-tool/...` の相対 glob を使用する
- `#line hidden` によるカバレッジ除外は、Native allocation枯渇やUnity内部例外、事前検証により構造上到達不能な二重防御に限定し、直前に理由をコメントする。再現可能な境界・失敗状態は除外せずテストで固定する

### 公開リリースfixtureの再生成

- リポジトリrootで `pwsh -File Tools~/HistoricalFixtures/Generate-HistoricalFixtures.ps1` を実行する。既定では公開14タグをpeeled commitから検証し、各tagのRuntimeだけを一時Unityプロジェクトへ展開してcorpusを再生成する
- 生成には Unity 2022.3.22f1 を使用する。別配置の場合は `-UnityPath <Unity.exe>` を指定する。生成処理はリポジトリの現行Runtimeを履歴コードとして混ぜず、tag/package version不一致、Unity失敗、manifest/meta欠落をエラーにする
- runnerは全14タグをstagingへ生成・検証してからcorpusをatomic swapし、失敗時は既存corpusへrollbackする。fixtureは非既定のactive group/layer選択、Cubic Bernstein補間、公開`RemoveLayer`のone-past-end payloadも含み、保存・移行後の選択状態とgolden出力を検証する
- generator/runnerまたはfixture schemaを変更した場合、代表tagを同じ入力で独立に2回生成して全ファイルのbyte-identicalを確認してから全14タグを正式再生成する。manifestに記録した決定的GUID/fileID schemeとgenerator/runner SHAがrepo実体に一致することも検証する
- generatorまたは期待schemaを変更した場合は14タグすべてを再生成し、`HistoricalReleaseFixtureTests.cs` と全EditModeテストを通す。fixture、`.meta`、manifestの一部だけを手編集・再生成してはならない

## 依存関係

- `nadena.dev.ndmf` >= 1.9.0 (VPM)
- `com.unity.mathematics` 1.2.6
- `com.unity.burst` 1.8.12
- `com.unity.collections` 1.2.4

## Codex へのルール

### タスク終了時の AGENTS.md 更新

タスク完了時、以下の変更があった場合は **必ず AGENTS.md を更新** してください：

- 新しいディレクトリやモジュールの追加
- 依存関係の変更（package.json の更新）
- 重要な設計パターンや規約の導入
- ビルド・テスト手順の変更
- その他、今後の開発で知っておくべき情報

更新時は既存のフォーマットに従い、簡潔かつ正確に記述してください。
