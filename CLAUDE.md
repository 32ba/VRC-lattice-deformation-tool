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
├── Runtime/             # ランタイムコンポーネント（MonoBehaviour, ScriptableObject）
└── package.json         # VPM パッケージ定義
```

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
- 日本語と英語の両方をサポート

### パフォーマンス考慮事項

- `Unity.Mathematics` と `Unity.Burst` を活用した高速な数値計算
- `Unity.Collections` の NativeArray を使用したメモリ効率の良い処理
- エディタ操作中はプレビューの更新頻度に注意

## ビルドとテスト

このプロジェクトは Unity パッケージとして提供されます。テストするには：

1. Unity 2022.3 以降で VCC プロジェクトを開く
2. パッケージを `Packages/` フォルダに配置または VPM 経由でインストール
3. NDMF 1.9.0 以降が必要

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
