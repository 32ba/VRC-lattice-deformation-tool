# 隔離Unityでの同期評価計測

`EvaluationBenchmark.cs` を検証専用Unityプロジェクトの `Assets/Editor/` にコピーする。
比較対象packageは明示的なcommitから配置し、Runtime/Editorを記録する。
基準版と候補版は同じUnity、package依存、hardwareとharnessを使用する。
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

70,000と200,000頂点それぞれで、Lattice、4 GroupのLattice/Brush混在、100 frameの生成BlendShapeを評価する。
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

このharnessはScene Viewの描画、drag入力、上流Preview、Clearance、Weight Transferの測定を代替しない。
