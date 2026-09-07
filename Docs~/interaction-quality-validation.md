# 操作の正しさの検証記録

## 対象と環境

対象は `1.4.6-beta.1`、基準commitは `3efcba79f4baebed127645973a027e59ef77f5bc`。
作業ブランチは `codex/interaction-quality`。
設計は [interaction-quality.md](interaction-quality.md) に記載し、GPT-5.6 Lunaへ実装を依頼した。
親担当が差分をレビューし、実入力テストを仕上げ、既存のUnity Editorで検証した。
利用者の意図は「正しく操作できること」であり、当初の「楽しく」は音声入力の誤変換だった。

検証日は2026年9月6日。
Unityプロジェクトは `D:/VRC/Projects/Plugin-dev-playground` を使用する。
既存のEditorを維持し、アバターのSceneを保存しない。

| 項目 | ローカル検証環境 |
| --- | --- |
| Unity | 2022.3.22f1 |
| Graphics | Direct3D11 |
| NDMF | 1.14.8 |
| Modular Avatar | 1.18.7 |
| Avatar Optimizer | 1.9.18 |
| Meshia Mesh Simplification | 3.2.0 |

CIの固定package版と、このローカル環境の版は異なる。
ローカルでの成功を、CI実行の成功と同一視しない。

## 検証する層

| 層 | 確認すること | この層だけでは確認できないこと |
| --- | --- | --- |
| コピーと貼り付けの操作契約 | 正しい頂点への適用、拒否時のデータ不変、理由、UndoとRedo | ボタンの到達性、画面の応答 |
| Scene Viewへの入力 | マウス入力の受付、対象切替、編集の再開 | OSや入力機器そのものの動作 |
| 実NDMFプレビュー | 最終表示meshへの反映、元meshの保持、表示の回復 | GPUに描かれたピクセルの見やすさ |
| 実画面の確認 | 説明の位置と読みやすさ、画面構成 | すべての画面サイズと操作の組合せ |

機械的な合否は、無反応、誤った対象への編集、UndoとRedoの不一致、理由のない拒否を検出するために使う。
応答時間の記録は同じ環境での変化を比較する材料とし、5秒のタイムアウトを快適さの目標としない。

## 変更前の基準

対象の2つのEditModeテストアセンブリを、変更前のbeta.1で実行した。
結果は984件成功、失敗0件、スキップ0件だった。
テスト実行時間は18.765秒。
これは既存テストの基準であり、今回追加する操作の保証を含まない。

成功後のConsoleには、NDMFの `NodeController.Create` が破棄済みの `SkinnedMeshRenderer` を参照した `MissingReferenceException` が1件残っていた。
このため、テストの成功を「実行後のEditorにも例外がない」という証拠にはしない。
この記録は実装用packageへ切り替える前に取得した。

## 変更後の検証

最終実行は **1,002件成功、失敗0件、スキップ0件**、実行時間28.384秒だった。
既存984件に、コピーと貼り付けの契約テスト15件、実入力シナリオ3件を追加した。
Unity Test Runnerが保持する結果をNUnit XMLとして書き出し、`Tools~/Assert-TestResults.ps1` でも検証した。
InteractionE2Eの必須3件、GraphicsE2Eの必須8件、既存のMA・探索系4カテゴリ各1件がすべて発見され、成功している。
リモートCIは実行していない。

### 実入力テストが検出したUndoの不具合

実際のInspectorの編集開始ボタンへマウス入力を送り、Scene ViewでMouseDown、別々の更新フレームのMouseDrag 2回、MouseUpを送った。
この操作で15頂点が変形したが、修正前は1回のUndoでMouseDown時の5頂点しか戻らず、後続ドラッグで編集した10頂点の変位が保存データに残った。
元の984件が成功していても、この操作経路には不具合が残っていた。

ブラシのMouseDownで、コンポーネントの操作前状態全体をUndoへ登録するように変更した。
修正後は1回のUndoで全変位が戻り、1回のRedoで編集結果が復元される。
保存データと、実NDMF PreviewSessionが公開する最終proxy meshの両方を照合している。
このシナリオでは入力の代わりに変位を書き込むAPIを呼んでいない。

### シナリオと応答の記録

以下は最終実行1回の測定値であり、性能目標や統計的な代表値ではない。
応答は入力開始から最終proxy meshが期待結果に一致するまでの時間で、画面のピクセルが表示された時刻ではない。

| シナリオ | 検証結果 | 入力からproxy一致まで |
| --- | --- | --- |
| A：編集開始、1ストローク、Undo、Redo | 15頂点変形、全Undo・Redo一致、元mesh不変 | ストローク40.8ms、Undo 8.7ms、Redo 6.1ms |
| B：対象切替、終了、元対象で再開 | 2対象の編集を分離、再開時に新たな変形、元mesh不変 | 各ストローク31.1 / 38.6 / 33.9ms |
| C：互換貼り付け、非互換拒否、理由表示、編集再開 | 拒否時の保存データ・Renderer・proxy不変、理由を実描画、再開で15頂点変化 | 元ストローク57.3ms、貼り付け13.7ms、再開35.0ms |

各ストロークは3 editor updates、貼り付けは1 update、UndoとRedoは同一update内でproxy一致を観測した。
3シナリオの実行と後始末で観測したError・Exception・Assertログは0件だった。
全体テスト後のConsoleには既存の異常系テスト由来のエラーや結果保存のログが残っており、Consoleの件数をシナリオ内の例外件数として扱っていない。

測定JSONは次の場所へ保存した。
頂点座標、頂点index配列、変位配列を含まない。

- [Aの記録](Validation/2026-09-06-interaction/interaction-a-stroke-undo-redo.json)
- [Bの記録](Validation/2026-09-06-interaction/interaction-b-target-switch.json)
- [Cの記録](Validation/2026-09-06-interaction/interaction-c-paste-rejection-resume.json)

### 画面確認とテストの入力位置

日本語のInspectorとScene View Overlayで、非互換の貼り付けを拒否した理由が表示されることを実画面で確認した。
Inspectorでは編集開始ボタンの直下、Overlayでは編集設定の上部に表示される。

866×519のScene Viewでは、右寄りの対象へ送っていたドラッグ終点が、拒否理由の表示で高くなったOverlayに重なることも確認した。
画面上の終点は窓内座標 `(558.09, 282.27)`、その時点のOverlayはおよそx=537以右、y=217以降を占めていた。
この状態ではSceneのブラシが入力を受け取れないため、テストは各ストロークの前に選択対象を画面中央へ合わせてから投影座標を取得するようにした。
Overlayを非表示にしたり、後続ドラッグの変形確認を削除したりして合格にはしていない。
狭い画面でパネルが対象を覆う問題は、今後の配置・折りたたみUIを検討する材料として残る。

### 後始末と作業場所

実装はbeta.1から作った別worktree `C:/Users/Yuki/ghq/github.com/32ba/VRC-lattice-interaction-quality` にある。
Playgroundのpackageリンクはこのworktreeを参照しており、既存のEditorで実装を確認できる。
元の作業中worktreeには変更を加えていない。

確認用の一時Scene、GameObject、Inspector窓は破棄し、元のSceneと `Phys_Haolan/Cityline Layered Set_005/inner` の選択、Moveツールへ戻した。
最終確認でSceneはdirtyではなく、コンパイル中でもなく、確認用オブジェクトと窓は0件だった。
手動確認前に保存した視点のSessionStateは最後に取得できなかったため、視点の完全復元を主張せず、元の選択対象を画面内へ収めた。
Sceneを保存しておらず、`HAOLAN.unity` のSHA-256は前後とも `69A84E7BD59DB3011471DD60C221091E64AC4E140A56D21D9D591C72180725F8` だった。

## この実装で保証していない範囲

実入力の3シナリオは独立生成した45頂点のMeshRendererを、Normalブラシで操作する。
SkinnedMeshRendererのポーズ中操作、他ブラシモード、頂点選択、ラティスの全操作、全Overlay配置の入力経路まで新たに保証するものではない。
それらの既存テストが成功したことと、新しい実入力検証の対象を区別する。
コピー・貼り付けはInspectorと共有するコマンドを実行しており、Copy/Pasteボタン自体のマウスクリックはこの3シナリオの対象外。
OSの入力機器やGPUの最終ピクセルを機械的に検査する仕組みでもない。

次に実入力の対象を増やすなら、頂点選択のMove・Rotate・Scaleと、ポーズ中のSkinnedMeshRendererを優先する。
今回の共通入力・proxy照合・レポート基盤を再利用し、各操作の保存データ、最終表示、Undoを独立して照合する。
