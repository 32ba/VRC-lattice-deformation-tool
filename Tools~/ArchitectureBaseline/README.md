# 隔離Unityでの同期評価計測

`EvaluationBenchmark.cs` を検証専用Unityプロジェクトの `Assets/Editor/` にコピーする。
比較対象packageは明示的なcommitから配置し、Runtime/Editorを記録する。
基準版と候補版は同じUnity、package依存、hardwareとharnessを使用する。
新規projectではSDKによるdefine追加と再compileまで初期化を終え、別Editorセッションから計測する。
計測途中のassembly reloadはharnessが失敗として記録する。初期化中の部分記録を比較へ混ぜない。
利用中のEditorやpackage junctionを書き換えない。

```powershell
.\Tools~\ArchitectureBaseline\Run-Benchmark.ps1 `
  -ProjectPath 'D:\path\to\isolated-project' `
  -OutputPath 'D:\path\to\result.json' `
  -Commit '<tested-commit>'
```

runnerは画面なしのEditorを起動し、1秒間隔でそのプロセスだけを監視する。
private memoryの既定上限は6 GiB、時間は600秒。
超過時は自身が起動したプロセスを停止し、supervisor JSONに失敗理由を残す。
進行中の同一プロジェクトがある場合は起動しない。
Editor側は非同期にProfiler frameを回収してから自分で終了するため、`-quit` を追加しない。

## 測定範囲

70,000と200,000頂点それぞれで、Lattice、4 GroupのLattice/Brush混在、100 frameの生成BlendShape、ProfileのLatticeを評価する。
Profile条件は共有assetの制御点を直接変更し、コンポーネントへの通知なしで変更検知と独立コピーの更新を測る。
`-Filter 'profile-70000;profile-200000'` でこの2条件だけを測定できる。
Profile追加前の記録は6条件であり、新しい8条件の実行と混同しない。
法線・tangent再計算は無効、bounds再計算は有効。
初回、3回のwarmup後の変更なし15回、3回の編集warmup後の制御点変更15回を記録する。
`-Samples` と `-Filter 'direct-70000;generated-200000'` で範囲を指定できる。

CPU Profilerの固定CustomSampler配下から処理時間と `GC.Alloc` のsize metadataを読む。
既知の16,384 byte配列を使う較正が成功し、metadataが揃っている場合だけ割当値を採用する。
`GC.GetAllocatedBytesForCurrentThread()` がこのUnity環境で0を返すこと、`ProfilerRecorder` のallocation sampleのValueをbyte数と解釈できないことを検証済みであり、それらの値へfallbackしない。
sampleの識別はmarker IDを使い、全sample名を文字列へ変換する走査を避ける。

各操作後にProfiler frameを破棄し、較正と各条件の初回・最後の変更なし・最後の編集だけを `.raw` へ保存する。
全測定値はJSONへ保持する。
15 sampleのnearest-rank p95は最大値と同じになるため、小さな差を確実な高速化と解釈しない。
Mesh数は対象オブジェクトとsourceを破棄した前後の差であり、NativeArrayやUnity内部の全native allocationを測った値ではない。

## 比較とソース照合

```powershell
.\Tools~\ArchitectureBaseline\Compare-Benchmarks.ps1 `
  -BaselinePath 'D:\path\to\baseline.json' `
  -CandidatePath 'D:\path\to\candidate.json' `
  -OutputPath 'D:\path\to\comparison.json'

python .\Tools~\ArchitectureBaseline\Verify-PackageSource.py `
  --commit '<tested-commit>' --include-tests `
  --package 'D:\path\to\isolated-project\Packages\net.32ba.lattice-deformation-tool' `
  --output 'D:\path\to\source-verification.json'
```

比較は未完了・較正失敗・sample欠落・入力条件の違いを拒否する。
p95が10%を超えて増加した条件、最大GC割当の増加、破棄後のMesh残存を件数と条件ごとに出力する。
出力件数をレビューし、残る性能条件も含めて判断する。
ソース照合は改行とUnityが空のimporter値へ追加する空白だけを明示的に正規化し、GUIDやC#本文の違いを許容しない。

`preview-70000;preview-200000` は上流で位置を変更したmeshと1つのBlendShape frameを入力する同期Preview評価を測る。初回・変更なし・ラティス編集後を区別し、各操作にcaller-owned出力の破棄を含む。通常評価のcomponent-owned出力とは所有契約が異なるため、同じscenario同士だけで比較する。

`clearance-70000;clearance-200000` は4,096頂点の平面参照と7万/20万問い合わせ点を使用する。firstはBVH構築と初回Query、unchangedは既存BVHの反復Query、editedは対象のworld変換を変えたQuery。結果配列と内部APIの呼び出しdelegateは計測前に用意し、reflectionや結果のboxingを計測区間に含めない。各phase後に全点の既知距離を検証する。ReferenceNormalと開いた平面に限定し、ClosedMesh、SkinnedMeshRendererのBake、Scan/Fitや参照cache更新全体は含まない。

`brush-normal-70000;brush-normal-200000` はhandlerのRebuildCacheIfNeeded、Normalブラシ適用、変更通知、Deform(false)を測る。Linear falloff、半径0.1、強度0.001、connected/backface/surface無効。unchangedは固定hitへの反復stroke、editedはhitを交互に変えるstrokeで、いずれも変位は累積する。設定は終了時に復元し、最後の変位全配列のSHA-256一致を比較条件とする。delegate作成と検証hashは計測区間外。Mouseイベント配送、raycast、Undo記録、描画更新全体は含まない。

このharnessはScene Viewの描画、drag入力、NDMF全体の非同期Preview更新、Clearance Scan/Fit全体、Weight Transferの測定を代替しない。

`ProfileSelectionBaselineProbe.cs` は固定基準 `c7f499c` 専用の再現probe。
隔離基準hostの `Assets/Editor/` へコピーして `-executeMethod ProfileSelectionBaselineProbe.Run -quit` で実行すると、2番目のGroupを選んだProfileの適用結果、raw/public選択、Group数、Mesh生成可否をJSONに記録する。
候補の検証には `DeformerDataResolverTests` を使い、この基準probeのcommit表示を候補結果として流用しない。

## 実NDMF proxy更新の計測

`AsyncPreviewBenchmark.cs`を隔離Projectの`Assets/Editor`へコピーし、Unityを`-batchmode -force-d3d11 -executeMethod AsyncPreviewBenchmark.Export -asyncPreviewOutput <新しいJSONパス>`で起動する。起動中の利用者Projectや、保存したい未保存SceneがあるEditorでは実行しない。

7万/20万頂点のMeshRendererとBrush変位を使い、初回1回・warmup 3回・測定15回について編集通知から実proxyの全頂点一致までを測る。`-asyncPreviewForceRebuild`を追加すると、毎操作でForceRebuildを要求し、proxyのinstance IDが実際に変わるまで待つ。後片付けでは観測したMeshの残存数を記録する。completeは実行完了を示すため、残存数や比較結果は出力JSONで別途検査する。

`-asyncPreviewProfileAllocations`は同期書込み・refresh区間のCPU Profiler GC.Alloc size metadataを取得する。較正失敗やmetadata欠落はエラー。後続の非同期処理や描画のGCは含まない。Profilerを有効にしたrunの時間は、無効にしたrunの性能値へ代入しない。OS入力遅延・画面描画完了の計測ではない。全体試験数や製品配布内容にはこの開発用計測器を含めない。

## Runtimeのassembly依存（検査手順）

`Verify-RuntimeDependencies.ps1` はUnityのコンパイルDLLをMono.Cecilで読み、Editor/NDMFのassembly・型・member参照とIL呼出しを検査する。
`-AssemblyPath`、検証projectに導入済みの `Mono.Cecil.dll` を示す `-CecilPath`、結果の `-OutputPath` を指定する。
コンパイル直後のDLLは例外なしで検査する。
UnityのJobs処理後のDLLに限り `-AllowUnityJobInitialization` を付けると、生成型 `__JobReflectionRegistrationOutput__<number>` の静的 `EarlyInit` に付いた `InitializeOnLoadMethodAttribute` だけを許可する。
Editor APIの呼出し、他のEditor型、製品型への属性付与は引き続き拒否し、違反時は終了code 1を返す。
Unityの自動生成方式が変わって検査に失敗した場合は、実際のILを確認してから許可条件を更新する。

`proportional-70000;proportional-200000` は256選択頂点、半径0.03、Linearでhandlerの影響cacheを計測する。firstは初回生成、unchangedはcache hitを100回呼ぶbatch（1呼出しの値ではない）、editedはscale変更で1回再構築する処理。最終全頂点の影響度hash一致、有効範囲、選択頂点の影響度1を検査する。reflectionによる準備と結果検証は計測区間外。頂点の移動、Undo、pose取得、Scene View描画は含まない。
