using System.Globalization;
using Illusion.Component.Correct;
using Illusion.Component.Correct.Process;
using RootMotion.FinalIK;
using UnityEngine;

namespace ksPlugPoseForce
{
    // 既存の中継目標の接続先とずらしを使い、体の補正後に手の目標を作る。
    internal sealed class PoseForceNativeHandIK
    {
        private readonly IKEffector _effector;
        private readonly string _label;
        private readonly PoseForceWeightLease<Transform> _lease = new PoseForceWeightLease<Transform>();
        private readonly PoseForceTargetLease<Transform> _targets = new PoseForceTargetLease<Transform>();
        private Transform _owned, _source;
        private Transform _processSource;
        private BaseProcess[] _processes;
        private bool _active, _announced;
        private float _observed;
        private float _nextStartLog, _nextEndLog;

        internal PoseForceNativeHandIK(IKEffector effector, string label) { _effector = effector; _label = label; }

        internal void Apply(Transform partnerRoot, Transform otherRoot, Transform bodyRoot)
        {
            if (_owned != null && !_targets.Owns(_effector.target)) Clear();
            var source = _owned != null ? _source : _effector.target;
            if (source == null) { Clear(); return; }
            var data = source.GetComponent<BaseData>();
            var process = ActiveSyncProcess(source);
            bool sync = data != null && process != null && bodyRoot != null;
            Transform anchor = sync ? data.bone : source;
            if (!Within(anchor, partnerRoot) && !Within(anchor, otherRoot)) { Clear(); return; }
            if (sync)
            {
                if (_owned == null)
                {
                    var go = new GameObject("ksPlugPoseForce_NativeHand_" + _label) { hideFlags = HideFlags.HideAndDontSave };
                    _owned = go.transform;
                    _source = source;
                    _targets.Capture(source, _effector.positionWeight, _owned);
                    _effector.target = _owned;
                    Notice("の既存リンクを補正後の骨に同期します（元 " + source.name + "・接続先 " + anchor.name + "）。", false);
                }
                if (_owned.parent != anchor) _owned.SetParent(anchor, false);
                // ずらしの向きは接続先ではなく、手を持つキャラクターの体の基準を使う。
                _owned.SetPositionAndRotation(anchor.position + bodyRoot.TransformDirection(data.pos), anchor.rotation * data.rot);
            }
            else if (_owned != null) { Clear(); return; }
            var target = _effector.target;
            _observed = _effector.positionWeight;
            _lease.Apply(target, _observed, 1f);
            _effector.positionWeight = 1f;
            _active = true;
            if (!_announced && _observed < 0.999f)
            {
                _announced = true;
                Notice("の既存リンクの位置の重みを維持します（" + target.name + "・元 " + _observed.ToString("0.000", CultureInfo.InvariantCulture) + "）。", false);
            }
        }

        private static bool Within(Transform anchor, Transform root)
        {
            return anchor != null && root != null && (anchor == root || anchor.IsChildOf(root));
        }

        private void Notice(string message, bool ending)
        {
            float now = Time.unscaledTime;
            if (now < (ending ? _nextEndLog : _nextStartLog)) return;
            if (ending) _nextEndLog = now + 5f; else _nextStartLog = now + 5f;
            PoseForcePlugin.Logger.LogInfo("[PoseForce][手] " + _label + message);
        }

        private BaseProcess ActiveSyncProcess(Transform source)
        {
            bool refresh = _processSource != source || _processes == null;
            if (!refresh)
                foreach (var process in _processes) if (process == null) { refresh = true; break; }
            if (refresh) { _processSource = source; _processes = source.GetComponents<BaseProcess>(); }
            foreach (var process in _processes)
                if (process != null && process.isActiveAndEnabled && process.type == BaseProcess.Type.Sync) return process;
            return null;
        }

        internal void Clear()
        {
            float weight;
            if (_lease.TryRestore(_effector.target, _effector.positionWeight, out weight)) _effector.positionWeight = weight;
            Transform original;
            float unused;
            if (_targets.TryRestore(_effector.target, out original, out unused)) _effector.target = original;
            if (_owned != null)
            {
                Object.Destroy(_owned.gameObject);
                Notice("の既存リンクの同期を解除しました。", true);
            }
            if (_active && _announced) Notice("の既存リンクの重みの維持を解除しました。", true);
            _active = _announced = false;
            _lease.Reset();
            _targets.Reset();
            _owned = _source = null;
        }

        internal string Describe()
        {
            var target = _effector.target;
            string name = target != null ? target.name : "なし";
            var source = _owned != null && _targets.Owns(target) ? _source : target;
            var data = source != null ? source.GetComponent<BaseData>() : null;
            var process = source != null ? ActiveSyncProcess(source) : null;
            string link = data != null && data.bone != null ? data.bone.name : "なし";
            string mode = process != null ? process.GetType().Name + "/" + process.type : "有効な同期なし";
            string weight = _effector.positionWeight.ToString("0.000", CultureInfo.InvariantCulture);
            string previous = _active ? _observed.ToString("0.000", CultureInfo.InvariantCulture) : "—";
            string gap = target != null && _effector.bone != null
                ? Vector3.Distance(target.position, _effector.bone.position).ToString("0.000", CultureInfo.InvariantCulture) : "—";
            return _label + " 目標=" + name + "・中継元=" + (source != null ? source.name : "なし") + "・接続先=" + link + "・中継=" + mode
                + "・位置の重み=" + weight + "・維持前=" + previous + "・目標と手の距離=" + gap;
        }
    }
}
