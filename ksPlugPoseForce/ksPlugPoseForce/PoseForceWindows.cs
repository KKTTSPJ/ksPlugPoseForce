using UnityEngine;

namespace ksPlugPoseForce
{
    internal static partial class PoseForce
    {
        private static bool _open, _editorOpen, _rectInit, _editorRectInit;
        private static Rect _rect, _editorRect;

        private static void OpenSimpleWindow(bool open)
        {
            _open = open; _clearEditorFocus = true;
            if (!open && _rectInit) { _winX.Value = _rect.x; _winY.Value = _rect.y; }
        }

        private static void OpenEditorWindow(bool open)
        {
            _editorOpen = open; _clearEditorFocus = true;
            if (!open && _editorRectInit) { _editorWinX.Value = _editorRect.x; _editorWinY.Value = _editorRect.y; }
        }

        private static Rect FitWindow(Rect rect)
        {
            rect.x = Mathf.Clamp(rect.x, 0, Mathf.Max(0, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0, Mathf.Max(0, Screen.height - rect.height));
            return rect;
        }

        internal static bool WindowOpen { get { return _open || _editorOpen || ModalOpen; } }
        internal static bool PointerOverWindow(Vector2 point)
        { return ModalOpen || (_open && _rect.Contains(point)) || (_editorOpen && _editorRect.Contains(point)); }

        internal static void OnGUI()
        {
            if (_clearEditorFocus) { GUI.FocusControl(null); _clearEditorFocus = false; }
            if (!WindowOpen) { PoseForceInputBlocker.Place(null); return; }
            if (!ModalOpen && Event.current.type == EventType.MouseDown && !PointerOverWindow(Event.current.mousePosition)) GUI.FocusControl(null);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !ModalOpen;
            if (_open)
            {
                if (!_rectInit) { _rect = new Rect(_winX.Value, _winY.Value, 340, 10); _rectInit = true; }
                _rect = FitWindow(_rect);
                _rect = GUILayout.Window(0x5F0CE, _rect, SimpleWindow, "姿勢補正：調整");
            }
            GUI.enabled = enabled && !ModalOpen;
            if (_editorOpen)
            {
                if (!_editorRectInit) { _editorRect = new Rect(_editorWinX.Value, _editorWinY.Value, 800f * 2f / 3f, 800); _editorRectInit = true; }
                _editorRect.width = Mathf.Min(800f * 2f / 3f, Mathf.Max(320, Screen.width - 20));
                _editorRect.height = Mathf.Min(880, Mathf.Max(300, Screen.height - 30));
                _editorRect = FitWindow(_editorRect);
                _editorRect = GUILayout.Window(0x5F0CF, _editorRect, EditorWindow, "姿勢補正：値の編集");
            }
            GUI.enabled = enabled;
            bool modalDrawn = ModalOpen;
            if (modalDrawn) DrawDialog();
            var e = Event.current;
            if ((modalDrawn || PointerOverWindow(e.mousePosition)) && (e.isMouse || e.type == EventType.ScrollWheel || (modalDrawn && e.isKey))) e.Use();
            if (ModalOpen) PoseForceInputBlocker.Place(new Rect(0, 0, Screen.width, Screen.height));
            else PoseForceInputBlocker.Place(_open ? _rect : _editorOpen ? _editorRect : (Rect?)null, _open && _editorOpen ? _editorRect : (Rect?)null);
        }

        private static void DrawPoseHeader(bool editor)
        {
            HFlag flags = _proc != null ? _proc.flags : null;
            if (flags == null) GUILayout.Label("Hシーンの中で使えます");
            else
            {
                GUILayout.Label("体位：" + (flags.nowAnimationInfo?.paramFemale?.path?.file ?? "?") + "（" + flags.mode + "）", EditorLabel);
                GUILayout.Label("状態：" + flags.nowAnimStateName);
                if (editor && _editor != null && _editorKPlugName.Length > 0) GUILayout.Label("kPlug: " + _editorKPlugName);
                GUILayout.Label(!Enabled.Value ? "EnablePoseForce がオフです"
                    : _active ? (_noIK ? "補正しています（女の IK が止まっている状態：手足の IK の目標・手の付け替えは効きません）" : "補正しています")
                    : "補正していません：" + _reason, EditorLabel);
            }
        }

        private static void DrawForceControls()
        {
            bool on = GUILayout.Toggle(Enabled.Value, "EnablePoseForce");
            if (on != Enabled.Value) Enabled.Value = on;
            GUILayout.BeginHorizontal();
            GUILayout.Label("補正量 " + Force.Value.ToString("0.000"), GUILayout.Width(100));
            float next = GUILayout.HorizontalSlider(Force.Value, 0f, 1f);
            if (Mathf.Abs(next - Force.Value) > 0.0001f) Force.Value = next;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−0.1")) Force.Value = PoseForceAmounts.Step(Force.Value, -1);
            foreach (float v in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                if (GUILayout.Button(v.ToString("0.##"))) Force.Value = v;
            if (GUILayout.Button("＋0.1")) Force.Value = PoseForceAmounts.Step(Force.Value, 1);
            GUILayout.EndHorizontal();
        }

        private static void SimpleWindow(int id)
        {
            DrawPoseHeader(false); DrawForceControls();
            if (GUILayout.Button("閉じる")) OpenSimpleWindow(false);
            if (!ModalOpen) GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private static void EditorWindow(int id)
        {
            DrawPoseHeader(true); DrawForceControls();
            _editorScroll = GUILayout.BeginScrollView(_editorScroll, GUILayout.Height(Mathf.Max(100, _editorRect.height - 235)));
            DrawEditor();
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("書き出しの取り込み画面を開く…")) OpenImportDialog();
            if (GUILayout.Button("自分の値の書き出し…")) OpenExportDialog();
            if (GUILayout.Button("閉じる")) OpenEditorWindow(false);
            GUILayout.EndHorizontal();
            if (!ModalOpen) GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }
    }
}
