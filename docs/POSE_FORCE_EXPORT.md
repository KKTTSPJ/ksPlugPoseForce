# kPlug の姿勢補正の値の書き出し（kPlugPoseForceExport）

## 用途と対象

kPlugPoseForceExport は、KK（Koikatu）・KKP（Koikatsu Party）の通常版で使う小さなプラグイン（GUID `kkttspj.kk.kplugposeforceexport`）。実行中の kPlug から、体位・人・状態ごとの姿勢の補正値と手の付け先を集めて TSV に書き出す。書き出した表を KKS の ksPlugPoseForce が取り込む。

ksPlugPoseForce には kPlug の補正値が入っていないので、kPlug の値を使うときは、初めに一度ここで書き出して取り込む。kPlug を更新したときも同じ。

- KK の通常版・kPlug 3.6.3／3.6.4 で書き出しを確かめた。KKP でも同じ DLL が読み込まれる作りだが、KKP での動作は確かめていない（kPlug の版によっては、必要な機能が見つからず理由を出して止まる）。
- VR での書き出しには対応しない。
- kPlug は参照せず、実行中に探して読む。kPlug が無い場合は、書き出しを押したときに理由を出すだけ。

## 操作

1. 通常版で男女の Hシーンを始め、体位・状態の切り替えが落ち着くまで待つ。
2. ConfigurationManager の `kPlug Pose Force Export` → `PoseForceExport / Export` を開く。
3. 「kPlug の姿勢補正の値を書き出す」を押す（設定の値を true にして動かす項目ではなく、ボタン）。
4. ボタンの下の表示と、ログの `[PoseForceExport]` を確かめる。成功すると保存先・体位の数・行の数を表示する。

今選んでいる体位だけでなく、kPlug が補正の対象として持つ体位・状態をまとめて集める。全体位を順に再生する必要は無い。必要な機能が見つからない場合や、対応していない補正の欄・識別できない付け先などを見つけた場合は止めて、理由を表示する。新しい欄を書き出せても、KKS 側で使えるかは別に確かめる。

集めている間に変えた骨・IK・状態は、元に戻ったことを確かめてから保存する。ログの「復元検証」（骨の変化・復元エラー）と完了の表示を確かめる。途中で失敗した場合は、前の書き出しのファイルを新しい物と取り違えないよう、保存先の更新日時とログを確かめる。

## 出力

出力先は KK 側の `BepInEx/config/`。ファイル名に kPlug の版が入る。

| ファイル | 用途 |
|---|---|
| kPlugPose_kPlug<版>.tsv | KKS で取り込む主表。体位・人・状態・欄・最大の補正値・手の付け先 |
| PoseForceDiagnostics_kPlug<版>.tsv | 補正量に比例するか、ピストンの深さ（motion）で変わるかを確かめる診断表 |
| それぞれの .bak | 同じ名前のファイルがあった場合、更新する直前の版 |

主表は UTF-8（BOM なし）・10 列で、origin=kplug。値は補正量 1・motion=0.5 のときの値。kPlug 3.6.3／3.6.4 では 32 アセット・54 状態・10,805 行（うち手 298 行）。kPlug の姿勢補正の機能が対象で、kPlug のアニメーションやほかの機能は書き出さない。

## KKS へ渡す

1. 主表だけを KKS 側の `BepInEx/config/ksPlugPoseForce/import/` にコピーする（例: `kPlugPose_kPlug3.6.4.tsv`。`import` フォルダは、KKS で取り込みの画面を一度開くと作られる）。
2. KKS の ConfigurationManager の `PoseForce`、または値の編集画面から「書き出しの取り込み画面を開く…」を押す。
3. 更新日時の順に並ぶファイルから選び、「選んだファイルを取り込む」を押す（開くだけでは取り込まない）。
4. 取り込みの件数とログを確かめる。手動で直した行と削除の指定は残る。

診断表は取り込まない。KKS に無い体位の行は、表に残っても使われない。KKS 側で対応する挿入の種類・人・状態は [ksPlugPoseForce の使い方](POSE_FORCE_GUIDE.md) を参照。

## 診断表

- force の行: 補正量 1 と 0.5 で比例しない値。kPlug 3.6.3／3.6.4 では 0 件。
- motion の行: motion=0 と 1 で変わる値。kPlug 3.6.3／3.6.4 では 1 件（開脚の正常位の SLoop の女の右手）。ksPlugPoseForce は motion=0.5 の値で固定して使う。

確かめたのは kPlug 3.6.3／3.6.4 だけ。将来の kPlug の新しい体位の種類（男２人など）・役割・手の数などに、そのまま対応できるとは限らない。補助的な IK の重みや挿入の目標の直接の移動は、この表に含まれない。

---
更新日時 (JST): 2026-11-07
更新者: ChatGPT Codex（GPT-6）、Claude Code（Opus 5.5）
