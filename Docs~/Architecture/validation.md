# 互換性と配布の検証

コードの試験、Unity上の実操作、配布物の導入は別々に確認する。
自動テストの成功数だけでマウス操作や通常更新の成功を主張しない。

## 自動テスト

`.github/workflows/test.yml`はUnity 2022.3.22f1で`default`と`next-release`の2構成を実行する。
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
