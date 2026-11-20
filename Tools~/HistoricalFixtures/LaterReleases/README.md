# 後続公開リリースの実保存fixture

`1.4.1` から `1.4.6-beta.1` までの公開28件を、各tagのRuntimeで保存・評価する。
対象と順序は `Docs~/Architecture/2026-09-07-published-releases.json` の公開日時順で固定する。
既存14件の `HistoricalReleases` とそのgeneratorは変更しない。

## 生成

PowerShell 7、Git、tar、Unity 2022.3.22f1を使用する。
リポジトリrootから実行する。

```powershell
& 'Tools~/HistoricalFixtures/LaterReleases/Generate-LaterReleaseFixtures.ps1'
```

実行ごとに `%LOCALAPPDATA%/Codex/Lattice2BetaEvidence/LaterFixtures/<ID>/UnityProject` を作る。
既存のUnityプロジェクト、起動済みEditor、package junctionには接続しない。
`-EvidenceParent` で保管場所を変更できる。
各tagのpeeled commitとpackage versionを公開一覧と照合し、Git archiveで取得したRuntimeだけをコンパイルする。
generatorはproduct型へのコンパイル時参照を持たず、そのtagのpublic APIをreflectionで実行する。
Unity側でもコピー済みhelperのSHA-256を照合する。

生成物は `UnityProject/Assets/Generated/LaterReleases/` に置く。
scriptは検証後もproject、tag別Runtime archive、log、`run.json` を保持し、リポジトリのcorpusを自動置換しない。
失敗時も調査資料を保持する。
timeout・memory上限を超えた場合に停止するのは、その起動で所有したUnity processだけである。

動作確認用のsubsetは公開順で指定できる。

```powershell
& 'Tools~/HistoricalFixtures/LaterReleases/Generate-LaterReleaseFixtures.ps1' `
    -Tags @('1.4.1', '1.4.2-rc.1', '1.4.6-beta.1')
```

## fixtureの内容

- 全28件にEmbeddedのchannel保持・再構築の2ケースを保存する。
- Profileが存在する27件には、public APIで保存・適用した共有Profileと参照Prefabを追加する。
- 合計83ケース。全Prefabはinactive、componentはdisabledとし、読み込みテストが明示的に移行を進める。
- Group/Layer、active選択、Brush/Mask、3×3×3制御点、出力curve、保存fieldのscalar値と参照先を記録する。
- sourceは9頂点、2submesh、UInt32 index、8UV、color、normal/tangent、5 influence、5bindpose、2既存BlendShape×2frameを持つ。
- 各tagの `Deform(false)` による全Mesh channelと生成BlendShape全frameを期待値にする。
- source不変、rendererのsource保持も生成中に検査する。

`1.4.1` は版番号を保存しないため、group schemaの最古識別可能境界から既存の移行順を通す。
他の27件は保存値15を共有する。
manifestのexact tag provenanceと、保存payloadだけで判定できる版を混同しない。
Profileは旧実装が正常に評価できるactive group 0を使用する。
非zero選択の旧不具合はP2の独立した基準版probeで扱う。

## 検証と再現性

もう一度、同じhelper・tag・Unityで新しい隔離projectへ全件生成する。
次のread-only検証は、helper hash、公開一覧、全file hash、GUID、Prefab fileID、期待データの参照・curveを検査し、2回の全526fileがbyte-identicalであることを要求する。

```powershell
python 'Tools~/HistoricalFixtures/LaterReleases/Verify-LaterReleaseFixtures.py' `
    --corpus '<1回目>/UnityProject/Assets/Generated/LaterReleases' `
    --compare '<2回目>/UnityProject/Assets/Generated/LaterReleases' `
    --output '<検証JSONの保存先>'
```

完全一致を確認した生成物だけを `Tests/Editor/Fixtures/LaterReleases/` に反映し、同じcorpusに対する `LaterReleaseFixtureTests` と既存の移行・出力回帰を実行する。
期待値を候補実装の出力で更新しない。
helperを変更した場合は、28件すべてを再生成・再検証する。
元の14件用helperを変更する場合は、既存corpusの再生成gateも別途必要になる。
