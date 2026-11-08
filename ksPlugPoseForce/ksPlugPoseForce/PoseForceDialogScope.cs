namespace ksPlugPoseForce
{
    // 選択を開いた時点の対象を保持し、別の体位・人・状態へ誤って適用しない。
    internal sealed class PoseForceDialogScope<T> where T : class
    {
        private readonly T _scene;
        internal readonly string Asset, Chara, State, Field;
        internal PoseForceDialogScope(T scene, string asset, string chara, string state, string field)
        { _scene = scene; Asset = asset; Chara = chara; State = state; Field = field; }
        internal bool Matches(T scene, string asset, string chara, string state)
        { return object.ReferenceEquals(_scene, scene) && Asset == asset && Chara == chara && State == state; }
    }
}
