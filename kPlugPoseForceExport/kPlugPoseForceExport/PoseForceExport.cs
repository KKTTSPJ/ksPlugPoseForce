using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace kPlugPoseForceExport
{
    internal static class PoseForceExport
    {
        private const string Tag = "[PoseForceExport]";
        private static bool _requested;
        private static string _status = "通常版の Hシーンで使えます。";
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // 2 人の挿入で使う状態。追加された判定用の一覧も実行時に取り込む。
        private const string BaseStates = "Idle Insert InsertIdle WLoop SLoop OLoop Pull Drop IN_A WS_IN_A SS_IN_A OUT_A "
            + "M_IN_Start M_IN_Loop M_OUT_Start M_OUT_Loop WS_IN_Start WS_IN_Loop SS_IN_Start SS_IN_Loop "
            + "WF_IN_Start WF_IN_Loop SF_IN_Start SF_IN_Loop";

        private sealed class ConfigurationManagerAttributes
        {
            public Action<ConfigEntryBase> CustomDrawer;
        }

        internal static void BindConfig(ConfigFile config)
        {
            config.Bind("PoseForceExport", "Export", false,
                new ConfigDescription("kPlug の姿勢補正の値を TSV に書き出します。通常版の Hシーンの初期化後に使えます。", null,
                    new ConfigurationManagerAttributes { CustomDrawer = entry =>
                    {
                        GUILayout.BeginVertical();
                        if (GUILayout.Button("kPlug の姿勢補正の値を書き出す")) _requested = true;
                        GUILayout.Label(_status);
                        GUILayout.EndVertical();
                    } }));
        }

        // OnGUI 中に採取せず、次の Update の中で完結させる。
        internal static void Tick()
        {
            if (!_requested) return;
            _requested = false;
            try
            {
                if (Paths.ProcessName.EndsWith("VR", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("書き出しは通常版で使ってください。");
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "kPlug");
                if (assembly == null) throw new InvalidOperationException("kPlug が見つかりません。");
                Export(assembly);
            }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                _status = "書き出せませんでした: " + ex.Message;
                PoseForceExportPlugin.Logger.LogWarning(Tag + " " + _status);
            }
        }

        private static List<string> States(Assembly assembly)
        {
            var states = new HashSet<string>(BaseStates.Split(' '));
            foreach (string s in states.ToArray()) states.Add("A_" + s);
            var toolH = PoseForceExportSampler.RequireType(assembly, "kPlug.Tools.ToolH");
            foreach (string name in new[] { "maleOrgStates", "inLoopStates", "analLoopStates", "outCumStates", "girlOrgStates", "girlStartOrgStates" })
            {
                var values = PoseForceExportState.Field(toolH, name).GetValue(null) as IEnumerable<string>;
                if (values == null) throw new NotSupportedException("状態の一覧を読めません: " + name);
                foreach (string s in values) states.Add(s);
            }
            return states.OrderBy(s => s, StringComparer.Ordinal).ToList();
        }

        private static void Export(Assembly assembly)
        {
            var timer = Stopwatch.StartNew();
            var poseType = PoseForceExportSampler.RequireType(assembly, "kPlug.CmpH.MainPoseCtrl");
            var known = ((IEnumerable)PoseForceExportState.Get(poseType, "KnownAnims")).Cast<string>().Distinct().ToList();
            var translator = PoseForceExportSampler.RequireType(assembly, "kPlug.DefAndParse.kTranslator");
            var mapper = PoseForceExportSampler.RequireMethod(translator, "AnimNameMapper_H").Invoke(null, null) as IDictionary;
            if (mapper == null || known.Count == 0) throw new NotSupportedException("体位とアセットの一覧を読めません。");
            var assets = new Dictionary<string, List<string>>();
            foreach (string pose in known) assets.Add(pose, new List<string>());
            foreach (DictionaryEntry pair in mapper)
            {
                List<string> list;
                if (pair.Key is string && pair.Value is string && assets.TryGetValue((string)pair.Value, out list)) list.Add((string)pair.Key);
            }
            var unmapped = known.Where(p => assets[p].Count == 0).ToArray();
            // 欠けた表を成功扱いにしない。
            if (unmapped.Length != 0) throw new NotSupportedException("アセットの対応が無い体位: " + string.Join(", ", unmapped));
            var states = States(assembly);
            var rows = new List<string>();
            var diagnostics = new List<string>();
            int motionDifferences = 0, nonlinear = 0, handRows = 0;
            using (var sampler = new PoseForceExportSampler(assembly))
            {
                foreach (string pose in known.OrderBy(s => s, StringComparer.Ordinal))
                {
                    foreach (string state in states)
                    {
                        var full = sampler.Sample(pose, state, 1f, 0.5f);
                        var half = sampler.Sample(pose, state, 0.5f, 0.5f);
                        var motion0 = sampler.Sample(pose, state, 1f, 0f);
                        var motion1 = sampler.Sample(pose, state, 1f, 1f);
                        nonlinear += Compare(pose, state, full, half, 0.5f, "force", diagnostics);
                        motionDifferences += Compare(pose, state, motion0, motion1, 1f, "motion", diagnostics);
                        foreach (string asset in assets[pose].OrderBy(s => s, StringComparer.Ordinal))
                            foreach (var value in full.OrderBy(v => v.Chara, StringComparer.Ordinal).ThenBy(v => v.Field, StringComparer.Ordinal))
                            {
                                ValidateVector(value.Offset);
                                rows.Add(Row(asset, pose, state, value));
                                if (value.Field.StartsWith("ik_hand_", StringComparison.Ordinal)) ++handRows;
                            }
                    }
                }
            }
            // 復元・検証に成功するまでファイルを更新しない。
            string version = assembly.GetName().Version.ToString();
            var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos;
            if (plugin.ContainsKey("kPlug.Katarsys")) version = plugin["kPlug.Katarsys"].Metadata.Version.ToString();
            if (version.Any(c => !(char.IsDigit(c) || c == '.'))) throw new InvalidOperationException("版の表記を読めません。");
            string path = Path.Combine(Paths.ConfigPath, "kPlugPose_kPlug" + version + ".tsv");
            var header = new StringBuilder();
            header.AppendLine("# ksPlugPoseForce PoseForce v1");
            header.AppendLine("# source\tkPlug " + version + "\t" + Timestamp(DateTime.UtcNow));
            header.AppendLine("# poses\t" + known.Count + "\tstates\t" + states.Count + "\trows\t" + rows.Count + "\thandRows\t" + handRows);
            header.AppendLine("# sampling\tForce=1\tmotion=0.5\tposMod=Piston\tanimUseIks=true");
            header.AppendLine("# comparison\tmotionDifferences=" + motionDifferences + "\tnonlinear=" + nonlinear);
            header.AppendLine("asset\tkplug\tchara\tstate\tfield\tx\ty\tz\ttarget\torigin");
            AtomicWrite(path, header.ToString() + string.Join("\n", rows.ToArray()) + "\n");
            string diagnosticPath = Path.Combine(Paths.ConfigPath, "PoseForceDiagnostics_kPlug" + version + ".tsv");
            AtomicWrite(diagnosticPath, "# force: a=Force1, b=Force0.5 (motion0.5); motion: a=motion0, b=motion1 (Force1)\n"
                + "comparison\tkplug\tstate\tchara\tfield\ttarget_a\tax\tay\taz\ttarget_b\tbx\tby\tbz\n"
                + string.Join("\n", diagnostics.ToArray()) + "\n");
            timer.Stop();
            _status = "書き出しました: " + path + "（" + known.Count + " 体位、" + rows.Count + " 行）";
            PoseForceExportPlugin.Logger.LogInfo(Tag + " " + _status + ", 手=" + handRows + ", motion差分=" + motionDifferences
                + ", 比例しない欄=" + nonlinear + ", 所要時間=" + timer.Elapsed.TotalMilliseconds.ToString("F1", Invariant) + " ms");
            PoseForceExportPlugin.Logger.LogInfo(Tag + " 診断ファイル: " + diagnosticPath);
            if (nonlinear != 0) PoseForceExportPlugin.Logger.LogWarning(Tag + " 補正量に比例しない欄があります。診断ファイルの force 行を確認してください。");
        }

        private static string Key(PoseForceExportSampler.Value v) { return v.Chara + "/" + v.Field; }
        private static int Compare(string pose, string state, List<PoseForceExportSampler.Value> a,
            List<PoseForceExportSampler.Value> b, float scale, string comparison, List<string> diagnostics)
        {
            var aa = a.ToDictionary(Key);
            var bb = b.ToDictionary(Key);
            int count = 0;
            foreach (string key in aa.Keys.Union(bb.Keys).OrderBy(s => s, StringComparer.Ordinal))
            {
                PoseForceExportSampler.Value av, bv;
                aa.TryGetValue(key, out av); bb.TryGetValue(key, out bv);
                Vector3 va = av == null ? Vector3.zero : av.Offset, vb = bv == null ? Vector3.zero : bv.Offset;
                ValidateVector(va); ValidateVector(vb);
                string ta = av == null ? "" : av.Target, tb = bv == null ? "" : bv.Target;
                if ((va * scale - vb).sqrMagnitude < 0.0000000001f && ta == tb) continue;
                var v = av ?? bv;
                diagnostics.Add(string.Join("\t", new[] { comparison, pose, state, v.Chara, v.Field, ta,
                    Number(va.x), Number(va.y), Number(va.z), tb, Number(vb.x), Number(vb.y), Number(vb.z) }));
                ++count;
            }
            return count;
        }

        private static void ValidateVector(Vector3 v)
        {
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                throw new InvalidOperationException("書き出し値が有限ではありません。");
        }

        // 日時のオフセット変換を使わず、UTC から JST の表記を作る。
        private static string Timestamp(DateTime utc)
        {
            return utc.AddHours(9).ToString("yyyy-MM-dd'T'HH:mm:ss.fff", Invariant) + "+09:00";
        }

        private static string Number(float value) { return value.ToString("R", Invariant); }
        private static string Row(string asset, string pose, string state, PoseForceExportSampler.Value value)
        {
            var columns = new[] { asset, pose, value.Chara, state, value.Field, Number(value.Offset.x), Number(value.Offset.y), Number(value.Offset.z), value.Target, "kplug" };
            if (columns.Any(c => c.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)) throw new InvalidOperationException("TSV の列に改行・タブがあります。");
            return string.Join("\t", columns);
        }

        private static void AtomicWrite(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
