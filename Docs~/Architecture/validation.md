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
- 実Previewの描画coroutineがnative logで中断した場合も、AAOの入力control・SceneView購読・Cage通知・選択・ツール・生成objectと、MA fixture/設定をTearDownで復元する。iteratorのfinallyだけに依存しない。元の描画失敗は失敗のまま保存する。
- Previewのフレーク調査は依存lock・言語・保存layoutを固定し、クリーン起動と同一セッションの順序変更を反復して比較する。前段失敗による状態漏れとnative font cacheの初回描画失敗を分け、固定sleepや無条件retryで通過扱いにしない。
- SceneView操作の準備完了はwindow生成や寸法だけで判断しない。AuthoringGestureではpreviewを一時停止したsourceの実カメラ描画完了を確認してからNDMFを開始し、実final proxyを待つ。描画とproxyは別の条件で検証し、5秒の各上限・全操作assertを維持する。shader cacheなしの隔離projectも検証し、既存projectのcacheは変更しない。
- Linuxの仮想display上のScene View event試験は、実OSマウス操作・利用者Avatar・Windows/macOSの検証を代替しない。
- 過去のEditorで保存したfixtureのmanifestや期待値は維持し、両Editorで同じ入力を読み込む。
- 公開gateは配布物検査と4組すべての同一commit CI成功を要求する。旧2 job名の結果で代用しない。

## Preview機能とローカライズ描画の分離

AAO/MAの実graph E2Eは、専用のSceneViewでカメラ描画・操作中のケージ保持・proxy更新・Undo/Redoを検証する。そのViewの`Mesh Deformer`操作パネルだけを`overlayCanvas.Remove`で外し、破棄時には専用Viewを閉じて元のViewへfocusを戻す。借用中の他ViewやそのOverlay、言語設定は変更しない。選択による遅延Inspector描画も混入するため、EditorTrackerは動かしたままInspectorの表示styleだけを保存して一時停止し、元の選択へ戻してからstyleを復元する。ITransientOverlayはUnityが再表示するため、単にdisplayed=falseとする方法に依存しない。

Cage通知は全SceneViewから届く。GUI hotControlはView単位なので、操作対象の`SceneView.currentDrawingSceneView`だけをmonitorへ取り込む。対象Viewの最低frame数・全mutation stage・cage shape・proxy identity・geometryのassertは維持する。別Viewの非操作frameを対象Viewの入力喪失と誤判定しない。

操作パネルの描画は既存`BrushToolOverlayTests.AllToolLanguagesAndModes_DrawWithoutChangingPayload`が3ツール・5言語・各modeを検証し、`LocalizedToolOverlay`カテゴリ1件をCI必須gateとする。67f1の既知font assertをこのUI試験から隠さず、元の失敗として記録する。graph E2Eの成功をローカライズUI成功の代用にしない。

`PreviewIsolation`カテゴリ1件は、専用Viewだけがパネルを持たないこと、他ViewのOverlay identityと使用言語の維持、終了時のView破棄、後で開くViewにOverlayが登録され続けることを確認する。これらの2カテゴリを最低1,769件に加えて必須検証する。

## SDKのテスト用config

CIの生成projectだけに`Tools~/CI/OfflineSdkConfig.cs`をコピーし、warmup/本実行へ`-latticeOfflineSdkConfig`を明示する。SDKの初期化後にEditor専用の公開API `ConfigManager.AssignTestRemoteConfig(null)`でSDK自身のローカル既定configへ切り替える。credential、ログイン、HTTP responseの偽装、ログ抑制は行わない。通常Editor・利用者project・製品packageには組み込まない。

このEditMode suiteはSDK componentを使うgeometry/preview/移行を対象とし、live VRChat config serviceやuploadの試験ではない。起動時の無関係なconfig HTTP requestが60秒後に任意のケースを落とすことを避ける。`verify_test_environment.py`は明示flagと初期化完了markerを必須にし、bootstrap欠落・初期化失敗を拒否する。live serviceを検証する場合はこの設定を使わず独立した試験を設ける。

AAO/MA fixture破棄前に実PreviewSessionをForceRebuildし、古いgraph contextを先に解放する。生成Avatarの破棄後に古いMA queryが参照する順序を避け、次のカメラ描画は同じ実plugin構成からgraphを再生成する。待機条件や元のgeometry assertは変えない。

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
- 許容件数は0〜4件、同一fullnameは1件だけ。成功したテストは例外数に含めない。4件すべての存在、完全runの最低1,766件、既存Categoryの正確な件数も要求する。
- 例外には版引数とEditorログの実版一致、通常のTest Runner終了code 2、対象XMLへの保存記録、完全一致の失敗message、case出力のassert、同数のnative assertそれぞれの`AddObjectToAsset` / `AddTextureToAsset` / `SetupNewAtlasTexture` stackが必要。同じassert文字列でも別のAssetDatabase障害は認めない。
- 未知failure、全Skipped/Ignore、Inconclusive、重複、root/leaf集計不一致、suite setup/teardown失敗、crash、ログ欠損、4件超過は拒否する。2022、69f1、将来版、版指定なしでは例外を認めない。
- CIで67f1のrunner stepだけ`continue-on-error`を用いるが、その直後の必須gateはrunner outcomeとXML/ログを照合し、未知失敗や未完了runを拒否する。raw artifactと`validation.json`を保存する。GitHub上のjob成功はこの承認条件の成立を示し、rawテストの全成功を意味しない。
- `validation.json`は`passed`または`accepted_with_known_issue`、raw成功/失敗/skip数、既知issue件数とfullnameを明記する。特にInteractionE2Eの対象8件中1件がこの例外になる場合、8件全成功とは報告しない。中断したテストの後続assertによる保証は得られていない。
- 解除条件: 指定Editorを公式修正版へ変更する場合は例外が自動的に無効になる。同じ67f1で問題が解消した場合は両構成で当該4件と全gateを再確認し、allowlistを削除する。別の失敗や版へ自動拡大しない。Unity6のVRChat SDK制作正式対応や無条件の製品対応保証を意味しない。

ローカルでは`Assert-TestResults.ps1`に`-UnityVersion`と`-EditorLogPath`を渡す。`-ReportPath`で分類結果を別JSONへ保存する。
失敗ログがあっても終了codeを一律無視せず、Unity終了code 0/2と通常終了の記録を確認してからgateへ渡す。

## WindowとOverlayの互換性

製品assemblyには独自EditorWindow派生型はなく、InspectorとSceneView上の`MeshDeformerToolOverlay`を使う。
Overlayの固定idは`Mesh Deformer`で、表示名やSceneViewのtitleContentを識別子として使わない。
5言語の現行catalogで製品名は同じ`Mesh Deformer`。`titleContent`/型による`GetWindow`は両Editorで利用でき、旧`EditorWindow.title`は2022でも既にobsoleteである。
Unity6ではWindow > Panelsの表示名がtitleContent.textを使い、2022のtooltip優先分岐がなくなっているが、製品はそのタイトル指定やmenu path検索に依存しない。

`EditorWindowLifecycle`カテゴリの5件は、生成時のOverlay名、非表示・折り畳み中の5言語切替とid不変、SceneViewのタイトル変更と型による再取得、close/reopen時のOverlay再作成と後始末、toolbarIconが共有GUIContentを変更しないことを確認する。
`EditorGUIUtility.IconContent`はUnityの共有cacheなので、tooltipを書き換える前にGUIContentを複製する。Texture参照は共有してよい。
`GetWindow<T>(title)`のtitleは既存windowの検索キーではなく、新規作成時の表示名である。既存windowの改名はtitleContentへ明示設定する。ただし組込みSceneViewはOnEnableで標準タイトルを再設定するため、その任意表示名をリロード後の識別に使わない。

ドメインリロード・layoutファイル復元・Editor再起動は別の隔離projectで実行し、実際の復元結果を保存する。内部WindowLayout APIを診断に使う場合はEditor版ごとのsignatureを確認し、製品コードへ依存を持ち込まない。
ウィンドウ名の検証だけでOS native file dialog、全dock配置、アクティブなcomponent toolの選択復元、全Editor APIの互換性を保証しない。

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

### 実Preview fixtureの終了条件

AAO/MAのfixture終了時は入力と購読を復元し、sessionのgraph contextを解放する。その後UnityTearDownでAvatarを非active化し、公開`ChangeNotifier.NotifyObjectUpdate`で直接変更をNDMFへ通知する。ウィンドウマネージャーのないbatch環境ではInspector/focus由来のproperty監視を頼れない。NDMFの公開`GetAvatarRoots()`が対象を含まなくなるまでEditor更新を進める。共有queryのinvalidationをflushしてからGameObjectとMeshを破棄する。これはNDMFの全非同期処理の完了を保証するAPIではなく、実際に破棄済みAvatarを返していた共有queryの参照解除を確認する条件である。180 frame以内に解除されなければテストを失敗させる。機能assert、言語設定、明示的な多言語UI試験は変更しない。

Prefab Stageの自動移行試験は、stage openによる選択変更が既存InspectorのCJK計測を起こすため、実Previewと同じ`InspectorPresentationScope`を使う。これはInspectorの表示styleだけを一時変更し、移行イベント・dirty/save・再openの冪等性をそのまま検証する。native log失敗でもTearDownからStage・一時asset・表示状態を復元する。ローカライズされたInspector/Overlayを直接描画する専用試験には適用しない。
