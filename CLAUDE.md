# CLAUDE.md

このファイルは、Claude Code がこのリポジトリで作業する際のガイダンスを提供します。

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
├── Runtime/             # ランタイムコンポーネント（MonoBehaviour, ScriptableObject）
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
  - **ミラー編集**: X/Y/Z 軸対称
  - **操作**: Alt+スクロールで半径、Shift+スクロールで強度調整
- `GeodesicDistanceCalculator.cs`: 測地線距離計算（Dijkstra ベースの表面距離フォールオフ用）
- `BrushDeformerPreviewFilter.cs`: NDMF プレビュー（IRenderFilter 実装）
- `LatticeDeformerNDMFPlugin.cs` に `BrushDeformerBakePass` を追加済み

**頂点マスク（Vertex Mask）:**
- `LatticeLayer` に `_vertexMask` (`float[]`) を保持。各頂点の編集可能度を 0.0（保護）〜 1.0（編集可能）で管理
- Mask ブラシモードで塗布（デフォルトは保護を塗る、Invert で保護を消す）
- Normal/Move/Smooth ブラシモードでは、マスク値に応じて変形量が自動的にスケーリングされる
- `TryApplyBrushLayerContribution` でもマスクが適用され、ビルド時の出力にも反映
- ミラー編集にも対応
- Scene ビューで保護された頂点を赤、編集可能な頂点を緑で可視化

**貫通検出（Penetration Detection）:**
- `PenetrationDetector.cs` (`Editor/MeshDeformer/Utilities/`): 変形後の頂点が参照メッシュを貫通しているかを検出するユーティリティ
  - 最近傍頂点の法線方向とのドット積で内側/外側を判定
  - ブラシツールの Visualization セクション「Show Penetration」トグルで有効化
  - 参照メッシュ（Renderer）を ObjectField で指定。SkinnedMeshRenderer / MeshRenderer に対応
  - 貫通頂点は赤色のドットで Scene ビューにハイライト表示

### 頂点選択ツール（Vertex Selection Tool）

頂点を直接選択して Move/Rotate/Scale 変換を適用するツール。ブラシレイヤーの変位データを操作する。

**Editor:**
- `VertexSelectionTool.cs` (`VertexSelectionHandler`): 頂点選択・変換ハンドラ（`MeshDeformerTool` から委譲）
  - **選択方式**: クリック選択、Shift+クリックで追加、Ctrl+クリックでトグル、矩形ドラッグ選択
  - **変換モード**: Move（移動）、Rotate（回転）、Scale（スケール）
  - **プロポーショナル編集**: 選択頂点周囲の頂点にも減衰付きで影響。Smooth/Linear/Constant 減衰
  - **操作**: W/E/R で変換モード切替、Alt+スクロールでプロポーショナル半径調整

### BlendShape 出力・読み込み

コンポーネント全体の変形デルタを1つの BlendShape フレームとして出力、または既存 BlendShape をブラシレイヤーとして読み込む機能。

**出力 (`BlendShapeOutputMode`)** — コンポーネントレベル設定:
- `Disabled`（デフォルト）: 従来通り頂点に直接変形を適用
- `OutputAsBlendShape`: 全レイヤーの合成変形を1つの BlendShape として出力。頂点はソース位置のまま保持
- `BlendShapeName`: 出力名（有効化時に空なら `gameObject.name` で自動補完）
- `BlendShapeCurve` (`AnimationCurve`): BlendShape の補間カーブ。フレーム数 > 1 の場合、カーブに沿って複数フレームを生成
- `BlendShapeFrameCount`: 生成するフレーム数（1〜100、デフォルト 1）。1 ならカーブ無視で単一フレーム
- Inspector UI の「BlendShape Output」独立 Foldout セクション内に配置。テストスライダーで SkinnedMeshRenderer 上の重みをプレビュー可能
- NDMF ビルドパイプラインは `Object.Instantiate()` で BlendShape データを保持

**読み込み**:
- `LatticeDeformer.ImportBlendShapeAsLayer(int blendShapeIndex)`: ソースメッシュの BlendShape をブラシレイヤーとしてインポート
- `LatticeDeformer.GetSourceBlendShapeNames()`: 利用可能な BlendShape 名一覧を取得
- Inspector UI の「Import BlendShape」ドロップダウンからも操作可能

### レイヤー左右分割・反転（L/R Split & Flip）

VRChat アバターの対称ワークフロー向けのレイヤー操作機能。

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

- 旧バージョンの `LatticeDeformer`（単一 `_settings` で制御点を保持）を読み込んだ際、  
  自動で Layer-only モデル（Base layer なし）へ移行する。
- 移行時は旧 `_settings` を `Lattice Layer`（weight=1）として先頭レイヤーに取り込み、既存 `_layers` は後続レイヤーとして維持する。
- グリッド分割や Bounds/Interpolation は各レイヤーの独立設定になり、共有ベース構造は使わない。

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
- ビルドパイプラインは NDMF プラグインとして登録
- 変形処理は非破壊的に行い、元メッシュは変更しない
- ボーンウェイト再計算はビルド時に自動実行（オプション）

### ボーンウェイト再計算（Weight Transfer）

SIGGRAPH Asia 2023 論文 "Robust Skin Weights Transfer via Weight Inpainting" に基づく実装：
- **Stage 1**: 変形後の頂点位置から元メッシュ上の最近傍点を探索し、距離・法線閾値でウェイトを転写
- **Stage 2**: 転写できなかった頂点にラプラシアンベースの補間（Inpainting）を適用
- 設定は `LatticeDeformer` の Inspector UI で調整可能

**パフォーマンス最適化:**
- `MeshSpatialQuery.cs`: Burst Jobs (`IJobParallelFor`) による並列空間クエリ
- `WeightInpainting.cs`: Burst 実装の疎行列 (CSR) + BiCGStab 反復法ソルバー
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
  - `ImportBlendShapeAsLayer_CreatesMatchingBrushLayer`
  - `GetSourceBlendShapeNames_ReturnsCorrectNames`
- UnityMCP で対象アセンブリのみ実行する例:
  - `unity-mcp raw run_tests '{"mode":"EditMode","assemblyNames":["net.32ba.lattice-deformation-tool.tests.editor"],"includeDetails":true}'`

## 依存関係

- `nadena.dev.ndmf` >= 1.9.0 (VPM)
- `com.unity.mathematics` 1.2.6
- `com.unity.burst` 1.8.12
- `com.unity.collections` 1.2.4

## Claude Code へのルール

### タスク終了時の CLAUDE.md 更新

タスク完了時、以下の変更があった場合は **必ず CLAUDE.md を更新** してください：

- 新しいディレクトリやモジュールの追加
- 依存関係の変更（package.json の更新）
- 重要な設計パターンや規約の導入
- ビルド・テスト手順の変更
- その他、今後の開発で知っておくべき情報

更新時は既存のフォーマットに従い、簡潔かつ正確に記述してください。
