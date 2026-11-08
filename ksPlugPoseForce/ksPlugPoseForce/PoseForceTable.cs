using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ksPlugPoseForce
{
    internal sealed class PoseForceRow
    {
        internal string Asset, KPlug, Chara, State, Field, Target, Origin;
        internal float X, Y, Z;
        internal string Key { get { return Asset + "\t" + Chara + "\t" + State + "\t" + Field; } }
        internal PoseForceRow Copy() { return (PoseForceRow)MemberwiseClone(); }
    }

    internal sealed class PoseForceTable
    {
        internal const string Header = "asset\tkplug\tchara\tstate\tfield\tx\ty\tz\ttarget\torigin";
        private const string ResourceName = "ksPlugPoseForce.PoseForce.Builtin.tsv";
        private readonly Dictionary<string, PoseForceRow> _rows = new Dictionary<string, PoseForceRow>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<PoseForceRow>> _states = new Dictionary<string, List<PoseForceRow>>(StringComparer.Ordinal);
        private readonly HashSet<string> _assets = new HashSet<string>(StringComparer.Ordinal), _activeStates = new HashSet<string>(StringComparer.Ordinal), _activeActorStates = new HashSet<string>(StringComparer.Ordinal), _disabled = new HashSet<string>(StringComparer.Ordinal);
        private readonly Action<string> _warning;
        private static readonly PoseForceRow[] NoRows = new PoseForceRow[0];

        internal PoseForceTable(Action<string> warning) { _warning = warning; }
        internal int RowCount { get { return _rows.Count; } }
        internal int AssetCount { get { return _assets.Count; } }
        internal int RejectedCount { get; private set; }
        internal IEnumerable<PoseForceRow> Rows { get { return _rows.Values; } }

        /// <summary>
        /// 内蔵の表の読み元。既定は DLL の埋め込み（PoseForceBuiltin.tsv）。0.1.1 から内蔵の値は空（列名だけ）で、
        /// 値は kPlug の書き出しの取り込みか、利用者の編集で用意する。内蔵の仕組み（origin=builtin、利用者の表による上書き・削除、
        /// 基準への復元）は、将来ここに独自の補正値を入れるために残す。検査（tools/）だけが、この読み元を差し替える。
        /// </summary>
        internal static Func<TextReader> BuiltinReader = OpenBuiltinResource;

        private static TextReader OpenBuiltinResource()
        {
            var stream = typeof(PoseForceTable).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null) throw new InvalidOperationException("内蔵の姿勢補正の表が見つかりません。");
            return new StreamReader(stream, Encoding.UTF8);
        }

        internal static PoseForceTable Create(string userPath, Action<string> warning)
        {
            var table = new PoseForceTable(warning);
            using (var reader = BuiltinReader()) table.Read(reader, ResourceName, true);
            if (File.Exists(userPath))
            {
                using (var reader = new StreamReader(userPath, Encoding.UTF8)) table.Read(reader, userPath, false);
            }
            return table;
        }

        internal void Read(TextReader reader, string source, bool builtin)
        {
            int lineNumber = 0, rejected = 0, firstRejected = 0, duplicate = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unsupported = new SortedSet<string>(StringComparer.Ordinal);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                ++lineNumber;
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line == Header) continue;
                PoseForceRow row;
                if (!TryParse(line, out row))
                {
                    if (rejected++ == 0) firstRejected = lineNumber;
                    continue;
                }
                if (builtin) row.Origin = "builtin";
                if (!seen.Add(row.Key)) ++duplicate;
                _rows[row.Key] = row;
                if (row.Field != "disabled" && !IsRemoval(row.Field) && !PoseForceFields.Supports(row.Field)) unsupported.Add(row.Field);
            }
            RejectedCount += rejected;
            if (rejected > 0) Warn(source + ":" + firstRejected + " から、読めない行を " + rejected + " 行飛ばしました。");
            if (duplicate > 0) Warn(source + ": 同じ鍵の行が " + duplicate + " 行あり、後の行を使います。");
            if (unsupported.Count > 0) Warn(source + ": この版では使わない欄: " + string.Join(", ", new List<string>(unsupported).ToArray()));
            Index();
        }

        private static bool TryParse(string line, out PoseForceRow row)
        {
            row = null;
            string[] c = line.Split('\t');
            float x, y, z;
            if (c.Length != 10 || string.IsNullOrEmpty(c[0]) || string.IsNullOrEmpty(c[4]) || c[0] == "*"
                || !Number(c[5], out x) || !Number(c[6], out y) || !Number(c[7], out z)
                || !IsOrigin(c[9])) return false;
            if (c[4] == "disabled")
            {
                if (c[2] != "*" || c[3] != "*" || (x != 0f && x != 1f) || y != 0f || z != 0f || c[8].Length != 0) return false;
            }
            else
            {
                if ((c[2] != "female" && c[2] != "male" && c[2] != "female2") || string.IsNullOrEmpty(c[3]) || c[3] == "*") return false;
                if (c[4].StartsWith("disabled:", StringComparison.Ordinal))
                {
                    if (!IsRemoval(c[4]) || (c[9] != "user" && !IsPack(c[9])) || x != 1f || y != 0f || z != 0f || c[8].Length != 0) return false;
                }
                else if (c[4] == "ik_hand_L" || c[4] == "ik_hand_R")
                {
                    int colon = c[8].IndexOf(':');
                    if (colon < 1 || colon == c[8].Length - 1) return false;
                    string owner = c[8].Substring(0, colon);
                    if (owner != "female" && owner != "male" && owner != "female2") return false;
                }
                else if (c[8].Length != 0) return false;
            }
            row = new PoseForceRow { Asset = c[0], KPlug = c[1], Chara = c[2], State = c[3], Field = c[4], X = x, Y = y, Z = z, Target = c[8], Origin = c[9] };
            return true;
        }

        private static bool Number(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void Index()
        {
            _states.Clear(); _assets.Clear(); _activeStates.Clear(); _activeActorStates.Clear(); _disabled.Clear();
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in _rows.Values)
                if (IsRemoval(row.Field)) removed.Add(row.Asset + "\t" + row.Chara + "\t" + row.State + "\t" + row.Field.Substring(9));
            foreach (var row in _rows.Values)
            {
                _assets.Add(row.Asset);
                if (row.Field == "disabled") { if (row.X == 1f) _disabled.Add(row.Asset); continue; }
                if (IsRemoval(row.Field) || removed.Contains(row.Key)) continue;
                string key = row.Asset + "\t" + row.Chara + "\t" + row.State;
                List<PoseForceRow> values;
                if (!_states.TryGetValue(key, out values)) { values = new List<PoseForceRow>(); _states.Add(key, values); }
                values.Add(row);
                if (PoseForceFields.Supports(row.Field))
                {
                    _activeStates.Add(row.Asset + "\t" + row.State);
                    _activeActorStates.Add(key);
                }
            }
        }

        /// <summary>origin 列の値: builtin（内蔵）・kplug（kPlug の書き出し）・pack:名前（共有データ）・user（自分の編集）。</summary>
        internal static bool IsOrigin(string origin)
        {
            return origin == "builtin" || origin == "kplug" || origin == "user" || IsPack(origin);
        }

        internal static bool IsPack(string origin)
        {
            return origin != null && origin.StartsWith("pack:", StringComparison.Ordinal) && origin.Length > 5 && origin.Length <= 5 + MaxPackName
                && origin.Trim() == origin && origin.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0;
        }

        internal const int MaxPackName = 64;

        /// <summary>共有データの名前を origin 列に入れられる形にする（タブ・改行・前後の空白を除き、空なら "share"、長ければ切る）。</summary>
        internal static string SanitizePackName(string name)
        {
            var text = new StringBuilder();
            foreach (char ch in (name ?? "").Trim()) text.Append(ch == '\t' || ch == '\r' || ch == '\n' ? '_' : ch);
            string result = text.ToString().Trim();
            if (result.Length > MaxPackName) result = result.Substring(0, MaxPackName).Trim();
            return result.Length == 0 ? "share" : result;
        }

        /// <summary>同じ項目の行がぶつかったときの優先（大きいほど強い）。user ＞ pack ＞ kplug ＞ builtin。</summary>
        internal static int Rank(string origin)
        {
            if (origin == "user") return 4;
            if (IsPack(origin)) return 3;
            if (origin == "kplug") return 2;
            return 1;
        }

        internal static bool IsRemoval(string field)
        {
            return field != null && field.StartsWith("disabled:", StringComparison.Ordinal) && PoseForceFields.Supports(field.Substring(9));
        }

        internal void Overlay(IEnumerable<PoseForceRow> rows)
        {
            foreach (var row in rows) _rows[row.Key] = row;
            Index();
        }

        internal bool HasAsset(string asset) { return _assets.Contains(asset); }
        internal bool IsDisabled(string asset) { return _disabled.Contains(asset); }
        internal bool HasState(string asset, string state) { return _activeStates.Contains(asset + "\t" + state); }
        internal bool HasActorState(string asset, string chara, string state) { return _activeActorStates.Contains(asset + "\t" + chara + "\t" + state); }
        internal IList<PoseForceRow> Get(string asset, string chara, string state)
        {
            List<PoseForceRow> rows;
            return _states.TryGetValue(asset + "\t" + chara + "\t" + state, out rows) ? (IList<PoseForceRow>)rows : NoRows;
        }

        internal static void Write(string path, IEnumerable<PoseForceRow> rows)
        {
            Write(path, rows, null);
        }

        /// <summary>notes は注記の行（先頭に "# " を付けて書く）。共有データの名前（"share\t名前"）など。</summary>
        internal static void Write(string path, IEnumerable<PoseForceRow> rows, IEnumerable<string> notes)
        {
            var sorted = new SortedDictionary<string, PoseForceRow>(StringComparer.Ordinal);
            foreach (var row in rows) sorted[row.Key] = row;
            var text = new StringBuilder("# ksPlugPoseForce PoseForce v1\n");
            if (notes != null)
                foreach (string note in notes)
                {
                    if (note == null || note.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new InvalidDataException("注記に改行があります。");
                    text.Append("# ").Append(note).Append('\n');
                }
            text.Append(Header).Append('\n');
            foreach (var row in sorted.Values)
            {
                var c = new[] { row.Asset, row.KPlug ?? "", row.Chara, row.State, row.Field,
                    row.X.ToString("R", CultureInfo.InvariantCulture), row.Y.ToString("R", CultureInfo.InvariantCulture), row.Z.ToString("R", CultureInfo.InvariantCulture), row.Target ?? "", row.Origin };
                foreach (string cell in c)
                    if (cell == null || cell.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0) throw new InvalidDataException("補正の表の列にタブ・改行・null があります。");
                string line = string.Join("\t", c);
                PoseForceRow checkedRow;
                if (!TryParse(line, out checkedRow)) throw new InvalidDataException("補正の表に保存できない行があります。");
                text.Append(line).Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private void Warn(string message) { if (_warning != null) _warning(message); }
    }
}
