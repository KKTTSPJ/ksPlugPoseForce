using BepInEx;
using BepInEx.Logging;

namespace kPlugPoseForceExport
{
    /// <summary>
    /// KK（Koikatu）・KKP（Koikatsu Party）の通常版で、実行中の kPlug から姿勢補正の値（体位・人・状態ごとの補正値と手の付け先）を TSV に書き出すプラグイン。
    /// 書き出した主表 kPlugPose_kPlug&lt;版&gt;.tsv を、KKS の ksPlugPoseForce が取り込む。
    ///
    /// kPlug は参照せず、すべてリフレクションで読む（kPlug が無ければ書き出しの要求で理由を出すだけ）。
    /// </summary>
    [BepInPlugin(GUID, PluginName, Version)]
    // 1 つの DLL で KK と KKP の両方に対応する（Release_KK でビルド。ゲーム側の参照は ChaControl と UnityEngine だけで、
    // KKP のアセンブリにも同じ名前・シグネチャであることを tools/verify_pose_force_export.ps1 で確かめる）
    [BepInProcess("Koikatu")]
    [BepInProcess("Koikatsu Party")]
    // kPlug より後に読み込む（必須ではない。書き出しの要求のときに kPlug を探す）
    [BepInDependency("kPlug.Katarsys", BepInDependency.DependencyFlags.SoftDependency)]
    public class PoseForceExportPlugin : BaseUnityPlugin
    {
        public const string GUID = "kkttspj.kk.kplugposeforceexport";
        public const string PluginName = "kPlug Pose Force Export";
        public const string Version = "0.1.1";

        internal static new ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;
            PoseForceExport.BindConfig(Config);
        }

        // ConfigurationManager のボタン（OnGUI）で要求を受け、次の Update の中で採取を完結させる
        private void Update()
        {
            PoseForceExport.Tick();
        }
    }
}
