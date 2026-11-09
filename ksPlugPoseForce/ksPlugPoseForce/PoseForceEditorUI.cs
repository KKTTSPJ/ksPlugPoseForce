using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ksPlugPoseForce
{
    internal static partial class PoseForce
    {
        private static Vector2 _editorScroll, _stateScroll, _targetScroll, _copyScroll, _restoreScroll;
        private static bool _showStates, _showFields;
        private static bool _clearEditorFocus;
        private static int _newFieldIndex, _restoreIndex;
        private static string _targetField, _targetSearch = "", _customTarget = "", _copyField;
        private static readonly HashSet<string> CopyStates = new HashSet<string>(StringComparer.Ordinal);
        private static GUIStyle _editorLabel;
        private static GUIStyle EditorLabel { get { return _editorLabel ?? (_editorLabel = new GUIStyle(GUI.skin.label) { wordWrap = true }); } }

        private static void ResetEditorSelection()
        {
            NumberEdits.Clear(); CopyStates.Clear();
            CloseEditorDialog();
            _showStates = _showFields = false; _targetField = _copyField = null;
            _clearEditorFocus = true;
        }

        private static void ResetEditorUI()
        {
            ResetEditorSelection(); _editorScroll = Vector2.zero;
        }

        private static void DrawEditor()
        {
            if (_editor == null)
            {
                GUILayout.Label(_editorStatus.Length > 0 ? _editorStatus : "２人・３人の挿入で体位を選ぶと編集できます（アナルを含む）。", EditorLabel);
                return;
            }
            GUILayout.Label(_editor.Dirty ? "未保存の編集をプレビュー中" : "保存済みの値を表示中");
            GUILayout.Label(_editorStatus, EditorLabel);
            bool second = HasSecondFemale();
            string[] roles = second ? new[] { "female", "male", "female2" } : new[] { "female", "male" };
            string[] labels = second ? new[] { "女１", "男", "女２" } : new[] { "女", "男" };
            int actor = Math.Max(0, Array.IndexOf(roles, _editorChara));
            string chara = roles[GUILayout.Toolbar(actor, labels)];
            if (chara != _editorChara) { _editorChara = chara; ResetEditorSelection(); }
            GUILayout.BeginHorizontal();
            _followState = GUILayout.Toggle(_followState, "今の状態に追従", GUILayout.Width(145));
            if (GUILayout.Button("状態: " + _editorState, GUILayout.Width(225))) _showStates = !_showStates;
            GUILayout.EndHorizontal();
            if (_showStates) DrawStateChoices();

            var rows = new List<PoseForceRow>(_editorView.Get(_editor.Asset, _editorChara, _editorState));
            rows.Sort((a, b) => StringComparer.Ordinal.Compare(a.Field, b.Field));
            if (rows.Count == 0) GUILayout.Label("この人・状態には補正項目がありません。下の追加ボタンから設定できます。", EditorLabel);
            var live = LiveRig(chara);
            if (rows.Count > 0 && live == null) GUILayout.Label("欄ごとの「効くか」は、ゲームの今の状態を選んでいるときに表示します。", EditorLabel);
            foreach (var source in rows)
            {
                if (!PoseForceFields.Supports(source.Field)) { GUILayout.Label(source.Field + "（この版では編集・適用しない欄）"); continue; }
                DrawEditorRow(source, live, rows);
            }
            if (GUILayout.Button("補正項目（位置・回転・手のIK）を追加")) _showFields = !_showFields;
            if (_showFields) DrawAddField();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _editor.Dirty;
            if (GUILayout.Button("保存")) _editorAction = SaveEditor;
            if (GUILayout.Button("元に戻す（保存前へ）")) _editorAction = () => { _editor.Undo(); EditorModified(); ResetEditorSelection(); _editorStatus = "未保存の編集を戻しました。"; };
            GUI.enabled = enabled;
            if (GUILayout.Button("kPlug・内蔵の値に戻す…")) OpenEditorDialog(DialogKind.Restore);
            GUILayout.EndHorizontal();
            bool disabled = _editorView.IsDisabled(_editor.Asset);
            if (GUILayout.Button(disabled ? "この体位を補正する（未保存）" : "この体位を補正しない（未保存）"))
                _editorAction = () => { _editor.SetDisabled(!disabled); EditorModified(); };
            GUILayout.Label("体位・Hシーンを変えると未保存の編集を破棄します。画面を閉じるだけなら保持します。", EditorLabel);
        }

        private static void DrawStateChoices()
        {
            GUILayout.Label("● はこの人の欄がある状態。選んでもゲームの状態は変更しません。");
            _stateScroll = GUILayout.BeginScrollView(_stateScroll, GUILayout.Height(125));
            for (int start = 0; start < _editorStates.Length; start += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = start; i < Math.Min(start + 3, _editorStates.Length); ++i)
                {
                    string state = _editorStates[i];
                    if (GUILayout.Button((_editorView.Get(_editor.Asset, _editorChara, state).Count > 0 ? "● " : "") + state))
                    { _editorState = state; _followState = false; ResetEditorSelection(); }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        /// <summary>編集中の体位・状態がゲームの今の体位・状態なら、その人の Rig（欄ごとの「効くか」の表示用）。</summary>
        private static Rig LiveRig(string chara)
        {
            if (_editor == null || _proc == null || _proc.flags == null) return null;
            if ((_proc.flags.nowAnimationInfo?.paramFemale?.path?.file ?? "") != _editor.Asset) return null;
            if ((_proc.flags.nowAnimStateName ?? "") != _editorState) return null;
            var rig = chara == "female" ? _female : chara == "female2" ? _female2 : chara == "male" ? _maleRig : null;
            return rig != null && rig.Ik != null ? rig : null;
        }

        private static void DrawEditorRow(PoseForceRow source, Rig live, IList<PoseForceRow> rows)
        {
            var row = source.Copy();
            GUILayout.Space(4); GUILayout.BeginHorizontal();
            GUILayout.Label(row.Field + "  [" + row.Origin + "]");
            if (GUILayout.Button("他の状態へ写す…", GUILayout.Width(135))) OpenEditorDialog(DialogKind.Copy, row.Field);
            if (GUILayout.Button("この欄を消す", GUILayout.Width(115)))
                _editorAction = () => { _editor.Delete(_editorChara, _editorState, row.Field); EditorModified(); ResetEditorSelection(); };
            GUILayout.EndHorizontal();
            if (live != null)
            {
                string status = live.FieldStatus(row.Field, rows);
                if (status.Length > 0) GUILayout.Label("今の状態で: " + status, EditorLabel);
            }
            if (PoseForceFields.IsHand(row.Field))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("付け先: " + row.Target + (ResolveHandTarget(row.Target) == null ? "（骨が見つかりません）" : ""), EditorLabel);
                if (GUILayout.Button("付け先を選ぶ…", GUILayout.Width(130))) { _customTarget = row.Target; OpenEditorDialog(DialogKind.Target, row.Field); }
                GUILayout.EndHorizontal();
            }
            bool rotation = !PoseForceFields.IsHand(row.Field) && !row.Field.EndsWith("_pos", StringComparison.Ordinal);
            string key = "PoseForceEdit_" + row.Key;
            row.X = EditorNumber(key + "x", rotation ? "X（度）" : "X（m）", row.X, rotation);
            if (row.Field != "spine_roll")
            {
                row.Y = EditorNumber(key + "y", rotation ? "Y（度）" : "Y（m）", row.Y, rotation);
                row.Z = EditorNumber(key + "z", rotation ? "Z（度）" : "Z（m）", row.Z, rotation);
            }
            if (row.X != source.X || row.Y != source.Y || row.Z != source.Z) SetEditorRow(row);
        }

        private static void SetEditorRow(PoseForceRow row)
        {
            try { _editor.Set(row); EditorModified(); }
            catch (Exception ex) { EditorError(ex); }
        }

        private static void DrawAddField()
        {
            _newFieldIndex = GUILayout.SelectionGrid(_newFieldIndex, EditorFields, 4);
            string field = EditorFields[_newFieldIndex];
            bool exists = false;
            foreach (var row in _editorView.Get(_editor.Asset, _editorChara, _editorState)) if (row.Field == field) exists = true;
            bool enabled = GUI.enabled; GUI.enabled = enabled && !exists;
            if (GUILayout.Button(exists ? "この欄はすでにあります" : field + " を追加"))
                _editorAction = () =>
                {
                    string target = "";
                    if (PoseForceFields.IsHand(field))
                    {
                        foreach (string candidate in _editorTargets) if (ResolveHandTarget(candidate) != null) { target = candidate; break; }
                        if (target.Length == 0) throw new InvalidDataException("付け先の候補の骨が見つかりません。");
                    }
                    var row = new PoseForceRow { Asset = _editor.Asset, KPlug = EditorKPlugName(), Chara = _editorChara, State = _editorState,
                        Field = field, Target = target, Origin = "user" };
                    SetEditorRow(row); _showFields = false;
                    if (PoseForceFields.IsHand(field)) { _customTarget = target; OpenEditorDialog(DialogKind.Target, field); }
                };
            GUI.enabled = enabled;
        }

        private static string EditorKPlugName()
        {
            return _editorKPlugName;
        }

        private static void DrawTargetChoices()
        {
            GUILayout.Label(_targetField + " の付け先（人:骨名）");
            bool second = HasSecondFemale();
            if (!second && _targetRole == 3) _targetRole = 0;
            _targetRole = GUILayout.Toolbar(_targetRole, second ? new[] { "すべて", "女１の骨", "男の骨", "女２の骨" } : new[] { "すべて", "女の骨", "男の骨" });
            GUILayout.Label("骨名で絞り込み:");
            GUI.SetNextControlName("PoseForceEdit_TargetSearch");
            _targetSearch = GUILayout.TextField(_targetSearch);
            _targetScroll = GUILayout.BeginScrollView(_targetScroll, GUILayout.Height(Mathf.Max(70, _dialogRect.height - 290)));
            int column = 0;
            foreach (string target in _editorTargets)
            {
                if (_targetSearch.Length > 0 && target.IndexOf(_targetSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if ((_targetRole == 1 && !target.StartsWith("female:", StringComparison.Ordinal)) || (_targetRole == 2 && !target.StartsWith("male:", StringComparison.Ordinal))
                    || (_targetRole == 3 && !target.StartsWith("female2:", StringComparison.Ordinal))) continue;
                if (column == 0) GUILayout.BeginHorizontal();
                if (GUILayout.Button(target)) ApplyEditorTarget(target);
                if (++column == 2) { GUILayout.EndHorizontal(); column = 0; }
            }
            if (column != 0) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal(); GUI.SetNextControlName("PoseForceEdit_CustomTarget");
            _customTarget = GUILayout.TextField(_customTarget);
            if (GUILayout.Button("この付け先を使う", GUILayout.Width(150))) ApplyEditorTarget(_customTarget.Trim());
            GUILayout.EndHorizontal();
        }

        private static void ApplyEditorTarget(string target)
        {
            var scope = _dialogScope;
            if (_editor == null || scope == null || !scope.Matches(_proc, _editor.Asset, _editorChara, _editorState)) { CloseDialog(); return; }
            if (PoseForceActors.TargetRole(target).Length == 0 || ResolveHandTarget(target) == null)
            { _dialogStatus = "付け先の骨が見つかりません: " + target; return; }
            foreach (var value in _editorView.Get(scope.Asset, scope.Chara, scope.State))
                if (value.Field == scope.Field) { var row = value.Copy(); row.Target = target; SetEditorRow(row); break; }
            CloseDialog();
        }

        private static void DrawCopyChoices()
        {
            GUILayout.Label(_copyField + " の値と付け先を、選んだ状態へ写す（同じ人）。");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("すべて選択")) foreach (string state in _editorStates) if (state != _editorState) CopyStates.Add(state);
            if (GUILayout.Button("選択を解除")) CopyStates.Clear();
            GUILayout.EndHorizontal();
            _copyScroll = GUILayout.BeginScrollView(_copyScroll, GUILayout.Height(Mathf.Max(70, _dialogRect.height - 260)));
            for (int start = 0; start < _editorStates.Length; start += 3)
            {
                GUILayout.BeginHorizontal();
                for (int i = start; i < Math.Min(start + 3, _editorStates.Length); ++i)
                {
                    string state = _editorStates[i]; if (state == _editorState) continue;
                    bool on = GUILayout.Toggle(CopyStates.Contains(state), state);
                    if (on) CopyStates.Add(state); else CopyStates.Remove(state);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            bool enabled = GUI.enabled; GUI.enabled = enabled && CopyStates.Count > 0;
            if (GUILayout.Button("選んだ状態へ写す（未保存）"))
            {
                string chara = _dialogScope.Chara, state = _dialogScope.State, field = _dialogScope.Field;
                string[] destinations = new List<string>(CopyStates).ToArray();
                _editorAction = () => { _editor.CopyTo(chara, state, field, destinations); EditorModified(); };
                CloseDialog();
            }
            GUI.enabled = enabled;
            GUILayout.EndHorizontal();
        }

        private static void DrawRestoreChoices()
        {
            GUILayout.Label("この体位の user 行と削除指定を取り除きます。戻した値は『保存』まで未保存です。", EditorLabel);
            if (_restoreFiles.Length == 0) GUILayout.Label("内蔵の値と未編集の kplug 行を基準に戻します。内蔵にない独自の補正値は、元の書き出しが無ければ復元できません。", EditorLabel);
            else
            {
                GUILayout.Label("復元元の書き出し（選んだファイルの値をこの体位へ戻します）:");
                var labels = Array.ConvertAll(_restoreFiles, Path.GetFileName);
                _restoreScroll = GUILayout.BeginScrollView(_restoreScroll, GUILayout.Height(Mathf.Max(70, _dialogRect.height - 245)));
                _restoreIndex = GUILayout.SelectionGrid(_restoreIndex, labels, 1);
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("この体位の手動編集を基準に戻す（未保存）"))
            {
                string path = _restoreFiles.Length > 0 ? _restoreFiles[_restoreIndex] : null;
                _editorAction = () => RestoreEditorSource(path);
                CloseDialog();
            }
            GUILayout.Space(8);
            GUILayout.Label("内蔵の値を自分の値にする: この体位の内蔵の値を、自分の値（origin=user）として写します。自分の値は kPlug・共有データの値より優先するので、"
                + "kPlug の値の代わりに内蔵の値を使いたい体位で使います。「保存」まで未保存です。", EditorLabel);
            int builtin = _editor != null ? _editor.BuiltinRowCount : 0;
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && builtin > 0;
            if (GUILayout.Button(builtin > 0 ? "内蔵の値を自分の値にする（この体位、" + builtin + " 行、未保存）" : "この体位に内蔵の値はありません"))
            {
                _editorAction = UseBuiltinValues;
                CloseDialog();
            }
            GUI.enabled = enabled;
        }
    }
}
