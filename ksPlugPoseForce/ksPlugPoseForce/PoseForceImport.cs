using System;
using System.Collections.Generic;
using System.IO;

namespace ksPlugPoseForce
{
    /// <summary>
    /// 取り込み。kPlug の書き出し（origin=kplug）と、利用者が作った共有データ（origin=pack:名前）を、利用者の表に合流する。
    /// 優先は user ＞ pack ＞ kplug ＞ builtin。同じ出どころの取り込み直しは、その出どころの行を置き換え、無くなった行を消す。
    /// </summary>
    internal static class PoseForceImport
    {
        internal enum SourceKind { KPlug, Share }

        internal sealed class Source
        {
            internal SourceKind Kind;
            internal string Origin, Name;
            internal List<PoseForceRow> Rows;
            internal int Skipped;   // 共有データから取り込まない行（kplug・pack・builtin の行、体位を止める行）
        }

        internal sealed class Result
        {
            internal List<PoseForceRow> Rows;
            internal List<PoseForceRow> SourceRows;
            internal Source Source;
            internal int Added, Replaced, Kept, Removed, Shadowed;
        }

        /// <summary>
        /// 取り込むファイルを読み、種類を見分ける。origin=user の行があれば共有データ（user の行だけを pack:名前 にする）、
        /// 無くてすべて kplug の値の行なら kPlug の書き出し。どちらでもなければ InvalidDataException。
        /// </summary>
        internal static Source ReadSource(string path, Action<string> warning)
        {
            var table = new PoseForceTable(warning);
            using (var reader = new StreamReader(path)) table.Read(reader, path, false);
            if (table.RowCount == 0 || table.RejectedCount != 0) throw new InvalidDataException("ファイルが空か、読めない行があります。現在の表は保存し直しません。");
            bool share = false;
            foreach (var row in table.Rows) if (row.Origin == "user") { share = true; break; }
            var source = new Source { Rows = new List<PoseForceRow>() };
            if (!share)
            {
                foreach (var row in table.Rows)
                    if (row.Origin != "kplug" || row.Field == "disabled" || PoseForceTable.IsRemoval(row.Field))
                        throw new InvalidDataException("kPlug の書き出しでも、共有データ（origin=user の行）でもありません。");
                source.Kind = SourceKind.KPlug; source.Origin = "kplug"; source.Name = "kPlug";
                source.Rows.AddRange(table.Rows);
                return source;
            }
            source.Kind = SourceKind.Share;
            source.Name = PoseForceTable.SanitizePackName(ShareName(path) ?? Path.GetFileNameWithoutExtension(path));
            source.Origin = "pack:" + source.Name;
            foreach (var row in table.Rows)
            {
                if (row.Origin != "user" || row.Field == "disabled") { ++source.Skipped; continue; }
                var copy = row.Copy(); copy.Origin = source.Origin;
                source.Rows.Add(copy);
            }
            if (source.Rows.Count == 0) throw new InvalidDataException("取り込める行（origin=user の値の行・欄を消す指定）がありません。");
            return source;
        }

        /// <summary>注記 "# share&lt;TAB&gt;名前" の名前。無ければ null。</summary>
        internal static string ShareName(string path)
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (!line.StartsWith("#", StringComparison.Ordinal)) break;
                if (line.StartsWith("# share\t", StringComparison.Ordinal) && line.Length > 8) return line.Substring(8).Trim();
            }
            return null;
        }

        internal static Result ReadAndMerge(string currentPath, string sourcePath, Action<string> warning)
        {
            var source = ReadSource(sourcePath, warning);
            var current = new PoseForceTable(warning);
            if (currentPath != null && File.Exists(currentPath)) using (var reader = new StreamReader(currentPath)) current.Read(reader, currentPath, false);
            if (current.RejectedCount != 0) throw new InvalidDataException("利用者の表に読めない行があります。修正してから取り込んでください。");
            var result = Merge(current.Rows, source.Rows, source.Origin);
            result.Source = source;
            return result;
        }

        /// <summary>kPlug の書き出しの合流（0.1.2 までと同じ呼び方）。</summary>
        internal static Result Merge(IEnumerable<PoseForceRow> current, IEnumerable<PoseForceRow> incoming)
        {
            return Merge(current, incoming, "kplug");
        }

        internal static Result Merge(IEnumerable<PoseForceRow> current, IEnumerable<PoseForceRow> incoming, string origin)
        {
            bool pack = PoseForceTable.IsPack(origin);
            if (origin != "kplug" && !pack) throw new InvalidDataException("取り込みの出どころが正しくありません: " + origin);
            var old = new Dictionary<string, PoseForceRow>(StringComparer.Ordinal);
            var source = new Dictionary<string, PoseForceRow>(StringComparer.Ordinal);
            var merged = new SortedDictionary<string, PoseForceRow>(StringComparer.Ordinal);
            foreach (var row in current) old[row.Key] = row;
            // 同じ出どころの前の行は、新しい内容に無ければ消す（体位を止める行は出どころによらず残す）。
            foreach (var row in old.Values) if (row.Origin != origin || row.Field == "disabled") merged[row.Key] = row;
            foreach (var row in incoming)
            {
                if (row.Origin != origin || row.Field == "disabled" || (!pack && PoseForceTable.IsRemoval(row.Field)))
                    throw new InvalidDataException(pack ? "共有データ以外の行が含まれています。" : "kPlug の書き出し以外の行が含まれています。");
                source[row.Key] = row;
            }
            var result = new Result();
            int rank = PoseForceTable.Rank(origin);
            foreach (var pair in source)
            {
                PoseForceRow previous;
                old.TryGetValue(pair.Key, out previous);
                if (previous != null && previous.Origin != origin && (previous.Field == "disabled" || PoseForceTable.Rank(previous.Origin) > rank))
                { ++result.Shadowed; continue; }
                // 欄を消す指定は、同じ欄に優先の高い行（自分の行など）があれば取り込まない（自分の編集を隠さないため）。
                if (PoseForceTable.IsRemoval(pair.Value.Field))
                {
                    PoseForceRow target;
                    string valueKey = pair.Value.Asset + "\t" + pair.Value.Chara + "\t" + pair.Value.State + "\t" + pair.Value.Field.Substring(9);
                    if (old.TryGetValue(valueKey, out target) && target.Origin != origin && PoseForceTable.Rank(target.Origin) > rank)
                    { ++result.Shadowed; continue; }
                }
                if (previous == null) ++result.Added; else ++result.Replaced;
                var row = pair.Value.Copy(); row.Origin = origin;
                merged[pair.Key] = row;
            }
            foreach (var pair in old)
            {
                PoseForceRow value;
                if (!merged.TryGetValue(pair.Key, out value)) ++result.Removed;
                else if (ReferenceEquals(pair.Value, value)) ++result.Kept;
            }
            result.Rows = new List<PoseForceRow>(merged.Values);
            result.SourceRows = new List<PoseForceRow>(source.Values);
            return result;
        }
    }
}
