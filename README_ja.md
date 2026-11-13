# ksPlugPoseForce

[English](README.md) / 日本語

H の挿入中の姿勢を、体位・状態ごとの値の表に従って補正する BepInEx プラグインです（腰の持ち上げ・反り、脚の開き、男の腰の位置、手の付け先など）。補正量（0〜1）は体位ごとに設定して覚えます。
KK（Koikatu）の **kPlug**（作者 Katarsys）にある姿勢補正を、Koikatsu Sunshine（KKS）で使うためのものです。

ゲームごとに 2 つのプラグインがあります。

| フォルダ | ゲーム | プロセス | 役目 |
|---|---|---|---|
| [ksPlugPoseForce](ksPlugPoseForce) | Koikatsu Sunshine | `KoikatsuSunshine` | 補正の適用、ゲーム内の編集、取り込み |
| [kPlugPoseForceExport](kPlugPoseForceExport) | Koikatu／Koikatsu Party | `Koikatu`・`Koikatsu Party` | 実行中の kPlug の補正値を TSV に書き出す |

**補正値は同梱していません。** 初めて使う前に、kPlugPoseForceExport で kPlug から書き出した表を ksPlugPoseForce で取り込むか、ゲーム内の編集画面で自分で値を入れてください。

- ksPlugPoseForce は ksPlug が無くても動きます。通常版だけです（VR は対象外）。
- 対応: ２人の挿入と、標準の３人（女２人＋男）の挿入。表にあるアナルの状態も含みます。対象外: MMF（男２人＋女１人）、ksPlug が２人モードのまま加える招待キャラ、奉仕など、エステ、VR。
- kPlugPoseForceExport は通常版の kPlug が必要です。kPlug のファイルは参照せず、実行中に読み取ります。
- 実機で確かめた環境: ksPlugPoseForce は KKS 通常版（ksPlug 1.7.3）、kPlugPoseForceExport は KK 通常版（kPlug 3.6.4。3.6.3 でも同じ表になります）。Koikatsu Party は確かめていません。
- 版: ksPlugPoseForce 0.1.6、kPlugPoseForceExport 0.1.1

## 1. 仕組み

- 補正は、ゲームが体の IK を解く直前に、骨と IK の目標に足します。手足が体から外れません。
- 表の 1 行は、1 つの体位（アニメーションのアセット）・人・状態・部位の補正の最大値（位置は m、回転は度）です。補正量を掛けて使います。
- 手は別の骨に付け替えられます（例: 男の手を女の太ももに）。付け先からのずらしも指定できます。
- 表はただの TSV（`BepInEx\config\ksPlugPoseForce\PoseForce.tsv`）です。自分で直した行は、新しい書き出しを取り込んでも残ります。
- **共有**: 自分で作った値をファイル（`export\PoseForceShare_<名前>_<日時>.tsv`）に書き出して、ほかの人に渡せます。受け取った人は自分の `import` フォルダに置いて取り込みます。書き出すのは自分で直した行だけです（kPlug の書き出しと同じ値の行は除きます）。同じ項目に値がいくつもあるときは、自分の値 ＞ 共有データ ＞ kPlug の値 ＞ 内蔵の値 の順に使います。

詳しくは次の文書を参照してください。

- [使い方](docs/POSE_FORCE_GUIDE.md)
- [ゲーム内の編集](docs/POSE_FORCE_EDITOR.md)
- [値の表の形式](docs/POSE_FORCE_TABLE_FORMAT.md)
- [手の IK](docs/POSE_FORCE_HAND_IK.md)
- [kPlug の値の書き出し](docs/POSE_FORCE_EXPORT.md)

## 2. 導入方法

1. BepInEx 5 と ConfigurationManager を入れます。
2. KKS: [Releases](../../releases) の ZIP の `ksPlugPoseForce.dll` を `BepInEx\plugins\ksPlugPoseForce\` にコピーします。
3. kPlug の値を使う場合: KK に `kPlugPoseForceExport.dll` を `BepInEx\plugins\kPlugPoseForceExport\` へコピーし、通常版の Hシーンで ConfigurationManager の `kPlug Pose Force Export` →「kPlug の姿勢補正の値を書き出す」を押します。書き出された `BepInEx\config\kPlugPose_kPlug<版>.tsv` を KKS の `BepInEx\config\ksPlugPoseForce\import\` にコピーし、ksPlugPoseForce の設定の「書き出しの取り込み画面を開く…」から取り込みます。
4. KKS の ConfigurationManager の `ksPlug Pose Force` → `PoseForce` で `EnablePoseForce` をオンにし、「調整の画面を開く」から補正量を上げます。

### 2.1. ソースからビルドする場合

各プロジェクトはゲーム本体と BepInEx の DLL を**相対パスで参照**します（`..\..\BepInEx\core\`、ksPlugPoseForce は `..\..\KoikatsuSunshine_Data\Managed\`、kPlugPoseForceExport は `..\..\Koikatu_Data\Managed\`）。

1. プラグインのフォルダ（`ksPlugPoseForce` または `kPlugPoseForceExport`）ごと、**そのゲームのインストール先の直下**に置きます。
2. そのフォルダで `build_and_deploy.ps1` を実行します（.NET SDK が必要です）。

`..\Deploy\<プラグイン名>\` に出力されます。別の場所に置く場合は、csproj の `HintPath` と `GameManagedPath` を環境に合わせて書き換えてください。kPlug・ksPlug の DLL は参照しません。

## 3. 設定項目

### 3.1. ksPlugPoseForce（`BepInEx\config\kkttspj.kks.ksplugposeforce.cfg`、節 `PoseForce`）

| キー | 既定 | 内容 |
|---|---|---|
| EnablePoseForce | false | 機能全体のオン／オフ |
| Force | 0 | 今の体位の補正量（0〜1）。体位ごとに `PoseForceAmounts.tsv` に保存し、体位に入ると読み込みます |
| WindowKey | 未割り当て | 補正量を操作する小さい画面の開閉 |
| EditorWindowKey | 未割り当て | 値の編集画面の開閉 |
| ForceUpKey／ForceDownKey | 未割り当て | 補正量を 0.1 増減。押し続けると繰り返します |
| UseHandIK | true | 手の付け先・ずらしと、既存の手のリンクの追従補助 |
| LegFKAfterIK | false | 太もも・すねの回転（thighFK_rot・shinFK_rot）を IK を解いた後に足します。脚の IK が働いている体位でも脚が動きますが、足が IK の目標から外れる（接地・接触がずれる）ことがあります |
| MoveUnlinkedLimbs | false | 手足のずらし（hand_pos・elbow_pos・foot_pos・foot_rot・knee_pos）を、ゲームの IK がその手足を自分の目標に付けていない体位・状態（騎乗位の女の手など）や、ゲームが IK を止めている状態でも効かせます。接触の位置がずれることがあります |
| ForceFootIK | false | foot_pos・foot_rot・elbow_pos・knee_pos の欄があるとき、ゲームの IK の付け先（相手の体など）に関係なく、足とひじ・ひざを自分の目標に付けて動かします。太もも・すねの欄がある脚は、LegFKAfterIK がオフなら足の IK を外します。相手の体との接触はずれます。付け替えは毎フレーム元に戻します |
| WheelAdjust | false | Hシーンで、右ボタンを押しながらホイールを回して補正量を変えます（右ボタンを押している間は続けて変えられ、その間はカメラのズームを止めます） |
| WheelStep | 0.1 | WheelAdjust の 1 目盛りの補正量 |
| LogBones | false | 調査用。骨と手の IK の値を約 1 秒ごとにログに出します |

ConfigurationManager には、ほかに「調整の画面を開く」「値の編集画面を開く」「値の表を読み直す」「書き出しの取り込み画面を開く…」「自分の値の書き出し画面を開く…」のボタンがあります。取り込むファイルは `BepInEx\config\ksPlugPoseForce\import\` に置き、自分の値は `BepInEx\config\ksPlugPoseForce\export\` に書き出されます（どちらも画面を開いたときに作られます）。

### 3.2. kPlugPoseForceExport（`BepInEx\config\kkttspj.kk.kplugposeforceexport.cfg`、節 `PoseForceExport`）

| キー | 内容 |
|---|---|
| Export | 「kPlug の姿勢補正の値を書き出す」のボタン（値は持ちません） |

## 4. 既知の制約

- 補正値は同梱していません。値の表が空のうちは補正しません（画面に「値の表が空」と出ます）。
- kPlug 3.6.3／3.6.4 の書き出しで、KKS で使えるのは通常の 26 体位と標準の３人用の 2 体位です。値の無い体位には、編集画面で自分で値を入れられます。
- 取り込んだ値は kPlug の値です。KKS のすべての体位・体格で見た目を確かめたわけではありません。
- kPlug の新しい版で補正の作りが変わると、書き出しが理由を出して止まることがあります。
- VR、MMF、２人モードに加えた招待キャラ、奉仕など、エステには対応しません。
- 欄によって効く条件が違います。手足のずらし（hand_pos など）は、ゲームの IK がその手足を自分の目標に付けている体位・状態だけで効きます。太もも・すね（thighFK_rot・shinFK_rot）は、脚の IK が働いていると IK に上書きされます。それぞれ MoveUnlinkedLimbs・ForceFootIK・LegFKAfterIK（既定オフ）で動かせます。編集画面に欄ごとの「効く／効かない（理由）」が出ます（[使い方](docs/POSE_FORCE_GUIDE.md) の「欄によって効かないとき」）。
- ゲームが女の IK を止めている状態では、手の付け替えは効きません。手足の IK の目標の欄は MoveUnlinkedLimbs か ForceFootIK がオンのときだけ効きます（体の骨の欄・太もも・すね・手首・指は補正します）。

## 5. 更新履歴

- ksPlugPoseForce 0.1.6:
  - 設定 ForceFootIK（既定オフ）を追加しました。オンにすると、foot_pos・foot_rot・elbow_pos・knee_pos を、ゲームの IK の付け先に関係なく自分の目標に付けて動かします（いろいろな体位で foot_pos を効かせたいという要望によるものです）。
  - WheelAdjust で、右ボタンを押し直さないと 2 目盛り目が効かなかったのを直しました。右ボタンを押している間は、ホイールで続けて増減できます。
- ksPlugPoseForce 0.1.5:
  - 手首（hand_rot）・指（4 指まとめての fingers_rot と、親指〜小指の関節ごと）の回転の欄を追加しました。IK を解いた後に足します。
  - 編集画面の「補正項目を追加」を日本語の名前で選べるようにしました（指の関節ごとの欄は折りたたみ）。
  - MoveUnlinkedLimbs がオンなら、ゲームが IK を止めている状態でも、欄のある手足だけを動かすようにしました（ゲームの IK の設定は毎フレーム元に戻します）。
  - 設定 WheelAdjust・WheelStep（右ボタン＋ホイールで補正量を変える、既定オフ）を追加しました。
  - 利用者の方から寄せられた改変を元に、作り直して取り込みました。
- ksPlugPoseForce 0.1.4:
  - ゲームが IK を止めている状態でも、体の骨の欄と太もも・すねを補正するようにしました（kPlug と同じ動き）。0.1.3 は何も補正していませんでした。
  - 設定 LegFKAfterIK（太もも・すねを IK の後に足す）と MoveUnlinkedLimbs（ゲームの IK が自分の目標に付けていない手足もずらす）を追加しました。どちらも既定はオフです。
  - 編集画面に、欄ごとの「今の状態で: 効く／効かない（理由）」を出すようにしました。LogBones のログに、手足ごとの IK の付け先と重み（`[PoseForce][手足]`）を足しました。
- ksPlugPoseForce 0.1.3・kPlugPoseForceExport 0.1.1: 最初の公開。

## 6. ライセンス・免責

- **MIT ライセンス**です（[LICENSE](LICENSE)）。
- **本ソフトは無保証で提供されます。** 利用によって生じたいかなる損害（セーブデータの破損、ゲームの不具合、他プラグインとの競合などを含む）についても、作者は責任を負いません。自己責任でご利用ください。
- **kPlug・ksPlug は別の作者による著作物であり、本リポジトリ・配布物には含まれていません。kPlug の補正値も含まれていません。** kPlug の値を使う場合は、利用者が自分の環境の kPlug から書き出してください。ゲーム本体・BepInEx・その他のプラグインのファイルも同梱していません。
- 本プラグインはゲームの動作を実行時に書き換えます。ゲームや kPlug の版が変わると、機能の一部または全部が働かなくなることがあります（その場合はログに理由が出ます）。

---
作成日時: 2026年11月7日 / 最終更新: 2026年11月13日 (ksPlugPoseForce 0.1.6・kPlugPoseForceExport 0.1.1)
作成者: Claude Opus 5.5、ChatGPT Codex（GPT-6）
