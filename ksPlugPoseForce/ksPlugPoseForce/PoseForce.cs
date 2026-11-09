using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RootMotion.FinalIK;
using UnityEngine;
using UnityEngine.UI;

namespace ksPlugPoseForce
{
    /// <summary>
    /// 表から体位・人・状態の補正値を選び、IK を解く直前に適用する。
    /// 補正量はキー・ConfigurationManager・調整の画面で共有する。
    ///
    /// 【当て方】
    ///   男女それぞれの FullBodyBipedIK の solver.OnPreUpdate（アニメーションの後、IK を解く直前）で、骨の localPosition・localEulerAngles に足す。
    ///   IK は補正した腰・足の目標から解くので、手足が腰から外れない。LateUpdate で足すと、FinalIK やほかの mod の LateUpdate での骨の補正との順番が決まらない。
    ///   骨がアニメーションで毎フレーム上書きされないときに足し続けにならないよう、前のフレームに書いた値のまま残っていたら、
    ///   前のフレームの元の値に足し直す（Slot）。
    ///   0.1.4: ゲームが IK を止めている人には OnPreUpdate が来ないので、LateUpdate で IK に関係しない欄を足す（kPlug と同じ）。
    ///   LegFKAfterIK は太もも・すねを OnPostUpdate（IK の後）で足す。MoveUnlinkedLimbs は手足の IK の目標を IK の間だけ差し替える。
    /// </summary>
    internal static partial class PoseForce
    {
        private const string Tag = "[PoseForce]";
        private const int ChangeFrames = 5;   // 体位を変えた後、補正を止めるフレーム数

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Force;
        internal static ConfigEntry<KeyboardShortcut> WindowKey;
        private static ConfigEntry<KeyboardShortcut> _editorWindowKey;
        internal static ConfigEntry<bool> LogBones;
        internal static ConfigEntry<bool> UseHandIK;
        internal static ConfigEntry<bool> LegFKAfterIK;
        internal static ConfigEntry<bool> MoveUnlinkedLimbs;
        private static bool _noIK;   // 女の IK が止まっている状態で、IK に関係しない欄だけを足している
        private static ConfigEntry<float> _winX, _winY;
        private static ConfigEntry<float> _editorWinX, _editorWinY;
        private static PoseForceTable _table;
        private static readonly string TablePath = Path.Combine(PoseForcePlugin.DataDirectory, "PoseForce.tsv");

        private static HSceneProc _proc;
        private static System.Reflection.FieldInfo _lstFemale, _male, _aaWeightsBody;
        private static int _changedFrame = -1000;
        private static Rig _female, _female2, _maleRig;
        private static int _bodyFrame = -1;
        private static bool _failed;
        private static string _reason = "Hシーンの中で使えます";
        private static bool _active;
        private static float _nextLog;

        internal static void BindConfig(ConfigFile config)
        {
            const string s = "PoseForce";
            Enabled = config.Bind(s, "EnablePoseForce", false,
                "値の表がある２人・３人の挿入（アナルを含む）の姿勢を補正します。値は kPlug の書き出しの取り込みか、値の編集画面で用意します（kPlug の補正値は内蔵していません）。切ると次のフレームから補正しません。");
            Force = config.Bind(s, "Force", 0f, new ConfigDescription(
                "今の体位の補正量（0 = 補正なし、1 = 最大）。体位ごとに保存し、初めての体位は 0 で始まります。Hシーンの外で変えた値は体位には保存せず、次に体位に入るとその体位の保存値に戻ります。",
                new AcceptableValueRange<float>(0f, 1f)));
            WindowKey = config.Bind(s, "WindowKey", KeyboardShortcut.Empty, "補正量を操作する小さい調整画面を開閉するキー。");
            _editorWindowKey = config.Bind(s, "EditorWindowKey", KeyboardShortcut.Empty, "値・手の付け先を編集する画面を開閉するキー。WindowKey とは別のキーに割り当ててください。既定は未割り当て。");
            config.Bind(s, "OpenWindow", false, new ConfigDescription("補正量を操作する調整画面", null,
                new ConfigurationManagerAttributes { CustomDrawer = entry => { if (GUILayout.Button("調整の画面を開く")) OpenSimpleWindow(true); } }));
            config.Bind(s, "OpenEditor", false, new ConfigDescription("補正値と手の付け先を編集する画面", null,
                new ConfigurationManagerAttributes { CustomDrawer = entry => { if (GUILayout.Button("値の編集画面を開く")) OpenEditorWindow(true); } }));
            LogBones = config.Bind(s, "LogBones", false,
                "デバッグ用。補正している間、骨の値と、骨がアニメーションで上書きされているかを 1 秒ごとにログに出します。");
            _winX = config.Bind(s, "WindowX", 20f, new ConfigDescription("画面の横位置", null, new ConfigurationManagerAttributes { Browsable = false }));
            _winY = config.Bind(s, "WindowY", 300f, new ConfigDescription("画面の縦位置", null, new ConfigurationManagerAttributes { Browsable = false }));
            _editorWinX = config.Bind(s, "EditorWindowX", 380f, new ConfigDescription("編集画面の横位置", null, new ConfigurationManagerAttributes { Browsable = false }));
            _editorWinY = config.Bind(s, "EditorWindowY", 30f, new ConfigDescription("編集画面の縦位置", null, new ConfigurationManagerAttributes { Browsable = false }));
            PoseForceControls.BindConfig(config);
            UseHandIK = config.Bind(s, "UseHandIK", true, "表に手の行があるとき、手の IK の付け先とずらしを使います。手の行が無いときは、相手の骨を使う既存の手のリンクを補正後の骨に同期します。切ると手だけを元に戻し、ほかの補正は続けます。");
            LegFKAfterIK = config.Bind(s, "LegFKAfterIK", false,
                "太もも・すねの回転（thighFK_rot・shinFK_rot）を、IK を解いた後に足します。脚の IK が働いている体位でも脚が動きますが、足が IK の目標から外れる（接地・接触がずれる）ことがあります。切ると IK を解く前に足し、脚の IK が働いている体位では IK が上書きします。");
            MoveUnlinkedLimbs = config.Bind(s, "MoveUnlinkedLimbs", false,
                "手足のずらし（hand_pos・elbow_pos・foot_pos・foot_rot・knee_pos）を、ゲームの IK がその手足を自分の目標に付けていない体位・状態でも効かせます。相手の体などに付いている手足は今の目標からずらし、IK の付いていない手足は今の位置からずらした目標に付けます。接触の位置がずれることがあります。手の付け替え（ik_hand）がある手は、そちらを優先します。");
            config.Bind(s, "ReloadTable", false, new ConfigDescription("内蔵の値と PoseForce.tsv を読み直します。", null,
                new ConfigurationManagerAttributes { CustomDrawer = entry => { if (GUILayout.Button("値の表を読み直す")) ReloadTable(); } }));
            config.Bind(s, "ImportExport", false, new ConfigDescription("import フォルダの kPlug の書き出し・共有データを取り込みます。自分で直した行と無効化の設定は残します。", null,
                new ConfigurationManagerAttributes { CustomDrawer = entry => { if (GUILayout.Button("書き出しの取り込み画面を開く…")) OpenImportDialog(); } }));
            BindShareConfig(config);
            ReloadTable();
        }

        private static void ReloadTable()
        {
            try
            {
                var next = PoseForceTable.Create(TablePath, message => PoseForcePlugin.Logger.LogWarning(Tag + " " + message));
                InstallTable(next);
            }
            catch (Exception ex)
            {
                PoseForcePlugin.Logger.LogWarning(Tag + " 値の表を読み直せません。前の表を使います: " + ex.Message);
            }
        }

        private static void InstallTable(PoseForceTable next)
        {
            ForgetEditor("表の読み直しまたは取り込み");
            ClearCorrections();
            HandBones.Clear();
            _table = next;
            SetActive(false, "値の表を読み直した");
            PoseForcePlugin.Logger.LogInfo(Tag + " 値の表を読み込みました（" + next.AssetCount + " アセット・" + next.RowCount + " 行）。利用者の表: " + TablePath);
            if (next.RowCount == 0)
                PoseForcePlugin.Logger.LogInfo(Tag + " 値の表が空です。KK の kPlugPoseForceExport で書き出した kPlugPose_*.tsv（またはほかの人の共有データ）を " + ImportDirectory
                    + " に置いて「書き出しの取り込み画面を開く…」から取り込むか、値の編集画面で体位ごとに値を足してください。");
        }

        internal static void TryApply(Harmony harmony)
        {
            try
            {
                _lstFemale = AccessTools.Field(typeof(HSceneProc), "lstFemale");
                _male = AccessTools.Field(typeof(HSceneProc), "male");
                _aaWeightsBody = AccessTools.Field(typeof(ChaControl), "aaWeightsBody");
                var change = AccessTools.Method(typeof(HSceneProc), "ChangeAnimator");
                var update = AccessTools.Method(typeof(HSceneProc), "Update");
                var destroy = AccessTools.Method(typeof(HSceneProc), "OnDestroy");
                var motion = AccessTools.Method(typeof(MotionIK), "Calc", new[] { typeof(string) });
                if (_lstFemale == null || _male == null || change == null || update == null || destroy == null || motion == null)
                {
                    PoseForcePlugin.Logger.LogWarning($"{Tag} HSceneProc の欄か処理が見つからないため、姿勢補正は使えません。");
                    _failed = true;
                    return;
                }
                harmony.Patch(change, prefix: new HarmonyMethod(typeof(PoseForce), nameof(ChangeAnimatorPrefix)), postfix: new HarmonyMethod(typeof(PoseForce), nameof(ChangeAnimatorPostfix)));
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(PoseForce), nameof(ProcUpdatePostfix)));
                harmony.Patch(destroy, prefix: new HarmonyMethod(typeof(PoseForce), nameof(ProcDestroyPrefix)));
                harmony.Patch(motion, prefix: new HarmonyMethod(typeof(PoseForce), nameof(MotionIKCalcPrefix)));
                PoseForcePlugin.Instance.gameObject.AddComponent<PoseForceInputBlocker>();
                PoseForcePlugin.Logger.LogInfo($"{Tag} HSceneProc に当てました（設定: {(Enabled.Value ? "有効" : "無効")}）。");
            }
            catch (Exception ex)
            {
                _failed = true;
                PoseForcePlugin.Logger.LogWarning($"{Tag} 当てられませんでした。姿勢補正は使えません: {ex.Message}");
            }
        }

        private static void ChangeAnimatorPostfix(HSceneProc __instance)
        {
            _changedFrame = Time.frameCount;
            _proc = __instance;
            PoseForceControls.Refresh(__instance);
        }
        private static void ProcUpdatePostfix(HSceneProc __instance) { _proc = __instance; }
        private static void ProcDestroyPrefix(HSceneProc __instance)
        {
            PoseForceControls.EndScene(__instance);
            if (!ReferenceEquals(_proc, __instance)) return;
            _proc = null;
            DetachAll("Hシーンを出た");
        }

        // ---- 毎フレーム（PoseForcePlugin.Update）: 購読の管理 ----

        internal static void OnUpdate()
        {
            if (!ModalOpen && !PoseForceControls.EditingText())
            {
                if (WindowKey != null && WindowKey.Value.IsDown()) OpenSimpleWindow(!_open);
                if (_editorWindowKey != null && _editorWindowKey.Value.IsDown()) OpenEditorWindow(!_editorOpen);
            }
            PoseForceControls.Tick(_proc, !_failed && Enabled.Value);
            ProcessImport();
            ProcessExport();
            EditorTick();
            if (_failed) return;
            try
            {
                bool inH = _proc != null && _proc.flags != null && !_proc.flags.isHSceneEnd;
                if (!Enabled.Value || !inH)
                {
                    if (!inH) _proc = null;
                    DetachAll(!inH ? "Hシーンを出た" : "機能を切った");
                    _reason = !inH ? "Hシーンの中で使えます" : "EnablePoseForce がオフです";
                    return;
                }
                var females = _lstFemale.GetValue(_proc) as List<ChaControl>;
                var male = _male.GetValue(_proc) as ChaControl;
                _female = Ensure(_female, females != null && females.Count > 0 ? females[0] : null, "female");
                _female2 = Ensure(_female2, _proc.flags.mode == HFlag.EMode.sonyu3P && females != null && females.Count > 1 ? females[1] : null, "female2");
                _maleRig = Ensure(_maleRig, male, "male");
                // 画面の表示用。補正そのものは IK の OnPreUpdate で決める。IK が止まっている人は OnLateUpdate で足す（0.1.4）
                string state, reason;
                if (!Wanted(out state, out reason)) { _reason = reason; ClearCorrections(); SetActive(false, reason); }
                else if (_female == null) _reason = "女の IK が見つからない";
                if (!UseHandIK.Value) ClearHands();
                // IK が止まっている人は、手の付け替えと手足のずらしを戻す（ゲームが IK を使わないので効かない）
                ReleaseIK(_female); ReleaseIK(_female2); ReleaseIK(_maleRig);
            }
            catch (Exception ex)
            {
                Fail("購読", ex);
            }
        }

        private static void ReleaseIK(Rig rig)
        {
            if (rig == null || rig.Ik.enabled) return;
            rig.ClearHands();
            rig.ClearLimbs();
        }

        private static bool NoIK(Rig rig) { return rig != null && !rig.Ik.enabled; }

        // ---- 毎フレーム（PoseForcePlugin.LateUpdate）: IK が止まっている人の補正（0.1.4） ----

        /// <summary>
        /// ゲームがその状態で IK を止めている（FullBodyBipedIK.enabled = false）と OnPreUpdate が呼ばれない。
        /// kPlug と同じく、アニメーションの後（LateUpdate）に、IK に関係しない欄（体の骨・太もも・すね）を足す。
        /// 手足の IK の目標の欄と手の付け替えは、IK が使われないので効かない。
        /// </summary>
        internal static void OnLateUpdate()
        {
            if (_failed || !Enabled.Value || _proc == null || _table == null) { _noIK = false; return; }
            if (!NoIK(_female) && !NoIK(_female2) && !NoIK(_maleRig)) { _noIK = false; return; }
            try
            {
                string state, reason;
                bool want = Wanted(out state, out reason);
                string asset = want ? _proc.flags.nowAnimationInfo?.paramFemale?.path?.file ?? "" : "";
                float f = Mathf.Clamp01(Force.Value);
                ApplyNoIK(_female, want, asset, state, f);
                ApplyNoIK(_female2, want, asset, state, f);
                ApplyNoIK(_maleRig, want, asset, state, f);
                if (NoIK(_female))
                {
                    _reason = want ? "" : reason;
                    SetActive(want, want ? asset + "・" + state + "・補正量 " + Force.Value.ToString("0.00") + "・女の IK が止まっている状態" : reason);
                    _noIK = want;
                    if (want && LogBones.Value && Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 1f; LogRigs(state); }
                }
                else _noIK = false;
            }
            catch (Exception ex)
            {
                Fail("IK の止まった状態の補正", ex);
            }
        }

        private static void ApplyNoIK(Rig rig, bool want, string asset, string state, float force)
        {
            if (!NoIK(rig)) return;
            if (!want) { rig.Clear(); return; }
            rig.Apply(_table.Get(asset, rig.Role, state), force, false);
        }

        private static Rig Ensure(Rig rig, ChaControl cha, string role)
        {
            FullBodyBipedIK ik = cha != null && cha.animBody != null ? cha.animBody.GetComponent<FullBodyBipedIK>() : null;
            if (rig != null && rig.Cha == cha && rig.Ik == ik && rig.Ik != null && rig.BonesAlive) return rig;
            // キャラの入れ替え・体の作り直し（骨や IK が新しくなる）・IK が無い
            if (rig != null) rig.Detach();
            _bodyFrame = -1;
            if (ik == null || ik.solver == null) return null;
            var r = new Rig(cha, ik, role);
            r.Attach();
            return r;
        }

        private static void DetachAll(string why)
        {
            if (_female == null && _female2 == null && _maleRig == null) return;
            if (_female != null) _female.Detach();
            if (_female2 != null) _female2.Detach();
            if (_maleRig != null) _maleRig.Detach();
            _female = null; _female2 = null; _maleRig = null;
            _bodyFrame = -1;
            HandBones.Clear();
            SetActive(false, why);
        }

        private static void ClearCorrections()
        {
            if (_female != null) _female.Clear();
            if (_female2 != null) _female2.Clear();
            if (_maleRig != null) _maleRig.Clear();
            _bodyFrame = -1;
        }

        private static void Fail(string where, Exception ex)
        {
            _failed = true;
            PoseForcePlugin.Logger.LogWarning($"{Tag} {where}で失敗したため、姿勢補正を止めます: {ex.Message}");
            try { DetachAll("失敗"); } catch { }
        }

        // ---- 今の補正 ----

        private static bool SupportedMode(HFlag.EMode mode) { return mode == HFlag.EMode.sonyu || mode == HFlag.EMode.sonyu3P; }

        private static bool HasSecondFemale()
        {
            if (_proc == null || _proc.flags == null || _proc.flags.mode != HFlag.EMode.sonyu3P || _lstFemale == null) return false;
            var females = _lstFemale.GetValue(_proc) as List<ChaControl>;
            return females != null && females.Count > 1 && females[1] != null;
        }

        private static ChaControl ActorCharacter(string role)
        {
            if (_proc == null || _proc.flags == null || _proc.flags.isHSceneEnd) return null;
            if (role == "male") return _male != null ? _male.GetValue(_proc) as ChaControl : null;
            if (role != "female" && (role != "female2" || _proc.flags.mode != HFlag.EMode.sonyu3P)) return null;
            var females = _lstFemale != null ? _lstFemale.GetValue(_proc) as List<ChaControl> : null;
            int index = role == "female" ? 0 : 1;
            return females != null && females.Count > index ? females[index] : null;
        }

        /// <summary>このフレームで補正するか。しないなら理由を返す。</summary>
        private static bool Wanted(out string state, out string reason)
        {
            state = "";
            HFlag flags = _proc != null ? _proc.flags : null;
            if (flags == null) { reason = "Hシーンの中で使えます"; return false; }
            state = flags.nowAnimStateName ?? "";
            string file = flags.nowAnimationInfo?.paramFemale?.path?.file ?? "";
            if (!SupportedMode(flags.mode)) { reason = "対応する挿入の種類ではない（" + flags.mode + "）"; return false; }
            if (_table == null) { reason = "値の表を読み込めない"; return false; }
            if (_table.AssetCount == 0) { reason = "値の表が空（kPlug の書き出しを取り込むか、編集画面で値を足す）"; return false; }
            if (!_table.HasAsset(file)) { reason = "この体位の値が無い（" + file + "）"; return false; }
            if (_table.IsDisabled(file)) { reason = "この体位は止めてある（" + file + "）"; return false; }
            if (Time.frameCount - _changedFrame <= ChangeFrames) { reason = "体位の切り替えの途中"; return false; }
            if (!HasActorRows(_female, file, state) && !HasActorRows(_female2, file, state) && !HasActorRows(_maleRig, file, state))
            { reason = "この状態・参加者の値が無い（" + state + "）"; return false; }
            if (Force.Value <= 0f) { reason = "補正量が 0"; return false; }
            reason = "";
            return true;
        }

        private static bool HasActorRows(Rig rig, string asset, string state)
        {
            return rig != null && _table.HasActorState(asset, rig.Role, state);
        }

        private static void PrepareBody(Rig rig, string asset, string state, float force)
        {
            if (rig == null) return;
            // IK が止まっている人も、ほかの人の手が付く骨のために先に足す（LateUpdate でも同じ値を足す。同じフレームに 2 回足しても 1 回分になる）
            rig.Apply(_table.Get(asset, rig.Role, state), force, rig.Ik.enabled);
        }

        private static void SetActive(bool active, string why)
        {
            if (active == _active) return;
            _active = active;
            PoseForcePlugin.Logger.LogInfo(active
                ? $"{Tag} 補正を始めました（{why}）。"
                : $"{Tag} 補正を止めました（{why}）。");
        }

        /// <summary>FullBodyBipedIK の solver.OnPreUpdate から。アニメーションの後、IK を解く直前。</summary>
        private static void PreSolve(Rig rig)
        {
            if (_failed || !Enabled.Value) { rig.Clear(); return; }
            try
            {
                string state, reason;
                bool want = Wanted(out state, out reason);
                _reason = want ? "" : reason;
                string asset = want ? _proc.flags.nowAnimationInfo?.paramFemale?.path?.file ?? "" : "";
                string status = want ? (asset + "・" + state + "・補正量 " + Force.Value.ToString("0.00")) : reason;
                if (rig == _female || _female == null)
                {
                    SetActive(want, status);
                    if (want && LogBones.Value && Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 1f; _logPending = true; }
                }
                if (!want) { rig.Clear(); return; }
                float f = Mathf.Clamp01(Force.Value);
                var rows = _table.Get(asset, rig.Role, state);
                if (_proc.flags.mode == HFlag.EMode.sonyu3P)
                {
                    // 手が参照する相手の体も、誰かの IK を解く前に準備する。
                    if (_bodyFrame != Time.frameCount)
                    {
                        _bodyFrame = Time.frameCount;
                        PrepareBody(_female, asset, state, f);
                        PrepareBody(_female2, asset, state, f);
                        PrepareBody(_maleRig, asset, state, f);
                    }
                }
                else rig.Apply(rows, f);
                rig.ApplyHands(rows, f);
                rig.ApplyLimbs(rows, f);
                // 骨のログは、男女の両方に足した後に出す（男がいなければ女の後）
                if (_logPending && (rig == _maleRig || _maleRig == null)) { _logPending = false; LogRigs(state); }
            }
            catch (Exception ex)
            {
                Fail("補正", ex);
            }
        }
        private static bool _logPending;

        private static void LogRigs(string state)
        {
            if (_female == null) return;
            var s = _female.Ik.solver;
            PoseForcePlugin.Logger.LogInfo($"{Tag}[骨] 状態 {state}・補正量 {Force.Value:0.00}。女: {_female.Hips.Describe("cf_j_hips")} / {_female.LegL.Describe("cf_pv_leg_L")} / {_female.KneeL.Describe("cf_pv_knee_L")}" +
                $"・足の IK の重み 左 {s.leftFootEffector.positionWeight:0.00}・右 {s.rightFootEffector.positionWeight:0.00}・ひざの曲げの重み 左 {s.leftLegChain.bendConstraint.weight:0.00}" +
                (_maleRig != null ? $"。男: {_maleRig.Hips.Describe("cf_j_hips")}" : "。男: なし") +
                (_female2 != null ? $"。女２: {_female2.Hips.Describe("cf_j_hips")}" : ""));
            PoseForcePlugin.Logger.LogInfo(Tag + "[手IK] 状態 " + state + " / " + _female.DescribeHands()
                + (_female2 != null ? " / " + _female2.DescribeHands() : "")
                + (_maleRig != null ? " / " + _maleRig.DescribeHands() : ""));
            // 0.1.4: 手足ごとのゲームの IK の付け先と重み（手足の IK の目標の欄が効くかの見分け）
            PoseForcePlugin.Logger.LogInfo(Tag + "[手足] 状態 " + state + "・LegFKAfterIK=" + LegFKAfterIK.Value + "・MoveUnlinkedLimbs=" + MoveUnlinkedLimbs.Value
                + " / " + _female.DescribeLimbs()
                + (_female2 != null ? " / " + _female2.DescribeLimbs() : "")
                + (_maleRig != null ? " / " + _maleRig.DescribeLimbs() : ""));
        }

    }

    /// <summary>ConfigurationManager が名前で読み取る設定の表示の指定（使う欄だけ）。</summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public System.Action<ConfigEntryBase> CustomDrawer;
    }

    /// <summary>
    /// 調整の画面の上にマウスがある間、ゲームの入力を止める（0.16.1）。0.16.0 は画面のスライダーをドラッグしてもゲームのカメラ操作に取られ、
    /// 押した 1 フレームだけ値が変わって戻っていた（ログ 20261106_1155: Force が 0 → 0.005 → 0 を繰り返した）。
    /// KK_SPJGamePatcher の調整画面と同じく、ゲームより先の Update で入力の軸を戻し（Input.ResetInputAxes）、
    /// 画面と同じ大きさの透明なキャンバスで、下の Hシーンの UI へのクリックを止める。
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    internal sealed class PoseForceInputBlocker : MonoBehaviour
    {
        private static GameObject _blocker;
        private static RectTransform _blockRect;
        private static RectTransform _otherBlockRect;

        private void Update()
        {
            PoseForceControls.CaptureInput();
            if (!PoseForce.WindowOpen) return;
            if (PoseForce.ModalOpen || GUIUtility.keyboardControl != 0 || PoseForce.PointerOverWindow(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))) Input.ResetInputAxes();
        }

        internal static void Place(Rect? rect, Rect? other = null)
        {
            if (rect == null)
            {
                if (_blocker != null) _blocker.SetActive(false);
                return;
            }
            if (_blocker == null)
            {
                _blocker = new GameObject("ksPlugPoseForce_Input", typeof(Canvas), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(_blocker);
                var canvas = _blocker.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32760;
                var panel = new GameObject("Window", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(_blocker.transform, false);
                panel.GetComponent<Image>().color = Color.clear;
                _blockRect = panel.GetComponent<RectTransform>();
                _blockRect.anchorMin = _blockRect.anchorMax = new Vector2(0, 1);
                _blockRect.pivot = new Vector2(0, 1);
                var otherPanel = new GameObject("OtherWindow", typeof(RectTransform), typeof(Image));
                otherPanel.transform.SetParent(_blocker.transform, false);
                otherPanel.GetComponent<Image>().color = Color.clear;
                _otherBlockRect = otherPanel.GetComponent<RectTransform>();
                _otherBlockRect.anchorMin = _otherBlockRect.anchorMax = new Vector2(0, 1);
                _otherBlockRect.pivot = new Vector2(0, 1);
            }
            if (!_blocker.activeSelf) _blocker.SetActive(true);
            Rect r = rect.Value;
            _blockRect.anchoredPosition = new Vector2(r.x, -r.y);
            _blockRect.sizeDelta = new Vector2(r.width, r.height);
            _otherBlockRect.gameObject.SetActive(other.HasValue);
            if (other.HasValue)
            {
                Rect second = other.Value;
                _otherBlockRect.anchoredPosition = new Vector2(second.x, -second.y);
                _otherBlockRect.sizeDelta = new Vector2(second.width, second.height);
            }
        }
    }
}
