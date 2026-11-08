using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ksPlugPoseForce
{
    /// <summary>
    /// Koikatsu Sunshine（KKS）の H の挿入で、体位・状態ごとの値の表に従って姿勢を補正するプラグイン。
    /// KK の kPlug の「挿入中に補正量を変えて姿勢を変える」機能に当たる。値の表・補正量の操作・ゲーム内の編集・手の IK の付け替えを持つ。
    ///
    /// 0.1.0 は ksPlugPatcher_NRE 0.16.11 の姿勢補正を、単独のプラグインに分けたもの。0.1.1 から kPlug の補正値は内蔵しない。
    /// ksPlug・ksPlugVR が無くても動く。通常版だけ（VR は対象外）。
    /// </summary>
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    // ksPlug より後に読み込む（必須ではない。ksPlug が無くても動く）
    [BepInDependency("ksPlug.Katarsys", BepInDependency.DependencyFlags.SoftDependency)]
    public class PoseForcePlugin : BaseUnityPlugin
    {
        public const string GUID = "kkttspj.kks.ksplugposeforce";
        public const string PluginName = "ksPlug Pose Force";
        public const string Version = "0.1.3";

        /// <summary>値の表・補正量・取り込むファイルを置くフォルダ（BepInEx/config/ksPlugPoseForce）。</summary>
        internal static readonly string DataDirectory = Path.Combine(Paths.ConfigPath, "ksPlugPoseForce");

        // 0.1.0 より前に姿勢補正を持っていた ksPlugPatcher_NRE（0.16.1〜0.16.11）。初回の引き継ぎと二重の確認だけに使う。
        private const string LegacyGuid = "kkttspj.kks.ksplugpatcher.nre";
        private const string LegacyDirectoryName = "ksPlugPatcher_NRE";
        private const string LegacyTypeName = "ksPlugPatcher_NRE.PoseForce";
        private const string Section = "PoseForce";
        /// <summary>引き継がない設定（補正量は体位ごとの PoseForceAmounts.tsv が正。ボタンだけの項目は値に意味が無い）。</summary>
        private static readonly HashSet<string> NotMigrated = new HashSet<string> { "Force", "OpenWindow", "OpenEditor", "ReloadTable", "ImportExport" };

        internal static new ManualLogSource Logger;
        internal static PoseForcePlugin Instance;
        private Harmony _harmony;
        /// <summary>姿勢補正入りの ksPlugPatcher_NRE と一緒に読み込まれたので、何もしない。</summary>
        private bool _standDown;

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;
            // 新しい設定ファイルがまだ無い = この版の初回の起動。BindConfig が補正量のファイルを読むので、データはその前に写す
            bool firstRun = !File.Exists(Config.ConfigFilePath);
            if (firstRun) MigrateLegacyData();
            PoseForce.BindConfig(Config);
            if (firstRun) MigrateLegacySettings();
            foreach (ConfigDefinition def in Config.Keys)
                if (def.Section == Section) WatchSetting(Config[def]);
        }

        private void Start()
        {
            // 全プラグインの Awake の後。姿勢補正入りの ksPlugPatcher_NRE が一緒に読み込まれていたら、同じ骨に 2 回足さないよう当てない
            if (LegacyPoseForceLoaded())
            {
                _standDown = true;
                Logger.LogWarning("[PoseForce] 姿勢補正の入った ksPlugPatcher_NRE（0.16.1〜0.16.11）が読み込まれています。二重に補正しないよう、ksPlugPoseForce の姿勢補正は使いません。ksPlugPatcher_NRE を 0.16.12 以降（姿勢補正なし）に替えてください。");
                return;
            }
            _harmony = new Harmony(GUID);
            PoseForce.TryApply(_harmony);
        }

        private void Update()
        {
            if (!_standDown) PoseForce.OnUpdate();
        }

        private void OnGUI()
        {
            if (!_standDown) PoseForce.OnGUI();
        }

        private void OnApplicationQuit()
        {
            if (!_standDown) PoseForceControls.Flush();
        }

        private static bool LegacyPoseForceLoaded()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetName().Name == LegacyDirectoryName && asm.GetType(LegacyTypeName, false) != null) return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[PoseForce] ksPlugPatcher_NRE の確認に失敗しました。続けます: " + ex.Message);
            }
            return false;
        }

        // ---- 0.16.x（ksPlugPatcher_NRE）からの初回の引き継ぎ。元のファイルは消さない・書き換えない ----

        private static void MigrateLegacyData()
        {
            try
            {
                string legacy = Path.Combine(Paths.ConfigPath, LegacyDirectoryName);
                if (!Directory.Exists(legacy)) return;
                var files = new List<string> { "PoseForce.tsv", "PoseForceAmounts.tsv" };
                foreach (string path in Directory.GetFiles(legacy, "kPlugPose_*.tsv")) files.Add(Path.GetFileName(path));
                foreach (string name in files)
                {
                    string from = Path.Combine(legacy, name), to = Path.Combine(DataDirectory, name);
                    if (!File.Exists(from) || File.Exists(to)) continue;
                    Directory.CreateDirectory(DataDirectory);
                    File.Copy(from, to);
                    Logger.LogInfo("[PoseForce] ksPlugPatcher_NRE の " + name + " を " + DataDirectory + " に写しました（初回の引き継ぎ。元のファイルは残します）。");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[PoseForce] ksPlugPatcher_NRE のデータを引き継げませんでした。内蔵の値で続けます: " + ex.Message);
            }
        }

        private void MigrateLegacySettings()
        {
            try
            {
                string path = Path.Combine(Paths.ConfigPath, LegacyGuid + ".cfg");
                if (!File.Exists(path)) return;
                var values = ReadSection(path, Section);
                int copied = 0;
                foreach (ConfigDefinition def in Config.Keys)
                {
                    string value;
                    if (def.Section != Section || NotMigrated.Contains(def.Key) || !values.TryGetValue(def.Key, out value)) continue;
                    ConfigEntryBase entry = Config[def];
                    string before = entry.GetSerializedValue();
                    entry.SetSerializedValue(value);
                    if (entry.GetSerializedValue() != before) copied++;
                }
                Config.Save();
                if (copied > 0) Logger.LogInfo("[PoseForce] " + Path.GetFileName(path) + " の [PoseForce] から設定を " + copied + " 件引き継ぎました（初回だけ。元のファイルは残します）。");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[PoseForce] ksPlugPatcher_NRE の設定を引き継げませんでした。既定値で続けます: " + ex.Message);
            }
        }

        /// <summary>BepInEx の設定ファイルから、1 つの節の「キー = 値」を読む（注記の行は飛ばす）。</summary>
        private static Dictionary<string, string> ReadSection(string path, string section)
        {
            var values = new Dictionary<string, string>();
            bool inSection = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inSection = line.Substring(1, line.Length - 2).Trim() == section;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (!inSection || eq <= 0) continue;
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return values;
        }

        // ---- 設定の変更のログ（ksPlugPatcher_NRE と同じ形。F1 の設定画面での切り替えは BepInEx がログに出さないため） ----

        private static readonly Dictionary<ConfigEntryBase, string> _lastSettingValues = new Dictionary<ConfigEntryBase, string>();

        private static void WatchSetting(ConfigEntryBase entry)
        {
            _lastSettingValues[entry] = Convert.ToString(entry.BoxedValue);
            EventHandler handler = (sender, args) =>
            {
                try
                {
                    string before = _lastSettingValues.TryGetValue(entry, out string b) ? b : "?";
                    string after = Convert.ToString(entry.BoxedValue);
                    if (before == after) return;
                    _lastSettingValues[entry] = after;
                    // 補正量の読み込み・キーの長押しでは毎回出さない（PoseForceControls が押し終わりに 1 行出す）
                    if (entry.Definition.Key == "Force" && PoseForceControls.SuppressForceLog) return;
                    Logger.LogInfo($"[設定] [{entry.Definition.Section}] {entry.Definition.Key} を {before} → {after} に変更しました（frame {Time.frameCount}、{DateTime.Now:HH:mm:ss}）。");
                }
                catch { }
            };
            // ConfigEntry<T>.SettingChanged は型ごとなのでリフレクションで登録する
            var ev = entry.GetType().GetEvent("SettingChanged");
            if (ev != null) ev.AddEventHandler(entry, handler);
        }
    }
}
