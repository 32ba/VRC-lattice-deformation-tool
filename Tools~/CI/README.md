# CIの機能構成

`test.yml` はdefaultとnext-releaseの2構成で同じEditMode suiteを実行する。defaultは利用者向けの機能フラグを維持し、next-releaseは非表示の機能も検証する。Library cacheと結果artifactの名前は構成ごとに分ける。

最初のwarmupでVRChat設定が確定し、Editorが終了してから `feature_configuration.py --mode <default|next-release> --settings <ProjectSettings.asset>` を使う。Standaloneの指定フラグだけを追加・削除し、他platformと他defineは保持する。想定外の保存形式は書込み前に拒否する。起動中の利用者プロジェクトには使わない。

本試験後は `--results <editmode-results.xml>` を使い、指定構成だけでコンパイルされるテストが1件成功し、反対構成のテストが存在しないことを確認する。全体件数・成功・skip・必須Categoryは引き続きAssert-TestResults.ps1で検査する。この構成確認だけで全体試験の成功を代替しない。

Python側の検証は `python -m unittest discover -s Tools~/CI -p 'test_*.py'`。改行と他設定の保持、冪等性、未知形式・重複keyの拒否、誤った構成とSkippedの拒否を確認する。
