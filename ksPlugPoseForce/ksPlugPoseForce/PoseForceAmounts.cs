using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ksPlugPoseForce
{
    // 体位ごとの量を保持する。骨の補正値の表とは別のファイルに保存する。
    internal sealed class PoseForceAmounts
    {
        private readonly Dictionary<string, float> _values = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly string _path;
        private readonly Action<string> _warning;
        private bool _dirty;
        private double _changedAt, _retryAt;

        internal PoseForceAmounts(string path, Action<string> warning)
        {
            _path = path;
            _warning = warning;
            Load();
        }

        internal static float Normalize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return (float)Math.Round(Math.Max(0d, Math.Min(1d, value)), 3, MidpointRounding.AwayFromZero);
        }

        internal static float Step(float value, int count)
        {
            return Normalize(value + count * 0.1f);
        }

        internal float Get(string asset)
        {
            float value;
            return !string.IsNullOrEmpty(asset) && _values.TryGetValue(asset, out value) ? value : 0f;
        }

        internal void Set(string asset, float value, double now)
        {
            if (string.IsNullOrEmpty(asset) || asset.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0) return;
            value = Normalize(value);
            float previous;
            if (_values.TryGetValue(asset, out previous) && previous == value) return;
            _values[asset] = value;
            _dirty = true;
            _changedAt = now;
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                int lineNumber = 0, rejected = 0, firstRejected = 0;
                foreach (string line in File.ReadLines(_path, Encoding.UTF8))
                {
                    ++lineNumber;
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line == "asset\tamount") continue;
                    string[] cells = line.Split('\t');
                    float value;
                    if (cells.Length != 2 || string.IsNullOrEmpty(cells[0]) || !float.TryParse(cells[1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f)
                    {
                        if (rejected++ == 0) firstRejected = lineNumber;
                        continue;
                    }
                    _values[cells[0]] = Normalize(value);
                }
                if (rejected > 0) Warn("読めない行を " + rejected + " 行飛ばしました（最初: " + _path + ":" + firstRejected + "）。");
            }
            catch (Exception ex) { Warn("補正量を読み込めません: " + _path + " / " + ex.Message); }
        }

        internal bool Save(double now, bool flush)
        {
            if (!_dirty || (!flush && (now - _changedAt < 1d || now < _retryAt))) return false;
            string temp = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var assets = new List<string>(_values.Keys);
                assets.Sort(StringComparer.Ordinal);
                var text = new StringBuilder("# ksPlugPoseForce PoseForceAmounts v1\nasset\tamount\n");
                foreach (string asset in assets)
                    text.Append(asset).Append('\t').Append(_values[asset].ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
                else File.Move(temp, _path);
                _dirty = false;
                return true;
            }
            catch (Exception ex)
            {
                _retryAt = now + 5d;
                Warn("補正量を保存できません: " + _path + " / " + ex.Message);
                return false;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) { Warn("一時ファイルを消せません: " + temp + " / " + ex.Message); }
            }
        }

        private void Warn(string message) { if (_warning != null) _warning(message); }
    }

    internal sealed class PoseForceKeyRepeat
    {
        private bool _armed;
        private double _next;

        internal int Tick(bool down, bool pressed, double now)
        {
            if (down) { _armed = true; _next = now + 0.4d; return 1; }
            if (!pressed) { Reset(); return 0; }
            if (!_armed || now + 0.000001d < _next) return 0;
            int count = (int)Math.Min(1000d, Math.Floor((now - _next + 0.000001d) / 0.1d) + 1d);
            _next += count * 0.1d;
            return Math.Min(10, count);
        }

        internal void Reset() { _armed = false; }
    }
}
