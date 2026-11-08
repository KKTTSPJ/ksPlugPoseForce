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
            }

            private Slot Bone(string name)
            {
                Slot slot;
                if (!_bones.TryGetValue(name, out slot)) { slot = new Slot(BodyBone(Cha, name)); _bones.Add(name, slot); }
                return slot;
            }

            internal bool BonesAlive { get { return Hips.T != null && Waist.T != null; } }

            internal void Apply(IList<PoseForceRow> rows, float force)
            {
                foreach (var slot in _bones.Values) slot.Begin();
                foreach (var row in rows)
                {
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
                _postHandler = LogHandsAfterSolve;
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

            internal void Clear() { _logHandAfter = false; ClearHands(); foreach (var slot in _bones.Values) slot.Clear(); }
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
            if (_female != null && _female.Ik == __instance.ik) _female.ClearHands();
            if (_female2 != null && _female2.Ik == __instance.ik) _female2.ClearHands();
            if (_maleRig != null && _maleRig.Ik == __instance.ik) _maleRig.ClearHands();
        }

        private static void ChangeAnimatorPrefix() { ClearHands(); _bodyFrame = -1; }

        private static string Name(ChaControl cha)
        {
            try { return cha != null ? cha.chaFile.parameter.fullname : "?"; } catch { return "?"; }
        }
    }
}
