using Illusion.Component.Correct;
using RootMotion.FinalIK;
using UnityEngine;

namespace ksPlugPoseForce
{
    /// <summary>
    /// 手足 1 本の IK の目標の扱い（0.1.4、設定 MoveUnlinkedLimbs）。
    /// 表の hand_pos・elbow_pos・foot_pos・foot_rot・knee_pos は、ふだんは自分の IK の目標（cf_pv_*）に足す。
    /// ゲームの IK がこの手足を自分の目標に付けていないとき（相手の骨に付けている・重み 0）は、それでは動かない。
    /// そのときは IK を解く直前に、今の目標の位置（重み 0 なら今の手足の位置）にずらしを足した仮の目標へ差し替え、
    /// IK を解いた後に元の目標・重みへ戻す。元の目標そのもの（相手の骨のことがある）は動かさない。
    /// ゲームの処理には、毎フレーム元の目標・重みが見える。
    /// </summary>
    internal sealed class PoseForceLimbIK
    {
        internal readonly string Label;
        internal readonly bool Arm;
        internal readonly string Side;
        private readonly IKEffector _effector;
        private readonly IKConstraintBend _bend;
        private readonly Transform _joint;

        private Transform _ownTarget, _ownGoal;
        private bool _pending;
        // 仮の目標に替えた手足（元に戻す値）
        private bool _swappedTarget;
        private Transform _origTarget;
        private float _origPosWeight, _origRotWeight;
        // 仮の曲げの目標に替えたひじ・ひざ
        private bool _swappedGoal;
        private Transform _origGoal;
        private float _origBendWeight;
        // 差し替えた先（戻すときに、ほかの処理が替えていないかを見る）
        private Transform _placedTarget, _placedGoal;
        /// <summary>診断の表示用。ForceFootIK で付け替えた・外したとき。</summary>
        private string _mode;

        internal bool Active { get; private set; }

        internal PoseForceLimbIK(IKEffector effector, FBIKChain chain, bool arm, string side, string label)
        {
            _effector = effector;
            _bend = chain != null ? chain.bendConstraint : null;
            _joint = chain != null && chain.nodes != null && chain.nodes.Length > 1 ? chain.nodes[1].transform : null;
            Arm = arm; Side = side; Label = label;
        }

        internal string PosField { get { return Side + (Arm ? "_hand_pos" : "_foot_pos"); } }
        internal string RotField { get { return Arm ? null : Side + "_foot_rot"; } }
        internal string BendField { get { return Side + (Arm ? "_elbow_pos" : "_knee_pos"); } }
        internal string PvTarget { get { return (Arm ? "cf_pv_hand_" : "cf_pv_leg_") + Side; } }
        internal string PvBend { get { return (Arm ? "cf_pv_elbo_" : "cf_pv_knee_") + Side; } }
        internal IKEffector Effector { get { return _effector; } }
        internal IKConstraintBend Bend { get { return _bend; } }

        /// <summary>今の目標が自分の cf_pv_* を追っていて、重みがあるか。</summary>
        internal static bool Follows(Transform target, Transform pv, float weight)
        {
            if (weight <= 0f || target == null || pv == null) return false;
            if (target == pv) return true;
            var data = target.GetComponent<BaseData>();
            return data != null && data.bone == pv;
        }

        internal static string LinkName(Transform target)
        {
            if (target == null) return "なし";
            var data = target.GetComponent<BaseData>();
            if (data != null) return data.bone != null ? data.bone.name : target.name + "（付け先なし）";
            return target.name;
        }

        /// <summary>
        /// IK を解く直前。delta・bendDelta はワールドのずらし、rot は目標の向きに掛ける回転。使わない物は null。
        /// 0.1.6 ForceFootIK: forceTarget は足の効き手をこの骨（自分の cf_pv_leg）に付け替えて重み 1（回転も 1）、
        /// release は足の効き手の重みを 0 にする（太もも・すねの FK を効かせる。目標は替えない）、
        /// forceGoal は曲げの目標をこの骨（自分の cf_pv_elbo・cf_pv_knee）に付け替えて重み 1。どれも IK の後（Restore）で戻す。
        /// </summary>
        internal void Apply(Vector3? delta, Quaternion? rot, Vector3? bendDelta, Transform forceTarget = null, bool release = false, Transform forceGoal = null)
        {
            Restore();
            if (release) ReleaseTarget();
            else if (forceTarget != null) SetTarget(forceTarget);
            else if (delta.HasValue || rot.HasValue) SwapTarget(delta ?? Vector3.zero, rot);
            if (forceGoal != null) SetGoal(forceGoal);
            else if (bendDelta.HasValue) SwapGoal(bendDelta.Value);
            Active = _pending = _swappedTarget || _swappedGoal;
            // 診断の表示用。ログは IK を解いて戻した後に出るので、次の Apply・Idle まで残す
            _mode = !Active ? null : release ? "ForceFootIK・足の IK を外す" : forceTarget != null ? "ForceFootIK"
                : forceGoal != null ? "ForceFootIK・曲げ" : null;
        }

        private void KeepTarget()
        {
            _origTarget = _effector.target;
            _origPosWeight = _effector.positionWeight;
            _origRotWeight = _effector.rotationWeight;
            _swappedTarget = true;
        }

        private void SetTarget(Transform pv)
        {
            KeepTarget();
            _effector.target = _placedTarget = pv;
            _effector.positionWeight = 1f;
            _effector.rotationWeight = 1f;
        }

        private void ReleaseTarget()
        {
            KeepTarget();
            _placedTarget = _effector.target;
            _effector.positionWeight = 0f;
            _effector.rotationWeight = 0f;
        }

        private void SetGoal(Transform pv)
        {
            if (_bend == null) return;
            _origGoal = _bend.bendGoal;
            _origBendWeight = _bend.weight;
            _bend.bendGoal = _placedGoal = pv;
            _bend.weight = 1f;
            _swappedGoal = true;
        }

        private void SwapTarget(Vector3 delta, Quaternion? rot)
        {
            if (_effector.bone == null) return;
            Transform target = _effector.target;
            bool linked = _effector.positionWeight > 0f && target != null;
            // 付いている手足は今の目標から、付いていない手足は今の位置からずらす
            Vector3 basePos = linked ? target.position : _effector.bone.position;
            bool rotLinked = _effector.rotationWeight > 0f && target != null;
            Quaternion baseRot = rotLinked ? target.rotation : _effector.bone.rotation;
            if (_ownTarget == null)
            {
                var go = new GameObject("ksPlugPoseForce_Limb_" + Label) { hideFlags = HideFlags.HideAndDontSave };
                _ownTarget = go.transform;
            }
            _ownTarget.SetPositionAndRotation(basePos + delta, rot.HasValue ? baseRot * rot.Value : baseRot);
            KeepTarget();
            _effector.target = _placedTarget = _ownTarget;
            if (!linked) _effector.positionWeight = 1f;
            if (rot.HasValue && !rotLinked) _effector.rotationWeight = 1f;
        }

        private void SwapGoal(Vector3 delta)
        {
            if (_bend == null) return;
            Transform goal = _bend.bendGoal;
            bool linked = _bend.weight > 0f && goal != null;
            if (!linked && _joint == null) return;
            // 曲げの目標が無いときは、今のひじ・ひざの位置にずらしを足す（今の曲げの向きからずらす）
            Vector3 basePos = linked ? goal.position : _joint.position;
            if (_ownGoal == null)
            {
                var go = new GameObject("ksPlugPoseForce_Bend_" + Label) { hideFlags = HideFlags.HideAndDontSave };
                _ownGoal = go.transform;
            }
            _ownGoal.position = basePos + delta;
            _origGoal = goal;
            _origBendWeight = _bend.weight;
            _bend.bendGoal = _placedGoal = _ownGoal;
            if (!linked) _bend.weight = 1f;
            _swappedGoal = true;
        }

        /// <summary>IK を解いた後（と、止めるとき）。ほかの処理が目標を替えていなければ元に戻す。</summary>
        internal void Restore()
        {
            if (!_pending) return;
            _pending = false;
            if (_swappedTarget)
            {
                if (_effector.target == _placedTarget)
                {
                    _effector.target = _origTarget;
                    _effector.positionWeight = _origPosWeight;
                    _effector.rotationWeight = _origRotWeight;
                }
                _swappedTarget = false;
                _origTarget = _placedTarget = null;
            }
            if (_swappedGoal && _bend != null)
            {
                if (_bend.bendGoal == _placedGoal)
                {
                    _bend.bendGoal = _origGoal;
                    _bend.weight = _origBendWeight;
                }
                _swappedGoal = false;
                _origGoal = _placedGoal = null;
            }
        }

        /// <summary>
        /// 0.1.5: ゲームが IK を止めている人の手足を動かすとき（Rig.SolveStopped）。
        /// 今の手足の位置にずらしを足した仮の目標に重み 1 で付け、曲げは今のひじ・ひざの位置（＋ずらし）へ向ける。
        /// 元の目標・重みは Rig.SolveStopped がまとめて控えて戻すので、ここでは控えない。
        /// </summary>
        internal bool PlaceStopped(Vector3 delta, Quaternion? rot, Vector3 bendDelta)
        {
            if (_effector.bone == null) return false;
            if (_ownTarget == null)
            {
                var go = new GameObject("ksPlugPoseForce_Limb_" + Label) { hideFlags = HideFlags.HideAndDontSave };
                _ownTarget = go.transform;
            }
            _ownTarget.SetPositionAndRotation(_effector.bone.position + delta, rot.HasValue ? _effector.bone.rotation * rot.Value : _effector.bone.rotation);
            _effector.target = _ownTarget;
            _effector.positionWeight = 1f;
            _effector.rotationWeight = rot.HasValue ? 1f : 0f;
            if (_bend != null && _joint != null)
            {
                if (_ownGoal == null)
                {
                    var go = new GameObject("ksPlugPoseForce_Bend_" + Label) { hideFlags = HideFlags.HideAndDontSave };
                    _ownGoal = go.transform;
                }
                _ownGoal.position = _joint.position + bendDelta;
                _bend.bendGoal = _ownGoal;
                _bend.weight = 1f;
            }
            return true;
        }

        /// <summary>このフレームはずらさない。</summary>
        internal void Idle() { Restore(); Active = false; _mode = null; }

        internal void Clear()
        {
            Restore();
            Active = false;
            if (_ownTarget != null) { Object.Destroy(_ownTarget.gameObject); _ownTarget = null; }
            if (_ownGoal != null) { Object.Destroy(_ownGoal.gameObject); _ownGoal = null; }
        }

        internal string Describe()
        {
            // ずらしている間は、ゲームの元の付け先・重みを出す
            Transform target = _swappedTarget ? _origTarget : _effector.target;
            float posWeight = _swappedTarget ? _origPosWeight : _effector.positionWeight;
            float rotWeight = _swappedTarget ? _origRotWeight : _effector.rotationWeight;
            Transform goal = _swappedGoal ? _origGoal : _bend != null ? _bend.bendGoal : null;
            float bendWeight = _swappedGoal ? _origBendWeight : _bend != null ? _bend.weight : 0f;
            string bend = _bend != null ? LinkName(goal) + "・重み " + bendWeight.ToString("0.00") : "なし";
            return Label + " 付け先=" + LinkName(target) + "・位置の重み " + posWeight.ToString("0.00") + "・回転の重み " + rotWeight.ToString("0.00")
                + "・曲げ " + bend + (Active ? "・ずらし中" + (_mode != null ?"（" + _mode + "）" : "") : "");
        }
    }
}
