# 配布物の作成と内容検査

Python 3.10以降とGitを使用する。Unityや外部Python packageは不要。

```powershell
python -m unittest discover -s Tools~/Release -p 'test_*.py'
python Tools~/Release/package_release.py --commit HEAD --output C:/path/to/new-release-directory
```

出力directoryは新規に限る。指定commitのGit bytesを読み、未コミット変更や未追跡ファイルを含めない。ZIP、UnityPackage、package.json、verification.jsonを作る。両archiveを再読込みし、対象一覧・各byte・GUID・Packages配下のpathnameを照合してから出力directoryを確定する。同じcommit・Python/zlib環境では時刻に依存しないbyte-identical出力を要求する。

Tests、Tools~、AGENTS、隠しファイル、Docs~/Architectureは配布しない。Unityが無視する~ directoryのファイルは従来のexporterと同じくZIPだけに含み、reportへ一覧を出す。READMEのロゴと開発状況は公開versionのGitHub tagを参照するため、tag公開前のURL到達性は保証しない。

UnityPackageは既存の固定exporterと同じGUID directory内のasset.meta・pathname・非folderのassetを保存する。空folderのGUIDも保持する。参考: [既存actionの実装](https://github.com/pCYSl5EDgo/create-unitypackage/blob/b5c57408698b1fab8b3a84d4b67f767b8b7c0be9/src/index.ts)と、そのlockfileのunitypackage 1.0.8。

workflow_dispatchのpublishは既定false。falseでは生成・内容検査・Actions artifact保持だけを行い、tag/Release/VPMを書き換えない。trueは別の公開操作であり、最終の実機・通常更新・CI検証と利用者の公開承認後にのみ実行する。beta等のSemVer prerelease identifierがある場合はGitHub Releaseもprereleaseにする。build metadata中のhyphenはprereleaseと扱わない。

この検査はUnity Import、旧project更新、依存下限、native操作の検証を代替しない。reportのunityImportVerifiedはfalseのままにし、実際のUnityによる結果を別途保持する。
