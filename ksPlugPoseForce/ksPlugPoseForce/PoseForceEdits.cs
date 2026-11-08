using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ksPlugPoseForce
{
    // 保存済みの表と未保存の編集を分ける。表示中の体位以外の行も保存時に保持する。
    internal sealed class PoseForceEdits
    {
        internal readonly string Asset;
        private readonly string _path;
        private readonly PoseForceTable _builtin;
        private Dictionary<string, PoseForceRow> _saved, _working;
        private string _fingerprint;
        internal bool Dirty { get; private set; }

        private PoseForceEdits(string asset, string path, PoseForceTable saved, string fingerprint)
        {
            Asset = asset; _path = path; _fingerprint = fingerprint;
            _builtin = PoseForceTable.Create(null, null);
            _saved = Clone(saved.Rows); _working = Clone(saved.Rows);
        }

        internal static PoseForceEdits Load(string asset, string path)
        {
            if (!Cell(asset) || asset.Length == 0 || asset == "*") throw new InvalidDataException("体位が選ばれていません。");
            byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var saved = new PoseForceTable(null);
            if (bytes != null)
                using (var stream = new MemoryStream(bytes))
                using (var reader = new StreamReader(stream, Encoding.UTF8)) saved.Read(reader, path, false);
            if (saved.RejectedCount != 0) throw new InvalidDataException("利用者の表に読めない行があります。ファイルを直して読み直してください。");
            return new PoseForceEdits(asset, path, saved, Fingerprint(bytes));
        }

        internal PoseForceTable Preview()
        {
            var table = new PoseForceTable(null);
            table.Overlay(_builtin.Rows); table.Overlay(_working.Values);
            return table;
        }

        internal void Set(PoseForceRow value)
        {
            if (value == null || value.Asset != Asset || !PoseForceActors.IsRole(value.Chara)
                || !Cell(value.State) || value.State.Length == 0 || value.State == "*" || !Cell(value.KPlug ?? "")
                || !PoseForceFields.Supports(value.Field) || !Finite(value.X) || !Finite(value.Y) || !Finite(value.Z))
                throw new InvalidDataException("編集する行が正しくありません。");
            if (PoseForceFields.IsHand(value.Field))
            {
                string target = value.Target ?? "";
                if (!Cell(target) || PoseForceActors.TargetRole(target).Length == 0)
                    throw new InvalidDataException("手の付け先は female:骨名、male:骨名、female2:骨名で指定します。");
            }
            else if (!string.IsNullOrEmpty(value.Target)) throw new InvalidDataException("手以外の欄には付け先を指定できません。");
            var row = value.Copy(); row.Origin = "user"; row.Target = row.Target ?? ""; row.KPlug = row.KPlug ?? "";
            _working[row.Key] = row;
            _working.Remove(Key(row.Chara, row.State, "disabled:" + row.Field));
            Changed();
        }

        internal void Delete(string chara, string state, string field)
        {
            if (!PoseForceFields.Supports(field) || !PoseForceActors.IsRole(chara) || !Cell(state) || state.Length == 0 || state == "*")
                throw new InvalidDataException("削除する欄が正しくありません。");
            _working.Remove(Key(chara, state, field));
            var marker = new PoseForceRow { Asset = Asset, KPlug = "", Chara = chara, State = state,
                Field = "disabled:" + field, X = 1f, Target = "", Origin = "user" };
            _working[marker.Key] = marker;
            Changed();
        }

        internal void CopyTo(string chara, string state, string field, string[] states)
        {
            PoseForceRow source = null;
            foreach (var row in Preview().Get(Asset, chara, state)) if (row.Field == field) { source = row; break; }
            if (source == null) throw new InvalidDataException("写す欄がありません。");
            foreach (string destination in states)
                if (!Cell(destination) || destination.Length == 0 || destination == "*") throw new InvalidDataException("写し先の状態が正しくありません。");
            foreach (string destination in states) { var row = source.Copy(); row.State = destination; Set(row); }
        }

        internal void SetDisabled(bool disabled)
        {
            var row = new PoseForceRow { Asset = Asset, KPlug = "", Chara = "*", State = "*", Field = "disabled",
                X = disabled ? 1f : 0f, Target = "", Origin = "user" };
            _working[row.Key] = row;
            Changed();
        }

        internal void ResetToSource(IEnumerable<PoseForceRow> source)
        {
            // 入力全体の検査が終わるまで未保存の編集を変更しない。復元元は 1 つの出どころ（kplug か pack:名前）の行だけ。
            var rows = new List<PoseForceRow>(source);
            string origin = rows.Count > 0 ? rows[0].Origin : "kplug";
            var validated = PoseForceImport.Merge(new PoseForceRow[0], rows, origin).Rows;
            var next = Clone(_working.Values);
            foreach (var row in _working.Values) if (row.Asset == Asset && row.Origin == "user") next.Remove(row.Key);
            foreach (var row in validated) if (row.Asset == Asset) next[row.Key] = row.Copy();
            _working = next; Changed();
        }

        /// <summary>この体位の内蔵の行の数（体位を止める行を除く）。</summary>
        internal int BuiltinRowCount
        {
            get { int n = 0; foreach (var row in _builtin.Rows) if (row.Asset == Asset && row.Field != "disabled") ++n; return n; }
        }

        /// <summary>
        /// この体位の内蔵の値を、自分の値（origin=user）として写す（未保存）。自分の行は kPlug・共有データより優先なので、
        /// kPlug の値の代わりに内蔵の値を使いたい体位で使う。同じ欄を消す自分の指定は外す。写した行の数を返す。
        /// </summary>
        internal int UseBuiltinAsUser()
        {
            var rows = new List<PoseForceRow>();
            foreach (var row in _builtin.Rows) if (row.Asset == Asset && row.Field != "disabled") rows.Add(row);
            if (rows.Count == 0) throw new InvalidDataException("この体位に内蔵の値はありません。");
            foreach (var row in rows)
            {
                var copy = row.Copy(); copy.Origin = "user"; copy.Target = copy.Target ?? ""; copy.KPlug = copy.KPlug ?? "";
                _working[copy.Key] = copy;
                if (!PoseForceTable.IsRemoval(copy.Field)) _working.Remove(Key(copy.Chara, copy.State, "disabled:" + copy.Field));
            }
            Changed();
            return rows.Count;
        }

        internal void Undo() { _working = Clone(_saved.Values); Dirty = false; }

        internal bool Save()
        {
            if (!Dirty) return false;
            string current = Fingerprint(File.Exists(_path) ? File.ReadAllBytes(_path) : null);
            if (current != _fingerprint) throw new IOException("利用者の表が外から変更されています。編集を控えてから『値の表を読み直す』で読み直してください。");
            PoseForceTable.Write(_path, _working.Values);
            _fingerprint = Fingerprint(File.ReadAllBytes(_path));
            _saved = Clone(_working.Values); Dirty = false;
            return true;
        }

        private string Key(string chara, string state, string field) { return Asset + "\t" + chara + "\t" + state + "\t" + field; }
        private static bool Cell(string text) { return text != null && text.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0; }
        private static bool Finite(float number) { return !float.IsNaN(number) && !float.IsInfinity(number); }
        private static string Fingerprint(byte[] bytes)
        {
            if (bytes == null) return "missing";
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }
        private static Dictionary<string, PoseForceRow> Clone(IEnumerable<PoseForceRow> rows)
        {
            var copy = new Dictionary<string, PoseForceRow>(StringComparer.Ordinal);
            foreach (var row in rows) copy[row.Key] = row.Copy();
            return copy;
        }
        private void Changed()
        {
            Dirty = _working.Count != _saved.Count;
            if (Dirty) return;
            foreach (var pair in _working)
            {
                PoseForceRow old;
                var row = pair.Value;
                if (!_saved.TryGetValue(pair.Key, out old) || row.KPlug != old.KPlug || row.Origin != old.Origin
                    || row.X != old.X || row.Y != old.Y || row.Z != old.Z || row.Target != old.Target) { Dirty = true; return; }
            }
        }
    }
}
