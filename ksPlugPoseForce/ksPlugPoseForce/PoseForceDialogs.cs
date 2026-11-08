using UnityEngine;

namespace ksPlugPoseForce
{
    internal static partial class PoseForce
    {
        private enum DialogKind { None, Target, Copy, Restore, Import, Export }
        private static DialogKind _dialog;
        private static Rect _dialogRect;
        private static PoseForceDialogScope<HSceneProc> _dialogScope;
        private static string _dialogStatus = "";
        private static int _targetRole;
        private static Vector2 _dialogBodyScroll;
        internal static bool ModalOpen { get { return _dialog != DialogKind.None; } }

        private static void OpenEditorDialog(DialogKind kind, string field = "")
        {
            if (_editor == null || ModalOpen) return;
            _dialogScope = new PoseForceDialogScope<HSceneProc>(_editorScene, _editor.Asset, _editorChara, _editorState, field);
            _targetField = kind == DialogKind.Target ? field : null;
            _copyField = kind == DialogKind.Copy ? field : null;
            if (kind == DialogKind.Copy) { CopyStates.Clear(); _copyScroll = Vector2.zero; }
            if (kind == DialogKind.Target) { _targetRole = 0; _targetSearch = ""; _targetScroll = Vector2.zero; }
            if (kind == DialogKind.Restore) _restoreScroll = Vector2.zero;
            ShowDialog(kind);
        }

        private static void ShowDialog(DialogKind kind)
        {
            _dialog = kind; _dialogStatus = ""; _clearEditorFocus = true; _dialogBodyScroll = Vector2.zero;
            float width = Mathf.Min(760, Mathf.Max(320, Screen.width - 20));
            float height = Mathf.Min(650, Mathf.Max(280, Screen.height - 30));
            _dialogRect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
        }

        private static void CloseDialog()
        {
            _dialog = DialogKind.None; _dialogScope = null; _targetField = _copyField = null;
            _clearEditorFocus = true;
        }

        private static void CloseEditorDialog()
        {
            if (_dialog != DialogKind.Import && _dialog != DialogKind.Export) CloseDialog();
        }

        private static void ValidateEditorDialog(string asset)
        {
            if (_dialogScope != null && !_dialogScope.Matches(_proc, asset, _editorChara, _editorState)) CloseDialog();
        }

        private static void DrawDialog()
        {
            // 入力欄が Esc を消費する前に処理する。
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            { CloseDialog(); Event.current.Use(); return; }
            Color previous = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
            string title = _dialog == DialogKind.Target ? "手の付け先を選ぶ" : _dialog == DialogKind.Copy ? "他の状態へ写す"
                : _dialog == DialogKind.Restore ? "kPlug・内蔵の値に戻す" : _dialog == DialogKind.Export ? "自分の値の書き出し" : "書き出し・共有データの取り込み";
            _dialogRect = FitWindow(_dialogRect);
            _dialogRect = GUI.ModalWindow(0x5F0D0, _dialogRect, DialogWindow, title);
        }

        private static void DialogWindow(int id)
        {
            GUILayout.BeginArea(new Rect(12, 26, _dialogRect.width - 24, _dialogRect.height - 38));
            if (_dialogScope != null) GUILayout.Label(_dialogScope.Asset + " / " + PoseForceActors.Label(_dialogScope.Chara) + " / " + _dialogScope.State, EditorLabel);
            _dialogBodyScroll = GUILayout.BeginScrollView(_dialogBodyScroll, GUILayout.Height(Mathf.Max(90, _dialogRect.height - 110)));
            if (_dialog == DialogKind.Target) DrawTargetChoices();
            else if (_dialog == DialogKind.Copy) DrawCopyChoices();
            else if (_dialog == DialogKind.Restore) DrawRestoreChoices();
            else if (_dialog == DialogKind.Import) DrawImportDialog();
            else if (_dialog == DialogKind.Export) DrawExportDialog();
            if (_dialogStatus.Length > 0) GUILayout.Label(_dialogStatus, EditorLabel);
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("閉じる（Esc）")) CloseDialog();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }
    }
}
