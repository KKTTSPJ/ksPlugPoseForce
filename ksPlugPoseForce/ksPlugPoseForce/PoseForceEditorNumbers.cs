using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ksPlugPoseForce
{
    internal static partial class PoseForce
    {
        private sealed class NumberEdit { internal string Text; internal float Value; }
        private static readonly Dictionary<string, NumberEdit> NumberEdits = new Dictionary<string, NumberEdit>();

        private static float EditorNumber(string name, string label, float value, bool rotation)
        {
            float limit = rotation ? 60f : 0.5f, step = rotation ? 1f : 0.01f;
            // 採取した値が仮の範囲外でも、画面を開いただけで値を切り詰めない。
            float low = Mathf.Min(-limit, value), high = Mathf.Max(limit, value);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(65));
            string formatted = value.ToString("0.#####", CultureInfo.InvariantCulture);
            NumberEdit edit;
            bool focused = GUI.GetNameOfFocusedControl() == name;
            string shown = focused && NumberEdits.TryGetValue(name, out edit) && edit.Value == value ? edit.Text : formatted;
            if (!focused) NumberEdits.Remove(name);
            GUI.SetNextControlName(name);
            string typed = GUILayout.TextField(shown, GUILayout.Width(85));
            if (typed != shown)
            {
                float parsed;
                if (float.TryParse(typed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                    value = Mathf.Clamp(parsed, low, high);
                NumberEdits[name] = new NumberEdit { Text = typed, Value = value };
            }
            if (GUILayout.Button("−", GUILayout.Width(35))) { value = Mathf.Clamp(value - step, low, high); NumberEdits.Remove(name); }
            if (GUILayout.Button("0", GUILayout.Width(35))) { value = 0f; NumberEdits.Remove(name); }
            if (GUILayout.Button("＋", GUILayout.Width(35))) { value = Mathf.Clamp(value + step, low, high); NumberEdits.Remove(name); }
            float next = GUILayout.HorizontalSlider(value, low, high);
            if (Mathf.Abs(next - value) > 0.000001f) { value = next; NumberEdits.Remove(name); }
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
