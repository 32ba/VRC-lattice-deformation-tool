# CIの出荷状態検証

`test.yml` は出荷時の機能フラグ状態のままUnity 2022.3.22f1と6000.0.67f1でEditMode suiteを実行する（2 job）。無効な次期機能はCIで強制有効化せず、機能構成を分けた追加jobを作らない。機能を正式有効化する判断とは別の方針であり、製品のフラグは変更しない。Library cacheと結果artifactの名前はEditorごとに分ける。cache keyのshipping prefixにより以前の機能構成cacheを再利用しない。

最初のwarmupでVRChat設定が確定し、Editorが終了してから同じ出荷設定で本試験を実行する。ProjectSettingsの機能defineを書き換える手順はない。

本試験後は `feature_configuration.py --results <editmode-results.xml>` を使い、現在の出荷状態を示すテストが1件成功し、開発用有効化のmarkerが存在しないことを確認する。このスクリプトは結果XMLを読むだけで、設定ファイルへの書込みや機能有効化のオプションを持たない。全体件数・成功・skip・必須Categoryは引き続きAssert-TestResults.ps1で検査する。出荷状態の確認だけで全体試験の成功を代替しない。既存の保存データ・評価・現行操作の有効なassertは維持する。

Python側の検証は `python -m unittest discover -s Tools~/CI -p 'test_*.py'`。出荷markerの欠落・重複・失敗・Skipped、開発機能の混入と未知失敗の拒否を確認する。
