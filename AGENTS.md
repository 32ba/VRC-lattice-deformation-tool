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
│   └── Fixtures/
│       ├── HistoricalReleases/ # 公開14リリースで実保存した移行fixture
│       └── LaterReleases/ # 後続公開28リリースの83ケース（独立corpus）
├── Runtime/             # ランタイムコンポーネント（MonoBehaviour, ScriptableObject）
├── Tools~/HistoricalFixtures/ # 隔離Unityプロジェクトで履歴fixtureを再生成するツール
├── Docs~/Architecture/  # 全体リファクタリングの監査、設計案、基準ファイル記録
└── package.json         # VPM パッケージ定義
```

### 2.0.0ベータのリファクタリング

- `Docs~/Architecture/2026-09-07-audit.md` と `2026-09-07-refactoring-plan.md` に従い、`2.0.0-beta.1` として実装中。達成範囲と検証記録は `2.0.0-beta.1-progress.md` を参照し、計画全体を実装済みと扱わない。
- 実装基準は公開済み `1.4.6-beta.1` に作業中のGuided UI・翻訳を統合した `c7f499c38e16f386fe6734e0f7937d50c502c529`。元の `1.4.5-rc.5` 作業ツリーと起動中のPlaygroundのpackage参照は保持し、`codex/refactor-2.0.0-beta` の隔離worktreeで作業する。
- `Runtime/MeshDeformer/SerializedDeformerReader.cs` は初期化・移行・配列補正・Profile展開を行わず、壊れた保存内容もそのまま読むinternal API。返す参照は同期処理中だけ使う借用viewであり、非同期評価用の不変snapshotではない。ValidatorとInspectorのGroup/Layerコピーが利用する。
- `Editor/MeshDeformer/Authoring/DeformerEditService.cs` はGroup/Layer追加・削除・並べ替え・複製・貼付を、1件のUndo、失敗時rollback、cache無効化、Prefab override記録へまとめる。Profile参照中の直接編集は拒否する。再評価とUI更新は呼出し側が担当し、raw readerから実行しない。
- public `LatticeDeformer.InsertGroup` / `MoveGroup` を追加し、Inspectorのprivate field reflectionを除去した。既存APIの互換入口、保存フィールド、schema version、既存GUID、履歴fixtureと期待値は維持する。各ツールへの展開、評価・移行・Previewの分離は後続作業。
- `DeformerAuthoringBoundaryTests` はraw読取り、Profile不変、失敗時rollback、Undo/Redo、Inspector callback、Prefab Apply/save-reloadを検証する。今回の隔離Unity batch検証は描画確認を含まないため、GraphicsE2Eと実際のScene View操作は別途実施する。
- `DeformerStore` はUnityのserialized propertyを変更するadapter。Layer削除後のnumeric selectionとclampは旧Inspectorの契約を保持し、public `RemoveLayer` が持つ選択規則と混同しない。Inspectorの構造操作は `PerformEditOperation` → service → store/API →再評価と表示更新へ統一した。
- `DeformerEditService.ExecuteBatch` は対象集合を先に固定・検証し、全対象を1つのUndoへ記録する。途中の拒否・例外は全対象をrollbackしてcacheを破棄する。Profileやfuture component/layer-model/lattice versionはUndoを作る前に拒否し、実際の複数選択UIとdragへの接続は後続工程で扱う。
- `Tests/Editor/Fixtures/ArchitectureBaseline/` は固定commit `c7f499c` の実行結果。`ArchitectureContractSnapshot.Export` と `LayerOperationBaselineFixture.Export` を隔離した基準版Unityで実行して生成し、新実装から期待値を上書きしない。公開API・保存field/path・enum・GUIDの維持と、6種類の旧Inspector操作を独立の互換性テストで照合する。`Docs~/Architecture/2026-09-07-p0-p1-contracts.md` に再実行条件と検証根拠を記録する。

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
- 旧 `BrushDeformer` はデシリアライズ互換性だけのために残し、空の `AddComponentMenu` で新規追加を禁止する。NDMF Preview/Bakeは登録しない。Editorのdelay callbackでロード済みScene、Prefab Stage、`Assets/`内のimport済みPrefabを検出し、変位と再構築設定を専用 `DeformerGroup` / Brushレイヤーへ自動移行する。移行後も旧コンポーネントは削除せず、無効化したバックアップとして保持する
- 旧 `BrushDeformer` の複数選択移行は1つの原子的な操作として扱い、1件でも失敗したら全対象を Undo で戻し、移行前のsource meshからruntime previewを再構築する。既存 `LatticeDeformer` とのsource不一致は、初期化を伴うpublic group APIへ触れる前にfail-fastで拒否する
- 自動移行はserialization callback、Play Mode、compile/import中、batch mode、read-only/package assetでは実行しない。Scene/Prefab単位で全対象を原子的に処理し、失敗時は元データを保ったまま警告する。Prefab assetは`LoadPrefabContents`で隔離して検証し、全件成功後だけ保存する。再検出時はmarker・source・payloadを純粋に照合して重複Group/Layerを作らない

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
- `ClearanceScanRunner.cs` (`Editor/MeshDeformer/Utilities/`): 1 Editor updateにつき1 Conditionを評価し、進捗・Cancelを提供する。各Conditionの統計・頂点clearance・NDMF proxy利用有無と、頂点ごとのworst Conditionを決定的に集計する。評価meshはscan開始時のoriginal mesh identityと位置非依存のtopology hashを維持し（NDMF proxyはtopology一致を必須とする）、proxyへ対象Renderer/Bone poseとBlendShape weightを同期する
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

**共通Validation:**
- `MeshDeformerValidator.cs` (`Editor/MeshDeformer/Validation/`): Inspector、NDMF Preview、Bake前で共有する診断API。`MDVxxx` のstable code、severity、対象Object/group/layer/property、任意の明示Fixを返す
- Renderer/source mesh、保存後のtopology drift、Brush/Mask配列長、Lattice設定、Group/Layer構造、BlendShape名、Profile互換性、Clearance参照、rest-space変換、Preview/Bake対象差を検査する。無効component/group/layerは致命Errorにしない
- BakeはErrorが1件でもあればMesh生成・置換前に停止する。Warningはstable codeと継続時の意味をEditor logへ出し、Bake自体は継続する
- Fixはsilentに実行せずInspectorのボタンから対象`LatticeDeformer` 1件だけをUndo可能に変更する。診断側から通常のgroup/layer getterを呼んで配列を暗黙補正しない

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
- 後続公開28件（`1.4.1`〜`1.4.6-beta.1`、公開RC/betaを含む）は `Tests/Editor/Fixtures/LaterReleases/` に独立保存する。Embeddedのchannel保持/再構築56件とProfile 27件の計83ケースについて、実tag Runtimeの保存値と全Mesh channelを `LaterReleaseFixtureTests` でdirect/stepwise/save-reload照合する。旧14件の期待値を更新して代用しない
- 後続corpusの生成は `Tools~/HistoricalFixtures/LaterReleases/` を使用し、4helperのhash・公開tag SHA・決定的GUID/fileIDを検証する。別の隔離projectで全28件を再生成し、526fileすべてのbyte一致を要求する。helperは既存のLF規約を守り、参照propertyのlive `m_FileID` を保存せず、永続GUID/local fileIDまたは同じowner内のcomponent型で照合する。`1.4.1` はunversioned group schema、他27件は値15を共有しており、exact releaseはpayloadから推測しない

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

### CI

- `.github/workflows/test.yml` が pull request と master への push で EditMode テストを実行する。GameCI の `unity-test-runner` が Unity 2022.3.22f1 で `ci-project/` を組み立て、`net.32ba.lattice-deformation-tool.tests.editor` と `...tests.editor.vrchat` を走らせる
- `ci-project/` には vrc-get で VRChat SDK（`com.vrchat.avatars`）、NDMF、Avatar Optimizer（`com.anatawa12.avatar-optimizer`）を VPM から導入し、実際の VCC プロジェクトを再現する。バージョンは workflow 内で固定。VRChat SDK がないと NDMF がプラグインを VRChat アバター専用とみなして bake pass を Incompatible でスキップするため、`MeshDeformerValidatorTests` が失敗する。AAO がないとユーザー環境と plugin set が乖離し、`RealPreviewPipelineEndToEndTests` も AAO 不在で Ignore になる
- Scripting Define は追加しない。`LATTICE_VRCSDK3_AVATAR` は asmdef の versionDefines 由来なのでクリーン構成のまま有効になる
- VRChat SDK の `EnvConfig` は初回起動時に API Compatibility Level と Scripting Define Symbols を書き換えてスクリプト再コンパイルを要求する。バッチモードのテスト実行中は assembly reload がロックされているためこの要求は保留のままとなり、`EditorApplication.isCompiling` が実行中ずっと true になって Prefab Mode の退出やシーン切り替えが Unity に拒否される。workflow はこれを避けるため、本番のテスト実行前に軽量なテスト1本だけの warm-up 実行を挟んで ProjectSettings を settled にしている
- `EnvConfig` は `PlayerSettings.legacyClampBlendShapeWeights` を true に強制する。Unity はこのフラグをセッション内で最初に BlendShape を bake した時点で latch し、以降の書き換えを無視するため、**テスト内で設定を変更しても `BakeMesh` の挙動は変わらない**。重み 100 超の挙動に依存するテストは、設定値を読んで期待値を切り替えること
- 実行には repository secrets `UNITY_LICENSE`（Personal の .ulf、Pro の場合は代わりに `UNITY_SERIAL`）、`UNITY_EMAIL`、`UNITY_PASSWORD` が必要
- ローカルで define 付き環境のみを確認して済ませないこと。CI と同じくクリーン構成で通ることを前提にテストを書く

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
- 対話エディタ経由のローカル実行では環境起因の偽失敗に注意する:
  - `RealPreviewPipelineEndToEndTests` は NDMF の `Camera.onPreCull` 駆動でプロキシを生成するため、エディタがバックグラウンド（非フォーカス）だとレンダリングが走らず fail する。実行中は Unity を前面に置くこと
  - ドメインリロード直後は NDMF の `NDMFSyncContext` 遅延初期化（`unityMainThreadId == -1`）が未発火のことがあり、`ComputeContext.Invalidate()` が非同期化して即時 assert するテストが落ちる。再実行または editor loop を1回回せば解消する
  - Inspector 系のカスタムエディタは破棄済みターゲットの再描画で例外を出さないよう fake-null ガード必須。batch mode の CI では GUI が存在しないためこの種のバグは検出できない
- カバレッジは `pwsh -File Tools~/Run-Coverage.ps1 -ProjectPath <UnityProject> -EnforceLineCoverage` で実行する。Unity Test Framework 1.4.x の複数 assembly 指定はセミコロン区切り、package の PDB path filter は `**/Packages/net.32ba.lattice-deformation-tool/...` の相対 glob を使用する
- `#line hidden` によるカバレッジ除外は、Native allocation枯渇やUnity内部例外、事前検証により構造上到達不能な二重防御に限定し、直前に理由をコメントする。再現可能な境界・失敗状態は除外せずテストで固定する

### 公開リリースfixtureの再生成

- リポジトリrootで `pwsh -File Tools~/HistoricalFixtures/Generate-HistoricalFixtures.ps1` を実行する。既定では公開14タグをpeeled commitから検証し、各tagのRuntimeだけを一時Unityプロジェクトへ展開してcorpusを再生成する
- 生成には Unity 2022.3.22f1 を使用する。別配置の場合は `-UnityPath <Unity.exe>` を指定する。生成処理はリポジトリの現行Runtimeを履歴コードとして混ぜず、tag/package version不一致、Unity失敗、manifest/meta欠落をエラーにする
- runnerは全14タグをstagingへ生成・検証してからcorpusをatomic swapし、失敗時は既存corpusへrollbackする。fixtureは非既定のactive group/layer選択、Cubic Bernstein補間、公開`RemoveLayer`のone-past-end payloadも含み、保存・移行後の選択状態とgolden出力を検証する
- generator/runnerまたはfixture schemaを変更した場合、代表tagを同じ入力で独立に2回生成して全ファイルのbyte-identicalを確認してから全14タグを正式再生成する。manifestに記録した決定的GUID/fileID schemeとgenerator/runner SHAがrepo実体に一致することも検証する
- generatorまたは期待schemaを変更した場合は14タグすべてを再生成し、`HistoricalReleaseFixtureTests.cs` と全EditModeテストを通す。fixture、`.meta`、manifestの一部だけを手編集・再生成してはならない

## 2.0評価の比較基準と境界

- `Runtime/Model/` は既存の `LatticeLayer` / `DeformerGroup` とenumを同一namespace・assembly・保存fieldのまま配置する。`LatticeDeformer.cs` と既存metaはコンポーネントの互換入口として維持し、ファイル移動をschema変更として扱わない
- `DeformationOutputBaselineFixture.cs` は固定commitの隔離Unityでのみ期待出力を生成する。候補実装でfixtureを更新して差を吸収しない
- `DeformationOutputCompatibilityTests.cs` は13条件について全Mesh channel、BlendShape全frame、source/upstream不変を比較する。基準と検証hashは `Docs~/Architecture/2026-09-07-output-contracts.md` を参照する
- 既存14タグcorpusは維持し、後続公開28件の83ケースは `Tests/Editor/Fixtures/LaterReleases/` に独立追加する。`Tools~/HistoricalFixtures/LaterReleases/` が各tagのRuntimeで生成し、manifestの4 helper hashと決定的GUID/fileIDを検証する。helperまたは期待schemaの変更時は28件すべてを独立に2回生成し、526fileのbyte-identicalと旧/新corpusの全テストを確認する。履歴fixtureを候補実装の出力で更新しない
- `Runtime/Evaluation/` の `DeformationEvaluator` が通常Deformと上流PreviewのGroup/Layer合成を共有する。入力は検証済みの同期借用view、managed workspaceはコンポーネント所有とし、非同期処理へ渡さない
- `GeneratedBlendShapeOutput` の候補は中間頂点bufferから独立させ、上流frameを評価しても保持済み候補を書き換えない。private互換wrapperは既存テスト用に残し、通常の内部利用を追加しない
- `LatticeEvaluator` が補間cache・managed scratch・NativeArray・Burst Jobsを所有し、`BrushEvaluator` がmaskを含むBrush加算を扱う。owner行列と旧絶対評価規則は `EvaluationSemantics` へ明示し、数値評価からコンポーネント・Renderer・Transformを参照しない。Disable/Destroy/Invalidateと確保途中の例外は同じNative解放処理を通す
- `DeformedMeshWriter` が既存/生成BlendShapeとsurface channelを書込み、`BlendShapeComposer` は所有者の `MeshOutputWorkspace` に100フレームを順次合成する。Meshへのコピー後だけbufferを再利用し、候補配列・sourceは変更しない。`DeformationPipeline` が上流Previewの評価と出力cloneを管理し、失敗時は自身のcloneを破棄する。Preview最終法線の旧再計算規則も維持する
- `SourceMeshAccess` はR/W無効Meshの全channelコピーをleaseとして所有し、`SourceVertexResolver` は取得済みweight値から元頂点とBlendShapeを評価する。通常評価の最終frame外挿と表示範囲計算の最終frame固定は `SourceBlendShapeExtrapolation` で区別する。`DeformerPlatformAdapter` がassembly load時にMeshUtility読取りと旧移行の保存記録を登録し、RuntimeからUnityEditor/NDMF assemblyを直接参照しない
- `DeformerDataResolver` はEmbeddedの同期借用viewと、ownerごとの独立したProfile評価コピーを返す。queryで保存Group・選択・Profile・dirty stateを変更しない。Profileのidentity、内容、source互換性を検証し、不正な配列・null slot・future payloadは適用前に拒否する。Profile利用中のactive GroupはProfileのGroup数で検証し、Prefab再読込みでコンポーネントの保存済み選択を保持する
- Profile互換性の再利用は `Deform` / `CreatePreviewMeshFromInput` の同期評価scope内だけに限定する。scopeを成功・早期return・例外時に閉じ、次回は同じMesh instanceの内容変更、Profileの直接編集、Undo/Redoも再検出する。Repaint/frameをまたぐ互換性の無条件cacheへ拡張しない
- `Runtime/Migration/DeformationMigrationPreflight` は旧/現行のraw保存形状を同期借用し、version・nested asset・selection・Brush/Maskの順に検査する。source頂点数とProfileのGroup数はコンポーネント境界で取得して渡し、検査からRenderer・Mesh・Profile asset・UnityEditorへ触れない。旧選択の既知例外を検査中に補正せず、正規化とversion更新は対応release stepへ残す。読み取りlistの走査はindexを使い、interface enumeratorのboxingを追加しない
- `DeformationMigrationRunner` が既存release step、構造変換、旧互換規則、rollbackを所有する。`DeformationMigrationState` は作業用scalarとcopy-on-writeのlistを保持し、owner行列・source countは値として受け取る。`TryAdvanceOneRelease` はpreflight後に1境界だけを処理し、失敗時はscalar/listに加え共有nested assetの補間flagとGroup選択も戻す。既存enumの `CurrentDevelopment=15` を再利用・再定義しない
- コンポーネントへの反映とUnityへの記録もrelease境界の成功条件に含める。記録に失敗したらraw保存fieldを復元し、Editor adapterが捕捉したPrefab override一覧も復元する。値の復元だけでPrefabの記録済みversion/Group数が戻るとは仮定しない。既存のprivate段階移行入口は互換テスト用の委譲として維持し、通常処理からはrunner内のstepを使う
- `DeformationModelCopy` がLattice設定とcurveの既存コピー規則を共有する。通常のcurrent評価では移行state/snapshotを確保せず、移行前検査だけを行う
- `DeformationReleaseManifest` は棚卸し済み42公開リリースと `2.0.0-beta.1` の順序をappend-onlyで管理する。新しい `_migrationReleaseIndex` は0が未分類、1〜43が完了済み境界であり、既存 `_layerModelVersion=3` / `CurrentDevelopment=15` の意味を変えない。1〜14は旧enumと対応し、15は1.4.1（旧enum14）、16は1.4.2-rc.1（旧enum15）、43が2.0 beta。exact tagを識別できない旧enum15は16から開始し、fixture manifestのprovenanceを保存データの推測に使わない
- `PublishedDeformationMigrationRunner` が通常の移行入口となり、既存の変換は旧runnerへ委譲する。1.4.0→1.4.1と16以降の境界は明示的no-opだが、境界ごとに記録成功後だけ進捗を確定する。負の進捗、未来の進捗、破損payloadを変更せず拒否し、Editorの共通診断は `MDV023` を5言語で返す
- 進捗番号はschema識別そのものではない。Prefab Variantが更新済みの親から進捗を継承し、自身のoverrideに古いschemaを残す場合は、純粋なpreflightを通ったraw markerから進捗を再開する。既知のstale-current構造の復旧もraw fieldとPrefab overrideを含む同じ原子的commitに入れる。未知の破損や範囲外の選択をこの処理で補正しない
- `PublishedDeformationMigrationTests` は全42境界の失敗・再試行、途中保存、Prefab Variantの継承/Apply/Revert、Undo、拒否時の不変を確認する。`ReleaseJournalFixtureTests` は旧/後続corpusのLatticeDeformer 102ケースで新しい全release入口のdirect/stepwise/save-reloadを照合する。既存の旧enum段階テストとfixture期待値は変更しない
- `Tools~/ArchitectureBaseline/` は隔離Unityでの同期評価Profiler、プロセス上限監視、比較とソース照合を提供する。較正に成功した `GC.Alloc` のsize metadataだけを割当量として扱い、frame読取りにsample名の大量文字列化を使わない。基準は `Docs~/Architecture/2026-09-07-evaluation-performance.md` を参照する
- `Editor/Preview/DeformerPreviewSession` はNDMF nodeごとの変更revision、同一Meshへの更新、Interactive通知とUndo購読を所有する。`MeshDeformerPreviewFilter` はplacement・入力検証とNDMF callbackの接続を維持し、既存のprivate node入口は互換テストから利用できる
- Preview session は `AssemblyReloadEvents.beforeAssemblyReload` でも同じ `Dispose` を実行し、通常終了時にはこの購読も解除する。NDMFの遅延終了だけに依存すると、domain終了後にHideAndDontSave Meshが残る。実reloadの検証はTest Runner内の通常Dispose検証と分け、再読み込み前のinstance IDの消滅、借用Meshの復元、source不変、実graphの再生成を確認する。
- `PreviewMeshLease` は生成Meshだけを所有し、各proxyで置き換えた上流Meshを借用する。終了時はproxyが自身の一意な出力Meshをまだ参照している場合だけ復元し、別世代・後段の割当てを上書きしない。元Renderer、上流Mesh、既存のproxy generation/token登録を変更・破棄しない。遅れて届いたproxyにも独立した復元先を保持し、終了・二重終了・破棄済みproxyを同じ処理で扱う
- NDMF Bakeは対象componentの破棄中だけ `SuppressMeshRestoration()` のscopeを使用する。scopeはownerごとに入れ子と二重Disposeを処理し、例外・native component破棄後にも閉じられる。既存public `SuppressRestoreOnDisable` の意味とAPIは互換用に維持するが、通常のBuildはglobal値を書き換えない
- `PreviewOwnershipTests` と `MeshRestorationScopeTests` は終了時復元、世代交代、外部割当て、遅延proxy、破棄順、評価拒否と再試行、二重終了後のUndo、owner限定の復元抑止を確認する。実Scene View/NDMFの検証には起動済みの正常な `Plugin-dev-playground` を使用し、元のシーン・package参照を保全して一時Sceneで比較する。`Mayo` など別projectを代用しない
- `DeformerEditSession` はBrush/Vertex/Latticeの複数frame編集で完全なUndo snapshot、対象Group/Layer/sourceのidentity確認、Prefab記録と終了を共有する。開始前にraw payloadを純粋に検査し、Layout/Repaintはidentity確認、実書込み前は元Meshのtopologyとpayloadを再検証する。Profile・破損・未来版・対象変更を補正して編集しない
- UnityのPrefab overrideのRedoには、開始時の `RegisterCompleteObjectUndo` に加えて各frameの書込み前の `RecordObject` と書込み後のPrefab記録が必要。ミラー・比例編集を含む一連の変更後に記録し、別操作を取り込むUndoの一括collapseを終了時に行わない。MouseUpはHandlesが消費する前のraw eventを保持して終了を判断する
- Escapeは自身のUndo group（またはUnityがEscape KeyDown用に作った直後の1境界）だけを取り消す。別のUndo操作が割り込んだ場合はそれを戻さず、既存の変形を独立したUndoとして残して終了する。Undo/Redo通知ではsessionを破棄して復元済みpayloadを書き戻さず、assembly reload前にも終了する
- `DeformerEditSessionTests` はframeをまたぐUndo/Redo、Prefab VariantのApply/save-reload、拒否時のpayload/dirty不変、取消と他のUndo保持を確認する。`AuthoringGestureEndToEndTests` は実Scene Viewへmouse/key eventを送り、Brush・Vertex/Latticeのnative handle・Escape・Layer切替を実NDMFの最終proxyで照合する。private編集methodを直接呼んだ検査をScene View入力の証拠と混同しない。pose/proxy snapshotの共通化とProfilerは引き続きP4/P5の対象とする
- `SkinnedPoseSnapshot` は各handlerのBakeMesh結果、local頂点、同じ取得時点のTransformを所有する。Brushのworld表示とraycastは同じcaptureを使い、VertexとLatticeも独立したownerを持つ。失敗時は古いposeを公開せず、Deactivate/cache reset/assembly reloadで所有Meshだけを破棄する。旧 `SkinnedVertexHelper` の静的capture APIは互換入口として残すが通常のツールからは使わない
- Brush/Vertexのpose参照はproxy登録revisionと破棄済みrendererを検出して再解決する。NDMFの登録revisionだけを最終proxyの生存保証としない。`ToolPoseSnapshotTests` は内部の複数owner検証と、実NDMF graphのpose・BlendShape・proxy世代交代の検証を区別する。Latticeのdrag中の固定座標とpending proxy切替は既存の規則を維持する

- `BlendShapeTestSession` はInspectorのテスト表示が借用するRuntime Meshの割当て、元の全ウェイト配列、対象RendererのMesh/weight Prefab差分を所有する。複数Inspectorでも同じRendererのsessionは1件に限定し、終了・Disable/Destroy・assembly reloadで復元する。Runtime Meshの破棄はコンポーネントに残し、外部Mesh割当てや別propertyの編集を戻さない。元Meshのshape順が変わった場合だけ名前でウェイトを対応させ、配列indexの旧Prefab差分を再利用しない。更新時は割当て所有を先に確認し、Groupの参照には保存payloadを修復しない `ReadResolvedData` を使う

- クリアランスの編集状態は `Authoring/ClearanceAuthoringSession` が所有する。Heatmap/Fitの別対象cache、Scan update購読、Condition再適用のScene snapshot、Undoでの無効化とassembly reload時の復元を同じownerで閉じる。`UI/ClearanceInspectorSection` は設定欄と明示操作、`UI/ClearanceSceneDrawer` は借用結果の描画だけを担当する。補正Layer生成はfresh評価後に `DeformerEditService` へ渡し、Profile・破損payloadは書込み前に拒否する。既存Editorのinternal評価入口は互換テスト用の委譲に限定し、通常のClearance処理から呼び戻さない

- ProfileのInspectorは `UI/ProfileInspectorSection`、互換性query・保存準備・明示操作は `Authoring/ProfileAuthoringService` が担当する。sourceの現在の参照と保存済みtopologyを検査し、保存用copyを準備してから対象ProfileだけをUndo可能に更新・保存する。`SaveAssets`で無関係なdirty assetを一括保存しない。新規作成は未使用の`Assets/`内pathだけへ行い、失敗時は自身が作成したassetだけを解放する。source切替と内蔵への複製は `DeformerEditService` の共通commitへ接続し、通常のLayer操作がProfileを編集しない制約は維持する。null inline Groupを持つ破損テストでは、UnityのJSON化がnullを実体化し得るため、検査自体で修復しないraw readerによる比較を使う

- Inspector の再計算設定は `UI/MeshRebuildInspectorSection`、診断表示は `UI/ValidationInspectorSection` と `Validation/InspectorValidationState` が担当する。表示だけで mixed selection や未知の enum 値を書き戻さず、明示入力時だけ SerializedProperty を変更する。Weight Transfer の計算と設定の保存 path は維持する。

- Guided UI は `UI/GuidedInspectorSection` が表示とツール起動を担当し、`GuidedInspectorState` は raw reader から表示値だけを取得する。Profile が欠落している Profile mode も編集開始しない。`Authoring/GuidedAuthoringService` が既存 Layer の再利用・選択・追加を判断し、変更が必要な場合だけ共通 `DeformerEditService` で Undo / Prefab / cache 更新を記録する。開始前の raw payload・source identity・topology 照合はドラッグと共通の `DeformerAuthoringSource` を通し、拒否時は配列修復、ツール起動、Preview 更新を行わない。null inline Group の検査では JSON による実体化を避け、raw list と slot を直接比較する。
- サポート情報は `Editor/Support/` の収集・形式 codec・NDMF 調査 adapter・ファイル出力・menu に分離する。既存 `MeshDeformerSupportReport` は形式 v1 の互換 facade として残す。収集は component の保存データを初期化せず、codec は component/Editor/NDMF を参照しない。PNG と decode JSON は同じ原子的ファイル置換を使い、途中失敗で既存ファイルを壊さない。`SupportCodecV1` fixture は変更前の実装 blob `4fcb1f3962534ceb208e68af92fe8a3814861cfa` から取得したもので、候補 codec の出力で期待値を作り直さない。

- `DeformerStackInspectorSection` が Group/Layer の入れ子一覧、行の binding、context menu と選択表示を所有する。詳細設定・BlendShape・import は Inspector の別の描画 callback として接続する。表示再構築は `SetSelectionWithoutNotify` を使用し、不正な保存選択を補正しない。Profile asset が欠落した Profile mode も編集禁止を維持する。IMGUIの設定欄はpanel座標の表示範囲で判別し、一覧のdrag待機を開始しない。native popupがMouseUpを消費し、Unity draggerがeventのtargetをListViewへ変えるため、event型だけで判別しない。
- `BlendShapeInspectorSection` がGroup/Layerの出力設定、読み込みmenu、`BlendShapeTestSession` の表示を所有する。`BlendShapeImportMenu` は開いた時点のowner・source/topology・Group/Layer選択と参照・shape名/frame構成を保持し、選択時に再検査する。Profile・破損・source drift・非finite deltaはUndo開始前に拒否する。payloadは `Runtime/Model/BlendShapeLayerImport` で検証済み保存sourceから独立生成し、`DeformerEditService` で追加する。inactive Prefabは実行用source cacheがなくても保存参照から読み込める。public import APIも同じfactoryへ委譲し、既存の署名・frame metadata・出力規則を維持する。
- `DeformerStackStructure` は Group/Layer の identity・型・選択だけを独立配列へ保存し、同じ件数の切替・入替え・Undo/Redoも再検出する。保持した object は identity 比較だけに使い、過去の mutable payload を表示値として読まない。通常の照合は Brush/Mask 全頂点の検査を繰り返さず、構造再構築と変更時に検証する。
- 行・メニュー・選択の callback は一覧の世代と storage identity を確認してから適用する。行の名前・enabled・weight は標準の SerializedProperty binding を保ち、その handler より先の TrickleDown で古い入力を拒否する。Unity のテキスト/slider Undo grouping、Prefab override 表示と property menu を独自実装で置換しない。選択は `DeformerStackSelection` から共通 `DeformerEditService` へ接続する。構造操作・行変更・内部 Clipboard copy は source/topology の純粋な検査を通し、拒否した選択表示は保存値へ戻す。破棄時は行の callback と binding を解除する。
- Animated ListView は drag 開始時に一時的な空選択を通知する。その通知を保存へ書かず、選択・並べ替えの反映と一覧再構築は pointer-up の dispatch 後にまとめる。入力中の外部 storage 変更と PointerCancel は保留操作を破棄し、保存済みの表示へ戻す。入れ子 ListView の pointer capture 中の終了は、実際に capture した一覧で受け取る。
- `DeformerStackInspectorTests` は read-only 再構築、同件数の切替、古い行入力、Editor panel 上の property event、Undo/Redo、Prefab Variant の Apply/save-reloadを検証する。property event の検証は panel への attach 後に Editor update を待ち、標準 binding の存在も確認する。未接続の field へ値を入れただけで拒否成功と判定しない。基準 Layer 操作の期待 snapshot は変更せず、fixture helper は旧 Inspector と新 section の Clipboard 保存場所を reflection で識別する。

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
