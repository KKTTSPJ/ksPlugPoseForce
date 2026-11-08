namespace ksPlugPoseForce
{
    // 自分の目標がまだ使われているときだけ、保存した状態を戻す。
    internal sealed class PoseForceTargetLease<T> where T : class
    {
        private T _original, _applied;
        private float _weight;
        private bool _has;

        internal void Capture(T original, float weight, T applied)
        {
            _original = original; _weight = weight; _applied = applied; _has = true;
        }

        internal bool Owns(T current) { return _has && object.ReferenceEquals(current, _applied); }

        internal bool TryRestore(T current, out T original, out float weight)
        {
            original = null; weight = 0;
            if (!Owns(current)) return false;
            original = _original; weight = _weight;
            return true;
        }

        internal void Reset() { _original = _applied = null; _has = false; }
    }

    // 目標を差し替えずに重みだけを維持する。外から更新された重みを元の値として取り直す。
    internal sealed class PoseForceWeightLease<T> where T : class
    {
        private T _target;
        private float _original, _written;
        private bool _has;

        internal void Apply(T target, float current, float written)
        {
            if (!_has || !object.ReferenceEquals(_target, target) || current != _written) _original = current;
            _target = target; _written = written; _has = true;
        }

        internal bool TryRestore(T target, float current, out float weight)
        {
            weight = 0f;
            if (!_has || !object.ReferenceEquals(_target, target) || current != _written) return false;
            weight = _original;
            return true;
        }

        internal void Reset() { _target = null; _has = false; }
    }
}
