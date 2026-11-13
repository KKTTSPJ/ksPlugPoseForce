using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ksPlugPoseForce
{
    internal static partial class PoseForce
    {
        private static PoseForceEdits _editor;
        private static PoseForceTable _editorView;
        private static HSceneProc _editorScene;
        private static bool _editorPreview, _editorChanged, _followState = true;
        private static string _editorStatus = "", _editorState = "", _editorChara = "female", _editorLoadKey;
        private static Action _editorAction;
        private static string[] _editorStates = new string[0], _editorTargets = new string[0], _restoreFiles = new string[0];
        private static string _lastImportPath;
        private static string _editorKPlugName = "";

        private static void EditorTick()
        {
            string asset = _proc != null && _proc.flags != null && !_proc.flags.isHSceneEnd
                && SupportedMode(_proc.flags.mode)
                ? _proc.flags.nowAnimationInfo?.paramFemale?.path?.file ?? "" : "";
            if (_editor != null && (_editor.Asset != asset || _editorScene != _proc))
            {
                bool restore = _editorPreview;
                ForgetEditor("体位または Hシーンの変更");
                if (restore)
                {
                    try { UseEditorTable(PoseForceTable.Create(TablePath, message => PoseForcePlugin.Logger.LogWarning(Tag + " " + message))); }
                    catch (Exception ex) { Fail("保存済みの表への復元", ex); }
                }
            }
            if (asset.Length == 0) { _editorLoadKey = null; return; }
            if (_editorOpen && _editor == null && _editorLoadKey != asset)
            {
                _editorLoadKey = asset;
                try
                {
                    _editor = PoseForceEdits.Load(asset, TablePath); _editorScene = _proc;
                    if (_editorChara == "female2" && !HasSecondFemale()) _editorChara = "female";
                    _editorView = _editor.Preview();
                    _editorState = _proc.flags.nowAnimStateName ?? "Idle"; _followState = true;
                    _editorStatus = "数値は補正量 1 のときの値です。編集は未保存でプレビューします。";
                    ResetEditorUI(); BuildEditorChoices();
                }
                catch (Exception ex) { EditorError(ex); }
            }
            if (_editor == null) return;
            ValidateEditorDialog(asset);
            var action = _editorAction; _editorAction = null;
            try
            {
                if (action != null) action();
                if (_editorChanged)
                {
                    UseEditorTable(_editorView);
                    _editorPreview = _editor.Dirty;
                    _editorChanged = false;
                }
            }
            catch (Exception ex) { EditorError(ex); }
            string live = _proc.flags.nowAnimStateName ?? "";
            if (!ModalOpen && _followState && live.Length > 0 && _editorState != live) { _editorState = live; ResetEditorSelection(); }
            if (live.Length > 0 && Array.IndexOf(_editorStates, live) < 0)
            {
                var states = new List<string>(_editorStates) { live }; states.Sort(StringComparer.Ordinal); _editorStates = states.ToArray();
            }
        }

        private static void EditorModified() { _editorView = _editor.Preview(); _editorChanged = true; }

        private static void UseEditorTable(PoseForceTable next)
        {
            ClearCorrections();
            _table = next;
        }

        private static void ForgetEditor(string why)
        {
            if (_editor != null && _editor.Dirty) PoseForcePlugin.Logger.LogInfo(Tag + " 未保存の編集を破棄しました（" + _editor.Asset + "・" + why + "）。");
            _editor = null; _editorView = null; _editorScene = null; _editorAction = null; _editorLoadKey = null;
            _editorPreview = _editorChanged = false;
            ResetEditorUI();
        }

        private static void EditorError(Exception ex)
        {
            _editorStatus = "編集を反映できませんでした: " + ex.Message;
            PoseForcePlugin.Logger.LogWarning(Tag + " " + _editorStatus);
        }

        private static void BuildEditorChoices()
        {
            var states = new SortedSet<string>(StringComparer.Ordinal);
            var targets = new SortedSet<string>(StringComparer.Ordinal);
            _editorKPlugName = "";
            if (_editorState.Length > 0) states.Add(_editorState);
            foreach (var row in _table.Rows)
            {
                if (row.Asset == _editor.Asset && !string.IsNullOrEmpty(row.KPlug)) _editorKPlugName = row.KPlug;
                if (row.State != "*" && row.State.Length > 0) states.Add(row.State);
                if (PoseForceActors.TargetRole(row.Target).Length > 0
                    && (PoseForceActors.TargetRole(row.Target) != "female2" || HasSecondFemale())) targets.Add(row.Target);
            }
            foreach (string role in new[] { "female", "male", "female2" })
                foreach (string bone in new[] { "cf_j_hips", "cf_j_waist01", "cf_j_spine01", "cf_j_hand_L", "cf_j_hand_R", "k_f_kosi02_00", "k_f_kosi02_01", "k_f_legupR_00", "k_f_legupL_00" })
                    if (ResolveHandTarget(role + ":" + bone) != null) targets.Add(role + ":" + bone);
            _editorStates = new List<string>(states).ToArray(); _editorTargets = new List<string>(targets).ToArray();
            _restoreFiles = PoseForceShare.ImportFiles(PoseForcePlugin.DataDirectory);
            _restoreIndex = _lastImportPath != null ? Array.IndexOf(_restoreFiles, _lastImportPath) : 0;
            if (_restoreIndex < 0) _restoreIndex = 0;
        }

        private static void SaveEditor()
        {
            bool saved = _editor.Save(); _editorPreview = false;
            _editorStatus = saved ? "保存しました。前の表は .bak に残しました（既存ファイルがあった場合）。" : "保存する変更はありません。";
            PoseForcePlugin.Logger.LogInfo(Tag + " " + _editorStatus + " / " + _editor.Asset);
        }

        private static void RestoreEditorSource(string path)
        {
            IEnumerable<PoseForceRow> source = new PoseForceRow[0];
            string name = "内蔵と未編集の kplug 行";
            if (path != null)
            {
                source = PoseForceImport.ReadSource(path, null).Rows;
                name = Path.GetFileName(path);
            }
            _editor.ResetToSource(source); EditorModified();
            ResetEditorSelection();
            _editorStatus = "手動編集を基準に戻しました（未保存）。復元元: " + name;
        }

        private static void UseBuiltinValues()
        {
            int count = _editor.UseBuiltinAsUser(); EditorModified();
            ResetEditorSelection();
            _editorStatus = "この体位の内蔵の値 " + count + " 行を自分の値にしました（未保存。「保存」で確定）。";
        }
    }
}
