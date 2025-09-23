# Lattice Deformation Tool

Unity 2022.3 以降に対応するラティス変形用の Unity エディタ拡張です。NDMF のプレビュー機能と連携し、元のメッシュを破壊しない非破壊ワークフローで編集できます。

English version is available in [README_en.md](README_en.md).

## 特徴
- NDMF のプレビュー機能を利用する非破壊ワークフローでラティス変形が可能で、編集中はオリジナルメッシュを置き換えず、プロキシ上で変形結果を確認できます。
- Scene ビューの **Lattice Tool** で境界制御点をクリック選択し移動することでラティスを編集できます。
- インスペクタにはメッシュ更新オプション（法線／タンジェント／境界の再計算）と `(NDMF) Enable Lattice Preview` トグルを用意し、プレビューの ON/OFF を切り替えられます。

## 使い方（概要）
1. [VPMリポジトリ](https://vpm.32ba.net) 経由でパッケージを導入するか、リポジトリを VCC ワークスペース内の `Packages` に配置します。
2. `LatticeDeformer` を `MeshFilter` または `SkinnedMeshRenderer` を持つ GameObject に追加します。
3. インスペクタでターゲットレンダラーを設定し、**Lattice Settings** でグリッドサイズやバウンズを調整します（詳細設定は Advanced Settings 内）。
4. **Activate Lattice Tool** を押して Scene ビューで境界制御点を選択 → PositionHandle で編集します。
5. 編集が完了したら NDMF のビルドパイプラインを実行し、変形済みメッシュをベイクします（ベイク時に元コンポーネントは自動で削除されます）。

## コントロールと Tips
- **選択**: 境界制御点は小さなキューブとして描画されます。クリックするとハイライトと PositionHandle が表示されます。
- **プレビュー切り替え**: インスペクタの `(NDMF) Enable Lattice Preview` ボタンで、プロキシ表示を即時に切り替えられます。
- **Undo/Redo**: 標準の Undo/Redo を完全サポート。操作後は自動的にプレビューを再計算します。

## 必要環境
- Unity 2022.3 LTS 以降
- VRChat Creator Companion（推奨）
- NDMF (`nadena.dev.ndmf`) 1.9.0 以降

## ライセンス
本パッケージは MIT License で提供されています。詳細は `LICENSE` を参照してください。