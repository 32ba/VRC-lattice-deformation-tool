# 互換性と配布の検証

コードの試験、Unity上の実操作、配布物の導入は別々に確認する。
自動テストの成功数だけでマウス操作や通常更新の成功を主張しない。

## 自動テスト

`.github/workflows/test.yml`はUnity 2022.3.22f1と6000.0.67f1のそれぞれで`default`と`next-release`を実行する（4組）。
Editorごとに新しい隔離プロジェクトを作り、Library cache・結果artifactもEditorと構成の組で分離する。
Test Frameworkは2022で1.4.6、Unity 6で1.6.0を指定する。Unity組込みpackageの解決差（uGUI等）は各projectのpackages-lock.jsonで記録する。
依存バージョンと必須Categoryの期待件数はworkflowを正本とし、結果XMLで構成専用markerとCategoryの実行を照合する。

2.0.0-beta.1公開時には両構成とも1,759件成功、失敗・スキップ0を確認した。
これは公開時の検証記録であり、後続commitの結果を保証するものではない。

| 変更箇所 | 必須の確認 |
| --- | --- |
| 保存・公開API | `ArchitectureCompatibilityTests`などによる固定基準の型・API・保存path・GUIDの照合 |
| 数値評価 | `DeformationOutputCompatibilityTests`の13条件、全Mesh channel、BlendShape全フレーム、source/upstream不変 |
| 移行 | `HistoricalReleaseFixtureTests`、`LaterReleaseFixtureTests`、`ReleaseJournalFixtureTests`、`PublishedDeformationMigrationTests`による直接・順次移行、失敗時保全、保存再読込み |
| 編集 | 編集境界・セッションの試験、Undo/Redo、複数対象の復元、Prefab Apply/Revert、Profile不変 |
| Preview | 実NDMF graph、入力変更、対象切替、Meshの所有・復元、ドメインリロードと終了時の解放 |
| 周辺機能 | Clearance/Scan/Fit/QA、Profile互換性、Weight Transfer、サポート出力の該当回帰試験 |

既存fixtureと期待値は候補実装の出力で更新しない。
再生成が必要な場合は各公開tagのRuntimeを使い、独立した2回の生成結果を照合する。
後続28版の526ファイル・83ケースは、helper hash、GUID、Prefab fileID、内容hashも検査する。
手順は`Tools~/HistoricalFixtures/`と`Tools~/HistoricalFixtures/LaterReleases/README.md`を参照する。

## Unity 6の検証範囲

VRChatの[制作向けUnity指定](https://creators.vrchat.com/sdk/upgrade/current-unity-version/)は2022.3.22f1。
[2026.3.3 Open Beta](https://docs.vrchat.com/docs/vrchat-202633-open-beta)の6000.0.67f1採用はclient側であり、SDKプロジェクトのUnity 6移行許可ではない。
Unity 6の試験は本packageの先行互換性検証として実施し、利用者の既存プロジェクトを開いて更新しない。

- 両Editorともworkflowの固定依存を導入し、warmupを完了してから構成defineを設定する。
- `-nographics`を使った試験ではGraphicsE2EやScene View入力の合格を主張しない。描画deviceをログで確認し、失敗・Skipped・Inconclusive・0件を分けて記録する。
- 同一ユーザー設定を共有するEditorは直列実行する。独立projectでもEditorPrefsの言語・ツール設定は共有され得る。
- Unity内部のフォントatlas追加assertが起きた場合は、VPM依存や本packageのないprojectで標準Editor labelによるCJK描画を確認する。製品なしの再現を分離して記録し、元のUI試験をignore/skipやログ抑止で合格にしない。
- Linuxの仮想display上のScene View event試験は、実OSマウス操作・利用者Avatar・Windows/macOSの検証を代替しない。
- 過去のEditorで保存したfixtureのmanifestや期待値は維持し、両Editorで同じ入力を読み込む。
- 公開gateは配布物検査と4組すべての同一commit CI成功を要求する。旧2 job名の結果で代用しない。

## UUM-85059の承認済み限定例外

[Unity Issue UUM-85059](https://issuetracker.unity.com/issues/6324/assert-error-is-thrown-when-the-editor-language-is-set-to-one-of-the-experimental-ones)はEditorのCJK font atlas拡張で発生する内部assert。
[6000.0.69f1](https://unity.com/releases/editor/whats-new/6000.0.69f1)の公式修正と、製品なしの同一最小再現（67f1失敗、69f1成功）で原因を確認した。
対象Editorは2022.3.22f1 / 6000.0.67f1のままとし、承認された例外は**6000.0.67f1の次の4テストだけ**に限定する。OSによる条件分岐は設けない（公式にはWindows/macOSでも再現）。

| Test fixture | Test method |
| --- | --- |
| AuthoringGestureEndToEndTests | BrushEscape_AfterAnotherUndoOperationPreservesBothEdits |
| BrushToolOverlayTests | AllToolLanguagesAndModes_DrawWithoutChangingPayload |
| GuidedAuthoringTests | GuidedPaint_AllLanguagesPreserveNullPayloadWithoutInitializingIt |
| PeripheralInspectorTests | RebuildInspector_PaintPreservesMixedValuesAndUnknownEnumWithoutUndoOrDirtyChanges |

- テストは省略しない。元のassert、描画、実行順、NUnit XML、Editorログを保持し、`LogAssert.Expect`による抑止や成功への書換えは行わない。
- `Tools~/CI/verify_test_results.py`を唯一の分類規則とし、PowerShell入口と機能構成markerの検証から共用する。Python 3が必要。
- 許容件数は0〜4件、同一fullnameは1件だけ。成功したテストは例外数に含めない。4件すべての存在、完全runの最低1,760件、既存Categoryの正確な件数も要求する。
- 例外には版引数とEditorログの実版一致、通常のTest Runner終了code 2、対象XMLへの保存記録、完全一致の失敗message、case出力のassert、同数のnative assertそれぞれの`AddObjectToAsset` / `AddTextureToAsset` / `SetupNewAtlasTexture` stackが必要。同じassert文字列でも別のAssetDatabase障害は認めない。
- 未知failure、全Skipped/Ignore、Inconclusive、重複、root/leaf集計不一致、suite setup/teardown失敗、crash、ログ欠損、4件超過は拒否する。2022、69f1、将来版、版指定なしでは例外を認めない。
- CIで67f1のrunner stepだけ`continue-on-error`を用いるが、その直後の必須gateはrunner outcomeとXML/ログを照合し、未知失敗や未完了runを拒否する。raw artifactと`validation.json`を保存する。GitHub上のjob成功はこの承認条件の成立を示し、rawテストの全成功を意味しない。
- `validation.json`は`passed`または`accepted_with_known_issue`、raw成功/失敗/skip数、既知issue件数とfullnameを明記する。特にInteractionE2Eの対象8件中1件がこの例外になる場合、8件全成功とは報告しない。中断したテストの後続assertによる保証は得られていない。
- 解除条件: 指定Editorを公式修正版へ変更する場合は例外が自動的に無効になる。同じ67f1で問題が解消した場合は両構成で当該4件と全gateを再確認し、allowlistを削除する。別の失敗や版へ自動拡大しない。Unity6のVRChat SDK制作正式対応や無条件の製品対応保証を意味しない。

ローカルでは`Assert-TestResults.ps1`に`-UnityVersion`と`-EditorLogPath`を渡す。`-ReportPath`で分類結果を別JSONへ保存する。
失敗ログがあっても終了codeを一律無視せず、Unity終了code 0/2と通常終了の記録を確認してからgateへ渡す。

## Unity上の操作

専用の検証プロジェクトを使い、対象package、コンパイル完了、正常なScene Viewを確認してから操作する。
利用者が編集中のSceneをテストのために自動保存しない。

- Brushの各mode、Mask、mirror、頂点のMove/Rotate/Scale・矩形選択・比例編集、ラティスの編集を確認する。
- 複数フレームのdragとUndo/Redo、対象切替、PrefabとProfileの操作を確認する。
- NDMFと併用packageを含むPreviewの更新・終了・再生成を確認する。
- OS入力とScene Viewへの自動イベント送信は実行方法を区別して記録する。

性能は同一の入力、依存、計測方法で固定基準と比較する。
初回とcache再利用時を分け、応答時間、GC、生成Mesh、NativeArrayの寿命を確認する。
代表メッシュの測定を全Avatar・全設定の保証に広げない。

## 配布物と通常更新

固定commitから`Tools~/Release/package_release.py`でZIPとUnityPackageを作成する。
実行例では出力先に新しいローカルディレクトリを指定する。

```powershell
python -m unittest discover -s Tools~/Release -p 'test_*.py'
python -m unittest discover -s Tools~/CI -p 'test_*.py'
pwsh -File Tools~/Test-AssertTestResults.ps1
python Tools~/Release/package_release.py --commit HEAD --output '<new-output-directory>'
```

archive内の対象ファイル、内容、GUIDを再読込みして照合する。
Docsの作業記録、Tests、Tools、AGENTSは配布対象に含めない。
圧縮実装が異なる場合はarchive全体のhashだけで判断せず、展開後の内容も比較する。

UnityPackage導入とVPM通常更新をそれぞれ別の隔離プロジェクトで確認する。
更新前後の変形出力、source、参照、選択、Prefab overrideを比較し、保存後に別起動で再読込みする。
内容検査だけをUnity導入の成功として扱わない。

公開前は対象commit自身のCI成功を確認する。
公開後はtag、prerelease属性、取得した配布物、VPMのmetadata・URL・hashを照合する。
