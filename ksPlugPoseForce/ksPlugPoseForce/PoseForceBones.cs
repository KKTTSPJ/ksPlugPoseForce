using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

namespace ksPlugPoseForce
{
    internal static partial class PoseForce
    {
        private static Transform BodyBone(ChaControl cha, string name)
        {
            try
            {
                var dict = (_aaWeightsBody?.GetValue(cha) as AssignedAnotherWeights)?.dictBone;
                GameObject go;
                if (dict != null && dict.TryGetValue(name, out go) && go != null) return go.transform;
            }
            catch (System.Exception) { }
            Transform root = cha.objBodyBone != null ? cha.objBodyBone.transform : null;
            return root != null ? Find(root, name) : null;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; ++i)
            {
                Transform found = Find(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        // 位置と回転を別々に所有し、他の処理で上書きされた値は復元しない。
        private sealed class Slot
        {
            internal readonly Transform T;
            private bool _hasPosition, _hasRotation, _usePosition, _useRotation, _eulerThisFrame, _multiplyThisFrame;
            private Vector3 _basePos, _writtenPos, _position, _euler;
            private Quaternion _baseRot, _writtenRot, _multiply = Quaternion.identity;
            internal int Frames, Kept;

            internal Slot(Transform t) { T = t; }
            internal void Begin() { _position = _euler = Vector3.zero; _multiply = Quaternion.identity; _eulerThisFrame = _multiplyThisFrame = false; }

            internal void Add(PoseForceFields.Operation mode, Vector3 value)
            {
                if (mode == PoseForceFields.Operation.Position) { _position += value; _usePosition = true; }
                else if (mode == PoseForceFields.Operation.Euler) { _euler += value; _useRotation = _eulerThisFrame = true; }
                else { _multiply *= Quaternion.Euler(value); _useRotation = _multiplyThisFrame = true; }
            }

            internal void Apply()
            {
                if (T == null) return;
                if (_usePosition)
                {
                    Vector3 current = T.localPosition;
                    bool kept = _hasPosition && current == _writtenPos;
                    Vector3 basis = kept ? _basePos : current;
                    ++Frames; if (kept) ++Kept;
                    T.localPosition = basis + _position;
                    _basePos = basis; _writtenPos = T.localPosition; _hasPosition = true;
                }
                if (_useRotation)
                {
                    Quaternion current = T.localRotation;
                    bool kept = _hasRotation && current == _writtenRot;
                    Quaternion basis = kept ? _baseRot : current;
                    if (!_usePosition) { ++Frames; if (kept) ++Kept; }
                    T.localRotation = basis;
                    if (_eulerThisFrame) T.localEulerAngles += _euler;
                    if (_multiplyThisFrame) T.localRotation *= _multiply;
                    _baseRot = basis; _writtenRot = T.localRotation; _hasRotation = true;
                }
            }

            internal void Clear()
            {
                if (T != null)
                {
                    if (_hasPosition && T.localPosition == _writtenPos) T.localPosition = _basePos;
                    if (_hasRotation && T.localRotation == _writtenRot) T.localRotation = _baseRot;
                }
                _hasPosition = _hasRotation = _usePosition = _useRotation = false;
                Begin();
            }

            internal string Describe(string name)
            {
                if (T == null) return name + " なし";
                Vector3 basis = _hasPosition ? _basePos : T.localPosition;
                string result = $"{name} {T.localPosition.x:0.000},{T.localPosition.y:0.000},{T.localPosition.z:0.000}（元 {basis.x:0.000},{basis.y:0.000},{basis.z:0.000}、上書きされない {Kept}/{Frames}）";
                Frames = Kept = 0;
                return result;
            }
        }

        private sealed class Rig
        {
            internal readonly ChaControl Cha;
            internal readonly FullBodyBipedIK Ik;
            internal readonly string Role;
            internal string Label { get { return PoseForceActors.Label(Role); } }
            internal readonly Slot Hips, Waist, Spine1, Spine2, LegL, LegR, KneeL, KneeR;
            private readonly Dictionary<string, Slot> _bones = new Dictionary<string, Slot>(System.StringComparer.Ordinal);
            private readonly HashSet<string> _missing = new HashSet<string>(System.StringComparer.Ordinal);
            private IKSolver.UpdateDelegate _handler, _postHandler;
            private bool _logHandAfter;
            private float _nextHandLog;
            private IKSolver _solver;
            private readonly PoseForceHandIK _leftHand, _rightHand;
            private readonly PoseForceNativeHandIK _nativeLeft, _nativeRight;
            private readonly HashSet<string> _missingTargets = new HashSet<string>(System.StringComparer.Ordinal);
            // 0.1.4: 手足の IK の目標（MoveUnlinkedLimbs）と、IK の後に足す太もも・すね（LegFKAfterIK）
            internal readonly PoseForceLimbIK[] Limbs;
            private readonly Dictionary<string, Slot> _postBones = new Dictionary<string, Slot>(System.StringComparer.Ordinal);
            private bool _postUsed;
            private IList<PoseForceRow> _lastRows;
            private float _lastForce;
            private int _lastFrame = -1;

            internal Rig(ChaControl cha, FullBodyBipedIK ik, string role)
            {
                Cha = cha; Ik = ik; Role = role;
                Hips = Bone("cf_j_hips"); Waist = Bone("cf_j_waist01");
                Spine1 = Bone("cf_j_spine01"); Spine2 = Bone("cf_j_spine02");
                LegL = Bone("cf_pv_leg_L"); LegR = Bone("cf_pv_leg_R");
                KneeL = Bone("cf_pv_knee_L"); KneeR = Bone("cf_pv_knee_R");
                _leftHand = new PoseForceHandIK(ik.solver.leftHandEffector, Label + "の左手");
                _rightHand = new PoseForceHandIK(ik.solver.rightHandEffector, Label + "の右手");
                _nativeLeft = new PoseForceNativeHandIK(ik.solver.leftHandEffector, Label + "の左手");
                _nativeRight = new PoseForceNativeHandIK(ik.solver.rightHandEffector, Label + "の右手");
                var s = ik.solver;
                Limbs = new[]
                {
                    new PoseForceLimbIK(s.leftHandEffector, s.leftArmChain, true, "L", Label + "の左手"),
                    new PoseForceLimbIK(s.rightHandEffector, s.rightArmChain, true, "R", Label + "の右手"),
                    new PoseForceLimbIK(s.leftFootEffector, s.leftLegChain, false, "L", Label + "の左足"),
                    new PoseForceLimbIK(s.rightFootEffector, s.rightLegChain, false, "R", Label + "の右足"),
                };
            }

            /// <summary>表の手足の欄の骨（cf_pv_*）。見つからなければ null。</summary>
            internal Transform PvBone(string name) { return Bone(name).T; }

            private Slot Bone(string name)
            {
                Slot slot;
                if (!_bones.TryGetValue(name, out slot)) { slot = new Slot(BodyBone(Cha, name)); _bones.Add(name, slot); }
                return slot;
            }

            internal bool BonesAlive { get { return Hips.T != null && Waist.T != null; } }

            /// <summary>
            /// 骨に足す。ikRunning: IK を解く直前（OnPreUpdate）なら true。
            /// false は IK が止まっている状態（LateUpdate）で、手足の IK の目標の欄は効かないので足さない（kPlug と同じく体の骨・脚の FK は足す）。
            /// IK を解く直前で LegFKAfterIK が有効なら、太もも・すねは IK の後（PostSolve）に足す。
            /// </summary>
            internal void Apply(IList<PoseForceRow> rows, float force, bool ikRunning = true)
            {
                if (ikRunning) { _lastRows = rows; _lastForce = force; _lastFrame = Time.frameCount; }
                bool legAfter = ikRunning && LegFKAfterIK.Value;
                foreach (var slot in _bones.Values) slot.Begin();
                foreach (var row in rows)
                {
                    var kind = PoseForceFields.KindOf(row.Field);
                    if (kind == PoseForceFields.Kind.Hand) continue;
                    if (!ikRunning && kind == PoseForceFields.Kind.IKTarget) continue;
                    if (legAfter && kind == PoseForceFields.Kind.LegFK) continue;
                    var bindings = PoseForceFields.Get(row.Field);
                    if (bindings == null) continue;
                    foreach (var binding in bindings)
                    {
                        var slot = Bone(binding.Bone);
                        if (slot.T == null && _missing.Add(binding.Bone))
                            PoseForcePlugin.Logger.LogWarning("[PoseForce] " + Label + "の骨が見つからないため、この欄を使いません: " + row.Field + " / " + binding.Bone);
                        slot.Add(binding.Mode, new Vector3(row.X, binding.OnlyX ? 0f : row.Y, binding.OnlyX ? 0f : row.Z) * force);
                    }
                }
                foreach (var slot in _bones.Values) slot.Apply();
            }

            internal void Attach()
            {
                _solver = Ik.solver;
                _handler = () => PreSolve(this);
                _solver.OnPreUpdate += _handler;
                _postHandler = PostSolve;
                _solver.OnPostUpdate += _postHandler;
                var missing = new List<string>();
                foreach (var pair in _bones) if (pair.Value.T == null) missing.Add(pair.Key);
                PoseForcePlugin.Logger.LogInfo("[PoseForce] " + Label + "（" + Name(Cha) + "）の IK を購読しました"
                    + (missing.Count == 0 ? "。" : "（見つからない骨: " + string.Join(", ", missing.ToArray()) + "）。"));
            }

            internal void ApplyHands(IList<PoseForceRow> rows, float force)
            {
                _logHandAfter = LogBones.Value && Time.unscaledTime >= _nextHandLog;
                if (_logHandAfter) _nextHandLog = Time.unscaledTime + 1f;
                if (!UseHandIK.Value) { ClearHands(); return; }
                PoseForceRow left = null, right = null;
                foreach (var row in rows)
                {
                    if (row.Field == "ik_hand_L") left = row;
                    else if (row.Field == "ik_hand_R") right = row;
                }
                var partner = Role == "male" ? _female : _maleRig;
                var other = Role == "female2" ? _female : _female2;
                Transform partnerRoot = partner != null && partner.Cha != null ? partner.Cha.transform : null;
                Transform otherRoot = other != null && other.Cha != null ? other.Cha.transform : null;
                Transform bodyRoot = Cha.objBodyBone != null ? Cha.objBodyBone.transform : null;
                if (left != null) _nativeLeft.Clear();
                _leftHand.Apply(left, HandTarget(left), force);
                if (left == null) _nativeLeft.Apply(partnerRoot, otherRoot, bodyRoot);
                if (right != null) _nativeRight.Clear();
                _rightHand.Apply(right, HandTarget(right), force);
                if (right == null) _nativeRight.Apply(partnerRoot, otherRoot, bodyRoot);
            }

            private Transform HandTarget(PoseForceRow row)
            {
                if (row == null) return null;
                var target = ResolveHandTarget(row.Target);
                if (target == null && _missingTargets.Add(row.Target))
                    PoseForcePlugin.Logger.LogWarning("[PoseForce][手] 付け先が見つからないため、この手を補正しません: " + row.Target);
                return target;
            }

            internal void ClearHands() { _leftHand.Clear(); _rightHand.Clear(); _nativeLeft.Clear(); _nativeRight.Clear(); }

            internal string DescribeHands()
            {
                return Label + " IK全体=" + Ik.solver.IKPositionWeight.ToString("0.000") + " / " + _nativeLeft.Describe() + " / " + _nativeRight.Describe();
            }

            /// <summary>
            /// MoveUnlinkedLimbs: ゲームの IK が自分の目標（cf_pv_*）に付けていない手足の hand_pos・elbow_pos・foot_pos・foot_rot・knee_pos を、
            /// 今の IK の目標をずらして効かせる（IK の後に戻す）。手の付け替え（ik_hand）の行がある手はそちらを優先する。
            /// </summary>
            internal void ApplyLimbs(IList<PoseForceRow> rows, float force)
            {
                bool on = MoveUnlinkedLimbs.Value;
                foreach (var limb in Limbs)
                {
                    if (!on) { limb.Idle(); continue; }
                    PoseForceRow pos = null, rot = null, bend = null, hand = null;
                    string handField = limb.Arm ? "ik_hand_" + limb.Side : null;
                    foreach (var row in rows)
                    {
                        if (row.Field == limb.PosField) pos = row;
                        else if (limb.RotField != null && row.Field == limb.RotField) rot = row;
                        else if (row.Field == limb.BendField) bend = row;
                        else if (handField != null && row.Field == handField) hand = row;
                    }
                    if (hand != null && UseHandIK.Value) { limb.Idle(); continue; }
                    var e = limb.Effector;
                    Transform pvTarget = PvBone(limb.PvTarget), pvBend = PvBone(limb.PvBend);
                    bool followTarget = PoseForceLimbIK.Follows(e.target, pvTarget, e.positionWeight);
                    bool followBend = limb.Bend != null && PoseForceLimbIK.Follows(limb.Bend.bendGoal, pvBend, limb.Bend.weight);
                    Vector3? delta = null, bendDelta = null;
                    Quaternion? q = null;
                    // 目標を追っている手足は、ふだんどおり cf_pv_* に足した値で動くので、ここでは何もしない。
                    if (!followTarget)
                    {
                        if (pos != null) delta = Space(pvTarget).TransformVector(new Vector3(pos.X, pos.Y, pos.Z) * force);
                        if (rot != null) q = Quaternion.Euler(new Vector3(rot.X, rot.Y, rot.Z) * force);
                    }
                    if (!followBend && bend != null) bendDelta = Space(pvBend).TransformVector(new Vector3(bend.X, bend.Y, bend.Z) * force);
                    if (delta == null && q == null && bendDelta == null) { limb.Idle(); continue; }
                    limb.Apply(delta, q, bendDelta);
                }
            }

            private Transform Space(Transform pv)
            {
                if (pv != null && pv.parent != null) return pv.parent;
                return Cha.objBodyBone != null ? Cha.objBodyBone.transform : Cha.transform;
            }

            internal void ClearLimbs() { foreach (var limb in Limbs) limb.Clear(); }

            /// <summary>IK を解いた後: 手足の目標を戻し、LegFKAfterIK の太もも・すねを足す。</summary>
            private void PostSolve()
            {
                try
                {
                    foreach (var limb in Limbs) limb.Restore();
                    if (LegFKAfterIK.Value && _lastFrame == Time.frameCount && _lastRows != null)
                    {
                        foreach (var slot in _postBones.Values) slot.Begin();
                        foreach (var row in _lastRows)
                        {
                            if (PoseForceFields.KindOf(row.Field) != PoseForceFields.Kind.LegFK) continue;
                            var bindings = PoseForceFields.Get(row.Field);
                            if (bindings == null) continue;
                            foreach (var binding in bindings)
                            {
                                Slot slot;
                                if (!_postBones.TryGetValue(binding.Bone, out slot)) { slot = new Slot(BodyBone(Cha, binding.Bone)); _postBones.Add(binding.Bone, slot); }
                                slot.Add(binding.Mode, new Vector3(row.X, row.Y, row.Z) * _lastForce);
                            }
                        }
                        foreach (var slot in _postBones.Values) slot.Apply();
                        _postUsed = true;
                    }
                    else if (_postUsed) ClearPost();
                }
                catch (System.Exception ex) { Fail("IK の後の補正", ex); return; }
                LogHandsAfterSolve();
            }

            private void ClearPost()
            {
                foreach (var slot in _postBones.Values) slot.Clear();
                _postUsed = false;
            }

            /// <summary>
            /// 編集画面の表示用: 今の状態でこの欄が効いているか。
            /// rows はこの人・状態の行（手の付け替えの有無を見る）。
            /// </summary>
            internal string FieldStatus(string field, IList<PoseForceRow> rows)
            {
                bool ik = Ik.enabled;
                switch (PoseForceFields.KindOf(field))
                {
                    case PoseForceFields.Kind.Body:
                        return ik ? "効く" : "効く（IK が止まっている状態。IK に関係しない欄として足す）";
                    case PoseForceFields.Kind.Hand:
                        if (!UseHandIK.Value) return "効かない：UseHandIK がオフ";
                        return ik ? "効く（手の IK の付け先を替える）" : "効かない：この状態ではゲームが IK を止めている";
                    case PoseForceFields.Kind.LegFK:
                        {
                            if (!ik) return "効く（IK が止まっている状態）";
                            if (LegFKAfterIK.Value) return "効く（LegFKAfterIK: IK の後に足す。足が IK の目標から外れることがある）";
                            var leg = Limbs[field.StartsWith("L_", System.StringComparison.Ordinal) ? 2 : 3];
                            bool pinned = leg.Effector.positionWeight > 0f || (leg.Bend != null && leg.Bend.weight > 0f);
                            return pinned ? "効かない見込み：脚の IK が働いていて、IK が上書きする（LegFKAfterIK で IK の後に足せる）"
                                : "効く見込み（脚の IK の重みが 0）";
                        }
                    default:
                        {
                            if (!ik) return "効かない：この状態ではゲームが IK を止めている";
                            PoseForceLimbIK limb = null;
                            foreach (var l in Limbs)
                                if (field == l.PosField || field == l.RotField || field == l.BendField) { limb = l; break; }
                            if (limb == null) return "";
                            bool bend = field == limb.BendField;
                            if (limb.Arm && UseHandIK.Value)
                                foreach (var row in rows) if (row.Field == "ik_hand_" + limb.Side) return "効かない：同じ手の手の付け替え（ik_hand）が優先";
                            Transform target = bend ? (limb.Bend != null ? limb.Bend.bendGoal : null) : limb.Effector.target;
                            float weight = bend ? (limb.Bend != null ? limb.Bend.weight : 0f) : limb.Effector.positionWeight;
                            string pv = bend ? limb.PvBend : limb.PvTarget;
                            if (PoseForceLimbIK.Follows(target, PvBone(pv), weight)) return "効く（ゲームの IK が " + pv + " に付けている）";
                            string link = "付け先 " + PoseForceLimbIK.LinkName(target) + "・重み " + weight.ToString("0.00");
                            if (MoveUnlinkedLimbs.Value) return "効く（MoveUnlinkedLimbs: 今の IK の目標をずらす。" + link + "）";
                            return "効かない：ゲームの IK がこの手足を " + pv + " に付けていない（" + link + "）。MoveUnlinkedLimbs で動かせる";
                        }
                }
            }

            internal string DescribeLimbs()
            {
                var parts = new List<string>();
                foreach (var limb in Limbs) parts.Add(limb.Describe());
                return Label + (Ik.enabled ? "" : "（IK 停止）") + " " + string.Join(" / ", parts.ToArray());
            }

            private void LogHandsAfterSolve()
            {
                if (!_logHandAfter) return;
                _logHandAfter = false;
                try
                {
                    if (!LogBones.Value) return;
                    string state, reason;
                    if (!Wanted(out state, out reason)) return;
                    string asset = _proc.flags.nowAnimationInfo?.paramFemale?.path?.file ?? "";
                    PoseForcePlugin.Logger.LogInfo("[PoseForce][手IK解後] " + asset + "・" + state + "・UseHandIK=" + UseHandIK.Value + " / " + DescribeHands());
                }
                catch (System.Exception ex) { Fail("手の診断", ex); }
            }

            internal void Detach()
            {
                try { if (_solver != null && _handler != null) _solver.OnPreUpdate -= _handler; } catch { }
                try { if (_solver != null && _postHandler != null) _solver.OnPostUpdate -= _postHandler; } catch { }
                _solver = null; _handler = _postHandler = null;
                Clear();
            }

            internal void Clear()
            {
                _logHandAfter = false; _lastFrame = -1;
                ClearHands(); ClearLimbs(); ClearPost();
                foreach (var slot in _bones.Values) slot.Clear();
            }
        }

        private sealed class HandBone
        {
            internal ChaControl Owner;
            internal Transform Root, Bone;
        }

        private static readonly Dictionary<string, HandBone> HandBones = new Dictionary<string, HandBone>(System.StringComparer.Ordinal);

        private static Transform ResolveHandTarget(string key)
        {
            int colon = key.IndexOf(':');
            if (colon < 1 || colon == key.Length - 1) return null;
            string role = key.Substring(0, colon), name = key.Substring(colon + 1);
            var cha = ActorCharacter(role);
            if (cha == null) return null;
            Transform root = cha.objBodyBone != null ? cha.objBodyBone.transform : cha.transform;
            HandBone cached;
            if (HandBones.TryGetValue(key, out cached) && cached.Owner == cha && cached.Root == root
                && (ReferenceEquals(cached.Bone, null) || cached.Bone != null)) return cached.Bone;
            Transform bone = BodyBone(cha, name);
            if (bone == null) bone = Find(cha.transform, name);
            HandBones[key] = new HandBone { Owner = cha, Root = root, Bone = bone };
            return bone;
        }

        private static void ClearHands()
        {
            if (_female != null) _female.ClearHands();
            if (_female2 != null) _female2.ClearHands();
            if (_maleRig != null) _maleRig.ClearHands();
        }

        private static void MotionIKCalcPrefix(MotionIK __instance)
        {
            // ゲームが手足の目標・重みを決め直す前に、こちらの付け替え・ずらしを戻す
            if (_female != null && _female.Ik == __instance.ik) { _female.ClearHands(); _female.ClearLimbs(); }
            if (_female2 != null && _female2.Ik == __instance.ik) { _female2.ClearHands(); _female2.ClearLimbs(); }
            if (_maleRig != null && _maleRig.Ik == __instance.ik) { _maleRig.ClearHands(); _maleRig.ClearLimbs(); }
        }

        private static void ChangeAnimatorPrefix()
        {
            ClearHands();
            if (_female != null) _female.ClearLimbs();
            if (_female2 != null) _female2.ClearLimbs();
            if (_maleRig != null) _maleRig.ClearLimbs();
            _bodyFrame = -1;
        }

        private static string Name(ChaControl cha)
        {
            try { return cha != null ? cha.chaFile.parameter.fullname : "?"; } catch { return "?"; }
        }
    }
}
