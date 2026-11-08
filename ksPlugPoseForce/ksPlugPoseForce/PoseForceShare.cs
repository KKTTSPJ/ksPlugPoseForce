using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ksPlugPoseForce
{
    /// <summary>
    /// 自分の値（origin=user）の書き出しと、取り込むファイルの一覧（0.1.3）。
    /// 自分の表（PoseForce.tsv）は取り込みの対象にしない。取り込むファイルは import フォルダ、書き出しは export フォルダ。
    /// </summary>
    internal static class PoseForceShare
    {
        internal const string ImportFolder = "import", ExportFolder = "export";

        internal sealed class Result
        {
            internal string Path, Name;
            internal int Rows, Assets, SameAsKPlug;
            internal bool KPlugReference;
        }

        /// <summary>取り込むファイル: import/*.tsv と、互換のため直下の kPlugPose_*.tsv（新しい順）。自分の表は含めない。</summary>
        internal static string[] ImportFiles(string dataDirectory)
        {
            var files = new List<string>();
            string import = System.IO.Path.Combine(dataDirectory, ImportFolder);
            if (Directory.Exists(import)) files.AddRange(Directory.GetFiles(import, "*.tsv"));
            if (Directory.Exists(dataDirectory)) files.AddRange(Directory.GetFiles(dataDirectory, "kPlugPose_*.tsv"));
            var array = files.ToArray();
            Array.Sort(array, (a, b) =>
            {
                int order = File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
                return order == 0 ? StringComparer.Ordinal.Compare(a, b) : order;
            });
            return array;
        }

        /// <summary>
        /// 保存済みの自分の表から、origin=user の値の行と欄を消す指定を書き出す（体位を止める行・kplug・pack・builtin は含めない）。
        /// referenceFiles のうち kPlug の書き出しと、同じ項目・同じ値・同じ付け先の行は除く（kPlug の値をそのまま配らないため）。
        /// asset が null なら全体位。
        /// </summary>
        internal static Result Export(string tablePath, string exportDirectory, string name, string author, string asset,
            IEnumerable<string> referenceFiles, string version, DateTime now)
        {
            if (!File.Exists(tablePath)) throw new InvalidDataException("自分の表（PoseForce.tsv）がまだありません。値を編集して保存してから書き出してください。");
            var table = new PoseForceTable(null);
            using (var reader = new StreamReader(tablePath)) table.Read(reader, tablePath, false);
            if (table.RejectedCount != 0) throw new InvalidDataException("自分の表に読めない行があります。直してから書き出してください。");

            var result = new Result { Name = PoseForceTable.SanitizePackName(name) };
            var reference = new Dictionary<string, List<PoseForceRow>>(StringComparer.Ordinal);
            foreach (string file in referenceFiles ?? new string[0])
            {
                PoseForceImport.Source source;
                try { source = PoseForceImport.ReadSource(file, null); }
                catch (Exception) { continue; }   // 読めない・共有データのファイルは比べる元にしない
                if (source.Kind != PoseForceImport.SourceKind.KPlug) continue;
                result.KPlugReference = true;
                foreach (var row in source.Rows)
                {
                    List<PoseForceRow> list;
                    if (!reference.TryGetValue(row.Key, out list)) reference[row.Key] = list = new List<PoseForceRow>();
                    list.Add(row);
                }
            }

            var rows = new List<PoseForceRow>();
            var assets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                if (row.Origin != "user" || row.Field == "disabled") continue;
                if (asset != null && row.Asset != asset) continue;
                if (!PoseForceTable.IsRemoval(row.Field) && SameAsReference(row, reference)) { ++result.SameAsKPlug; continue; }
                rows.Add(row);
                assets.Add(row.Asset);
            }
            if (rows.Count == 0)
                throw new InvalidDataException(result.SameAsKPlug > 0
                    ? "書き出す行がありません（自分の行は、すべて kPlug の書き出しと同じ値でした）。"
                    : "書き出す自分の行（編集して保存した値・欄を消す指定）がありません。");
            result.Rows = rows.Count; result.Assets = assets.Count;
            string stamp = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            result.Path = System.IO.Path.Combine(exportDirectory, "PoseForceShare_" + FileNamePart(result.Name) + "_" + stamp + ".tsv");
            var notes = new List<string>
            {
                "share\t" + result.Name,
                "author\t" + OneLine(author),
                "plugin\tksPlugPoseForce " + version,
                "created\t" + now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                "scope\t" + (asset ?? "all"),
                "rows\t" + rows.Count + "\tassets\t" + assets.Count
            };
            Directory.CreateDirectory(exportDirectory);
            PoseForceTable.Write(result.Path, rows, notes);
            return result;
        }

        private static bool SameAsReference(PoseForceRow row, Dictionary<string, List<PoseForceRow>> reference)
        {
            List<PoseForceRow> list;
            if (!reference.TryGetValue(row.Key, out list)) return false;
            foreach (var other in list)
                if (other.X == row.X && other.Y == row.Y && other.Z == row.Z && (other.Target ?? "") == (row.Target ?? "")) return true;
            return false;
        }

        private static string OneLine(string text)
        {
            var builder = new StringBuilder();
            foreach (char ch in (text ?? "").Trim()) builder.Append(ch == '\t' || ch == '\r' || ch == '\n' ? ' ' : ch);
            return builder.ToString();
        }

        private static string FileNamePart(string name)
        {
            var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
            var builder = new StringBuilder();
            foreach (char ch in name) builder.Append(invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch);
            return builder.ToString();
        }
    }
}
