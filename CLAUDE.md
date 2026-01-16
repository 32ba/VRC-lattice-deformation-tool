# CLAUDE.md

このファイルは、Claude Code がこのリポジトリで作業する際のガイダンスを提供します。

## プロジェクト概要

Lattice Deformation Tool は Unity 2022.3 以降向けのエディタ拡張で、VRChat アバターやワールドのメッシュに対して非破壊的なラティス変形を提供します。NDMF (Non-Destructive Modular Framework) と連携し、ビルド時にのみ変形を適用します。

## プロジェクト構造

```
├── Editor/           # Unity エディタ拡張コード
│   ├── Localization/ # 多言語対応（日本語/英語）
│   └── VRChat/       # VRChat 固有の機能
├── Runtime/          # ランタイムコンポーネント（MonoBehaviour, ScriptableObject）
└── package.json      # VPM パッケージ定義
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
