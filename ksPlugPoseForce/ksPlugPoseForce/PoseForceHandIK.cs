using RootMotion.FinalIK;
using UnityEngine;

namespace ksPlugPoseForce
{
    internal sealed class PoseForceHandIK
    {
        private readonly IKEffector _effector;
        private readonly string _label;
        private readonly PoseForceTargetLease<Transform> _lease = new PoseForceTargetLease<Transform>();
        private Transform _owned, _parent;
        private int _replacements;
        private float _nextWarning;

        internal PoseForceHandIK(IKEffector effector, string label) { _effector = effector; _label = label; }

        internal void Apply(PoseForceRow row, Transform parent, float force)
        {
            if (row == null || parent == null || force <= 0f) { Clear(); return; }
            bool replaced = false;
            if (_owned != null && !_lease.Owns(_effector.target))
            {
                ++_replacements;
                if (_replacements >= 2 && Time.unscaledTime >= _nextWarning)
                {
                    _nextWarning = Time.unscaledTime + 5f;
                    PoseForcePlugin.Logger.LogWarning("[PoseForce][手] " + _label + "の目標がほかの処理で変更されています（" + _replacements + " 回）。現在の目標を保存し直します。");
                    _replacements = 0;
                }
                Clear(false);
                replaced = true;
            }
            if (_owned == null || _parent != parent)
            {
                Clear();
                var go = new GameObject("ksPlugPoseForce_Hand_" + _label) { hideFlags = HideFlags.HideAndDontSave };
                _owned = go.transform;
                _owned.SetParent(parent, false);
                _parent = parent;
                _lease.Capture(_effector.target, _effector.positionWeight, _owned);
                _effector.target = _owned;
                if (!replaced) PoseForcePlugin.Logger.LogInfo("[PoseForce][手] " + _label + "を付け替えました（" + row.Target + "）。");
            }
            _owned.localPosition = new Vector3(row.X, row.Y, row.Z) * force;
            _effector.positionWeight = 1f;
        }

        internal void Clear(bool log = true)
        {
            Transform original;
            float weight;
            bool restored = _lease.TryRestore(_effector.target, out original, out weight);
            if (restored) { _effector.target = original; _effector.positionWeight = weight; }
            if (_owned != null)
            {
                Object.Destroy(_owned.gameObject);
                if (log) PoseForcePlugin.Logger.LogInfo("[PoseForce][手] " + _label + "を解除しました（" + (restored ? "元の目標へ復元" : "現在の別の目標を維持") + "）。");
            }
            _owned = _parent = null;
            _lease.Reset();
        }
    }
}
