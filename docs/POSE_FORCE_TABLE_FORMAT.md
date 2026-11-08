# 姿勢補正の値の表（ksPlugPoseForce）

値の表は２つを重ねて使う。

- **内蔵の表**: DLL に埋め込まれた表（ソースの `ksPlugPoseForce/PoseForceBuiltin.tsv`、読み込み時に origin=builtin）。**今は空**（列名だけ）で、kPlug の補正値は入っていない。将来、独自の補正値を入れるための仕組み。
- **利用者の表**: `BepInEx/config/ksPlugPoseForce/PoseForce.tsv`。kPlug の書き出しを取り込むか、ゲーム内の編集を保存すると作られる。

体位ごとの「補正量」を保存する `PoseForceAmounts.tsv` とは別のファイル。

## 読み込みと上書き

- 内蔵の表を読み、利用者の表の同じ鍵（asset・chara・state・field）で上書きする。
- 利用者の表が無ければ、内蔵の表だけを使う（今は空なので補正しない）。画面を開くだけでは空のファイルを作らず、取り込みか、編集して「保存」したときに作る。手で作ってもよい。
- ファイルを編集したら、ConfigurationManager の `PoseForce` →「値の表を読み直す」を押す。ゲームを起動し直しても読み込む。
- 自分で書いた行・変えた行の origin は `user` にする（取り込みで上書きされない）。
- 取り込み・編集の保存では利用者の表に書き、前の版を `.bak` に残す。ゲーム内の編集は [編集の手順](POSE_FORCE_EDITOR.md)。

## 取り込む（kPlug の書き出し・共有データ）

1. 取り込むファイルを KKS の `BepInEx/config/ksPlugPoseForce/import/` に置く。KK の kPlugPoseForceExport で書き出した `kPlugPose_kPlug<版>.tsv`（主表。[書き出しの手順](POSE_FORCE_EXPORT.md)）か、ほかの人の共有データ。`PoseForceDiagnostics_*.tsv` は診断用なので取り込まない。
2. 編集画面か、ConfigurationManager の `PoseForce` にある「書き出しの取り込み画面を開く…」を押す。専用のダイアログでファイルを選び、「選んだファイルを取り込む」を押す。1 件でも自動では実行しない。
3. ファイルは更新日時の新しい順に並ぶ（`import` フォルダと、以前の置き場所の直下の `kPlugPose_*.tsv`。自分の `PoseForce.tsv` は並ばない）。選んだ名前を確かめて実行し、追加・置き換え・保持・削除・使わない（優先の高い値がある）の件数を、取り込み状況とログで確かめる。Hシーンの中なら、ゲームの体位の一覧と比べた対応数も表示する。

ファイルの種類は中身で見分ける。

| 中身 | 種類 | 取り込む行 |
|---|---|---|
| `origin=user` の行がある | 共有データ（`PoseForceShare_*.tsv`、ほかの人の `PoseForce.tsv`） | `user` の値の行と欄を消す指定だけ。`origin=pack:<データの名前>` にする。データの名前は注記 `# share<TAB><名前>`、無ければファイル名（拡張子なし） |
| すべて `kplug` の値の行 | kPlug の書き出し | すべて。`origin=kplug` |
| どちらでもない | 取り込まない | — |

同じ鍵の行がぶつかったときは、`user` ＞ `pack:…` ＞ `kplug` ＞ `builtin` の順に強い方を残す（体位を止める行は常に残す）。`pack:…` 同士は、後から取り込んだ方。同じ出どころ（同じ `kplug`、同じ `pack:<名前>`）を取り込み直すと、その出どころの行を新しい内容に置き換え、新しい内容に無くなった行は消す。共有データの「欄を消す指定」は、同じ欄に自分の行があれば取り込まない。２回取り込んでも同じ鍵の行は増えない。読めない行がある場合や、取り込める行が無い場合は取り込みを止め、今のファイルは書き換えない。

## 自分の値を書き出す

「自分の値の書き出し…」で、自分の表の `origin=user` の値の行と欄を消す指定を、`BepInEx/config/ksPlugPoseForce/export/PoseForceShare_<名前>_<日時>.tsv` に書き出す（体位を止める行・`kplug`・`pack:…`・`builtin` の行は含めない）。`import` フォルダなどにある kPlug の書き出しと、同じ鍵・同じ値・同じ付け先の行は除く。先頭の注記に `# share`（データの名前）・`# author`・`# plugin`・`# created`・`# scope`・`# rows` を書く。行の `origin` は `user` のままで、取り込む側で `pack:<名前>` になる。

２人・３人の挿入（sonyu・sonyu3P）と、状態の名前が完全に一致するアナルの状態に適用する。female2 は３人用の女性２人目。KKS に無いアセットの行も利用者の表に残す（適用はしない）。取り込んだ値は kPlug の値で、KKS の全体位の見た目を比べ済みという意味ではない。

## 列

タブ区切り、UTF-8。先頭が `#` の行は注記。数値の小数点は `.`。位置は m、回転は度。次の例の区切りは実際のタブ。

```text
asset	kplug	chara	state	field	x	y	z	target	origin
khs_f_00		female	InsertIdle	root_pos	0	0.05	0		user
```

| 列 | 内容 |
|---|---|
| asset | 女性１人目のアニメーションのアセット名（`paramFemale.path.file`） |
| kplug | 参考の名前。空でもよい |
| chara | `female`・`male`・`female2` |
| state | `Idle`・`InsertIdle` など、状態の正確な名前 |
| field | 下の補正欄 |
| x/y/z | 補正量１のときの最大値 |
| target | 手の IK の付け先。ほかの行では空 |
| origin | `builtin`（内蔵）・`kplug`（kPlug の書き出し）・`pack:<名前>`（取り込んだ共有データ）・`user`（自分の編集）。自分で変えた行は `user` |

人は female＝女性リストの１人目、female2＝２人目、male＝男性。３人用で挿入の相手が切り替わっても、人の意味は入れ替わらない。体位の鍵は、女性１人目のアセットを全員で共有する。`A_` の状態は接頭辞まで含めて照合し、通常の状態の値で代用しない。補正量はアセットごとで、同じアセットの通常・アナルは量を共有する。

状態をまとめる `*` は使わない。行の無い状態には補正を掛けない。０の行を置けば、その欄だけを０にできる。読めない行は飛ばし、ファイル名・最初の行番号・件数を警告する。同じ鍵が１つのファイルに複数あれば後の行を使い、警告する。

## 欄と骨

`L/R` は左・右で、それぞれ別の行。

| field | 骨 | 足し方 |
|---|---|---|
| root_pos | cf_j_hips | localPosition |
| waist_rot | cf_j_waist01 | localEulerAngles |
| spine_rot | cf_j_spine01 | localEulerAngles |
| spine_roll | cf_j_spine01 と cf_j_spine02 | x の値を両方の localEulerAngles.x に足す |
| L/R_shld_rot | cf_j_shoulder_L/R | localEulerAngles |
| L/R_hand_pos | cf_pv_hand_L/R | localPosition |
| L/R_elbow_pos | cf_pv_elbo_L/R | localPosition |
| L/R_foot_pos | cf_pv_leg_L/R | localPosition |
| L/R_foot_rot | cf_pv_leg_L/R | localRotation に Quaternion.Euler を掛ける |
| L/R_knee_pos | cf_pv_knee_L/R | localPosition |
| L/R_hipShp_pos | cf_s_leg_L/R | localPosition |
| L/R_thighFK_rot | cf_j_thigh00_L/R | localEulerAngles |
| L/R_shinFK_rot | cf_j_leg01_L/R | localEulerAngles |
| L/R_footFK_rot | cf_j_foot_L/R | localEulerAngles |
| L/R_toeFK_rot | cf_j_toes_L/R | localEulerAngles |

同じ骨に入る spine_rot と spine_roll は合わせて適用する。FK の回転が IK の後にも残るかは確かめていない。存在しない骨の欄は、警告して使わない。

手の行は `ik_hand_L`・`ik_hand_R`。target は `female:k_f_legupR_00` のように「人:骨の名前」、x/y/z は付け先から見たずらし（m）。female・male・female2 と、自分の骨も指定できる。付け替えは量が０より大きくなった時点で行い、ずらしだけが量に比例する。UseHandIK・既存の手のリンクの追従・診断は [手 IK](POSE_FORCE_HAND_IK.md)。

## 体位を止める

次の特別な行を利用者の表に置く。chara と state だけは `*` を使う。x が 1 なら体位全体を止め、0 なら止めない。

```text
khs_f_00		*	*	disabled	1	0	0		user
```

この行を消して読み直すと、表の値で補正する状態に戻る。

## 一つの欄を消す

画面の「この欄を消す」は、次の削除の指定を保存する。元の欄が取り込んだ表や内蔵の表にあっても、再起動や再取り込みで復活させないため。

```text
khs_f_00		female	InsertIdle	disabled:root_pos	1	0	0		user
```

field は `disabled:` と対応している欄名、x=1、y=z=0、target は空、origin は user（取り込んだ共有データの指定なら `pack:<名前>`）。chara・state は消す欄と同じ。同じ鍵の通常の補正行を隠し、ほかの人・状態には作用しない。再取り込みしても残る。

欄を足し直すと、その削除の指定は外れる。「kPlug・内蔵の値に戻す…」でも、この体位の手動の行と削除の指定を取り除ける。手の付け替えの欄を消した場合は、既存の手のリンクの追従に戻ることがある。手の付け替え・追従補助を止めるには UseHandIK を切る（手・肘の位置などの体の補正の欄は引き続き効く）。

---
更新日時 (JST): 2026-11-07
更新者: ChatGPT Codex（GPT-6）、Claude Code（Opus 5.5）
