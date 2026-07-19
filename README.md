<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="Blobs~/logo-white.png">
    <img src="Blobs~/logo.png" alt="Lattice Deformation Tool" width="500">
  </picture>
</p>

# Lattice Deformation Tool

Unity 内で `MeshRenderer` / `SkinnedMeshRenderer` の形状をレイヤーとして調整し、NDMF Preview とビルド時 Bake へ非破壊で反映する Unity 2022.3 以降向けエディタ拡張です。ラティスだけでなく、ブラシ、頂点選択、マスク、BlendShape 入出力を1つのワークフローで扱えます。

衣装・髪・アクセサリのフィット調整、貫通箇所の手動修正、形状差分の BlendShape 化などに利用できます。元の Mesh アセットは変更しません。

English documentation: [README_en.md](README_en.md)

## 主な機能

- **Lattice**: 制御点を動かして広い範囲を滑らかに変形
- **Brush**: Normal / Move / Smooth / Mask の4モードで局所編集
  - Smooth / Linear / Constant / Sphere / Gaussian フォールオフ
  - 表面距離フォールオフ、X/Y/Z ミラー、貫通頂点の可視化
- **Vertex Selection**: 頂点を直接選択し、Move / Rotate / Scale を適用
  - 矩形選択とプロポーショナル編集に対応
- **Group / Layer**: Lattice と Brush レイヤーを重ね、名前・有効状態・ウェイトを個別管理
  - レイヤーやグループの複製、コピー＆ペースト、並べ替えと、レイヤーの左右分割・反転に対応
- **Vertex Mask**: 保護する頂点を塗り、Brush とレイヤー合成の変形量を制限
- **BlendShape**: 既存 BlendShape を Brush レイヤーへ読み込み、グループまたはレイヤーを新しい BlendShape として出力
- **Mesh rebuild**: 法線、タンジェント、Bounds、SkinnedMesh の Bone Weight を必要に応じて再計算
- **NDMF Preview / Bake**: Preview 中はプロキシ Mesh で確認し、アバターまたはワールドのビルド時にだけ変形を適用

## 対応対象と必要環境

- Unity 2022.3 LTS 以降
- `MeshFilter` + `MeshRenderer`、または `SkinnedMeshRenderer`
- NDMF (`nadena.dev.ndmf`) 1.9.0 以降
- VRChat Creator Companion（VPM から導入する場合に推奨）

## 導入

1. [VPM リポジトリ](https://vpm.32ba.net) を VCC に追加し、対象プロジェクトへ **Lattice Deformation Tool** を導入します。
2. 調整する Renderer と同じ GameObject に `LatticeDeformer` を追加します。
3. Inspector の **Skinned Mesh Source** または **Static Mesh Source** を確認します。同じ GameObject 上の対応 Renderer は自動設定されます。
4. **(NDMF) Enable Mesh Preview** を有効にし、Scene View で編集結果を確認します。

リポジトリを直接利用する場合は、VCC プロジェクトの `Packages` 以下へ配置してください。

## 基本操作

1. `LatticeDeformer` の Inspector で Group を作成または選択します。
2. Group 内に Lattice Layer または Brush Layer を追加します。
3. Layer を選び、Inspector 下部の **Open Lattice Editor** / **Open Brush Editor** を押します。
4. Scene View の **Mesh Deformer** Overlay から編集モードと設定を選びます。
5. Group / Layer の有効状態とウェイトを調整し、NDMF Preview で最終結果を確認します。
6. 通常の NDMF 対応アバター／ワールドのビルドを実行すると、生成 Mesh に変形が Bake されます。

### Scene View の主な操作

- Lattice: 制御点をクリック、Shift+クリックで追加選択し、ハンドルで移動
- Brush: Alt+スクロールで半径、Shift+スクロールで強度を調整
- Vertex Selection: クリック、Shift+クリック、Ctrl+クリック、矩形ドラッグで選択
- Vertex Transform: W / E / R で Move / Rotate / Scale を切り替え
- Proportional Editing: Alt+スクロールで影響半径を調整
- Undo / Redo: Unity 標準の Undo / Redo に対応

## 代表的なワークフロー

### 1. 衣装全体のシルエットをラティスで調整

1. Lattice Layer を追加し、グリッド数と Bounds を対象範囲に合わせます。
2. Lattice Editor を開き、複数の制御点を選択して移動します。
3. Layer Weight で効き具合を調整し、必要なら別 Layer に細部の修正を分けます。

### 2. 貫通箇所を Brush と Mask で局所修正

1. Brush Layer を追加し、Brush Editor の Normal または Move を選びます。
2. 動かしたくない領域は Mask モードで保護します。
3. 必要に応じて **Show Penetration** を有効にして参照 Renderer を指定し、赤く表示された頂点を修正します。
4. 頂点単位で仕上げる場合は Overlay を Vertex Selection に切り替えます。

貫通表示は編集補助用の近似判定です。現在ポーズを Bake した参照 `SkinnedMeshRenderer` の厳密な表面判定ではありません。

### 3. 形状差分を BlendShape として編集・出力

1. Source Mesh の既存 BlendShape を **Import BlendShape** から Brush Layer として読み込みます。
2. Brush または Vertex Selection で形状を編集します。
3. Layer または Group の **BlendShape Output** を有効にして出力名とカーブを設定します。
4. Test Mode と NDMF Preview でウェイト変化を確認してからビルドします。

直接変形する Group と BlendShape 出力する Group は同じコンポーネント内で併用できます。

## データの扱い

- Source Mesh アセットへ頂点変更を書き戻しません。
- 変形データは `LatticeDeformer` の Group / Layer としてシーンまたは Prefab に保存されます。
- NDMF Preview は表示用のプロキシ Mesh を利用し、終了時に上流の Mesh 表示へ戻します。
- 旧 `BrushDeformer` は Inspector の明示的な移行操作で Brush Layer へコピーできます。移行後の旧コンポーネントは無効なバックアップとして保持されます。

## ライセンス

本パッケージは MIT License で提供されています。詳細は [LICENSE](LICENSE) を参照してください。
