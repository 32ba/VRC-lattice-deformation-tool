# 配布物の作成と内容検査

Python 3.10以降とGitを使用する。Unityや外部Python packageは不要。

`verification.json`の`buildEnvironment`にPythonのversion・implementationと実行中zlibのversionを記録する。同じ展開内容でも圧縮実装によりarchiveのhashは変わり得るため、過去のhashと異なる場合はentryの内容と生成環境を照合する。byte-identicalの再現性は同じ生成環境で確認する。

```powershell
python -m unittest discover -s Tools~/Release -p 'test_*.py'
python Tools~/Release/package_release.py --commit HEAD --output C:/path/to/new-release-directory
```

出力directoryは新規に限る。指定commitのGit bytesを読み、未コミット変更や未追跡ファイルを含めない。ZIP、UnityPackage、package.json、verification.jsonを作る。両archiveを再読込みし、対象一覧・各byte・GUID・Packages配下のpathnameを照合してから出力directoryを確定する。同じcommit・Python/zlib環境では時刻に依存しないbyte-identical出力を要求する。

Tests、Tools~、AGENTS、隠しファイル、Docs~/Architectureは配布しない。Unityが無視する~ directoryのファイルは従来のexporterと同じくZIPだけに含み、reportへ一覧を出す。READMEのロゴと開発状況は公開versionのGitHub tagを参照するため、tag公開前のURL到達性は保証しない。

UnityPackageは既存の固定exporterと同じGUID directory内のasset.meta・pathname・非folderのassetを保存する。空folderのGUIDも保持する。参考: [既存actionの実装](https://github.com/pCYSl5EDgo/create-unitypackage/blob/b5c57408698b1fab8b3a84d4b67f767b8b7c0be9/src/index.ts)と、そのlockfileのunitypackage 1.0.8。

workflow_dispatchのpublishは既定false。falseでは生成・内容検査・Actions artifact保持だけを行い、tag/Release/VPMを書き換えない。trueは別の公開操作であり、最終の実機・通常更新・CI検証と利用者の公開承認後にのみ実行する。beta等のSemVer prerelease identifierがある場合はGitHub Releaseもprereleaseにする。build metadata中のhyphenはprereleaseと扱わない。

この検査はUnity Import、旧project更新、依存下限、native操作の検証を代替しない。reportのunityImportVerifiedはfalseのままにし、実際のUnityによる結果を別途保持する。

## 通常更新後の保存データ照合

`verify_normal_upgrade.py` には、同じevidence directory内の `before.json`、`saved.json`、`reloaded.json`、`before-files.json`、`old-archives`、`OldProject`、`UnityPackageProject` が必要。保存直後と再読み込み後のreport全体一致を必須とし、その後に旧版との4対象の出力・参照・選択・overrideと元assetの保持を検査する。Unity起動・importの成否と実行順序は別のlogで確認する。

## VPM更新後の保存データ照合

`verify_vpm_upgrade.py` は、旧版で保存した `NormalUpgradeProbe` の結果と、VPMクライアントで更新後に `SaveAndCapture`、別起動の `Capture` を実行した結果を照合する。

```powershell
python Tools~/Release/verify_vpm_upgrade.py --baseline C:/evidence/NormalUpgrade --evidence C:/evidence/VpmUpgrade --release C:/release/net.32ba.lattice-deformation-tool-2.0.0-beta.1.zip --output C:/evidence/VpmUpgrade/verification.json
```

baselineには `before.json`、`before-files.json`、`OldProject`、evidenceには `saved.json`、`reloaded.json`、`old-vpm-manifest.json`、`new-vpm-manifest.json`、更新後の `Project` を置く。出力は新規ファイルだけを許容する。保存直後と別起動再読込み後のreport全体一致、旧データと4対象の変形結果・参照・選択・override、新版ZIPとの全ファイル一致、旧C#ファイルの残存、他のVPM依存の維持を検査する。クライアント取得操作そのものは別の実行logと取得直後の照合記録で証明する。

## 公開対象commitのCI確認

publish=trueのときは、tag作成・Release・VPM更新より先にverify_release_ci.pyを実行する。対象の完全SHAに対するtest.ymlの最新push/manual runが成功し、Package archives、EditMode Tests (default)、EditMode Tests (next-release)がすべて完了・成功している必要がある。missing/skipped/duplicate job、進行中・失敗・取消を拒否し、古い成功runへfallbackしない。

PRのCIは一時的なmerge commitを試すため、この公開確認には使わない。masterへのpush、またはtest.ymlのworkflow_dispatchで公開対象commitを直接検証する。両構成を導入する以前の単一job成功も受け入れない。CI照会はGitHub CLIのread操作のみで、publish=falseの配布物生成にはCI照会を要求しない。

このチェックは実操作確認や利用者の公開承認を代替しない。workflowを追加しただけでリモートCIが成功したとは扱わない。
