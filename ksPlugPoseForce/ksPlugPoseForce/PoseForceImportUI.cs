using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ksPlugPoseForce
{
    internal static partial class PoseForce
    {
        private static string[] _importFiles = new string[0], _importLabels = new string[0];
        private static string _pendingImport, _importStatus = "取り込むファイルを import フォルダに置いてください。";
        private static int _importIndex;
        private static Vector2 _importScroll, _importLogScroll;
        private static readonly List<string> ImportLog = new List<string>();

        private static void OpenImportDialog()
        {
            if (ModalOpen) return;
            _dialogScope = null;
            ShowDialog(DialogKind.Import);
            TryCreateDirectory(ImportDirectory);
            FindImportFiles();
        }

        private static void DrawImportDialog()
        {
            GUILayout.Label("import フォルダに置いた kPlug の書き出し（kPlugPose_*.tsv）や、ほかの人の共有データ（PoseForceShare_*.tsv、PoseForce.tsv をそのまま置いても可）を取り込みます。"
                + "自分の表（PoseForce.tsv）は対象になりません。", EditorLabel);
            GUILayout.Label("同じ項目は、自分で直した値 ＞ 共有データ ＞ kPlug の値 ＞ 内蔵の値 の順に優先します（共有データ同士は後から取り込んだ方）。"
                + "同じファイル（同じデータの名前）を取り込み直すと、その分の値を置き換えます。PoseForce.tsv の前の版は .bak に残し、未保存の編集は破棄されます。", EditorLabel);
            GUILayout.Label("取り込むフォルダ: " + ImportDirectory, EditorLabel);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && _pendingImport == null;
            if (GUILayout.Button("ファイルを再検索")) FindImportFiles();
            if (_importFiles.Length > 0)
            {
                GUILayout.Label("取り込むファイルを選択（新しい順）");
                _importScroll = GUILayout.BeginScrollView(_importScroll, GUILayout.Height(Mathf.Max(60, _dialogRect.height * 0.28f)));
                _importIndex = GUILayout.SelectionGrid(_importIndex, _importLabels, 1);
                GUILayout.EndScrollView();
                GUILayout.Label("選択中: " + _importLabels[_importIndex], EditorLabel);
                if (GUILayout.Button("選んだファイルを取り込む")) _pendingImport = _importFiles[_importIndex];
            }
            GUI.enabled = enabled;
            GUILayout.Label(_importStatus, EditorLabel);
            GUILayout.Label("取り込み状況（この起動中の記録）");
            _importLogScroll = GUILayout.BeginScrollView(_importLogScroll, GUILayout.Height(Mathf.Max(70, _dialogRect.height * 0.22f)));
            foreach (string line in ImportLog) GUILayout.Label(line, EditorLabel);
            GUILayout.EndScrollView();
        }

        private static void AddImportLog(string message)
        {
            _importStatus = message;
            ImportLog.Add(DateTime.Now.ToString("HH:mm:ss") + " " + message);
            if (ImportLog.Count > 80) ImportLog.RemoveAt(0);
            _importLogScroll.y = float.MaxValue;
        }

        private static void FindImportFiles()
        {
            try
            {
                _importFiles = PoseForceShare.ImportFiles(PoseForcePlugin.DataDirectory);
                _importLabels = Array.ConvertAll(_importFiles, ImportLabel);
                _importIndex = 0;
                if (_importFiles.Length == 0)
                {
                    AddImportLog("取り込むファイルがありません: " + ImportDirectory);
                    PoseForcePlugin.Logger.LogInfo(Tag + " " + _importStatus);
                }
                else AddImportLog("取り込むファイルを " + _importFiles.Length + " 件見つけました。選択して実行してください。");
            }
            catch (Exception ex) { ImportError(ex); }
        }

        /// <summary>一覧の表示: フォルダ（import か直下）・ファイル名・種類（kPlug の書き出し／共有データの名前と行数／取り込めない理由）。</summary>
        private static string ImportLabel(string path)
        {
            string place = Path.GetFileName(Path.GetDirectoryName(path)) == PoseForceShare.ImportFolder ? PoseForceShare.ImportFolder + "/" : "";
            try
            {
                var source = PoseForceImport.ReadSource(path, null);
                return place + Path.GetFileName(path) + (source.Kind == PoseForceImport.SourceKind.KPlug
                    ? "（kPlug の書き出し、" + source.Rows.Count + " 行）"
                    : "（共有データ「" + source.Name + "」、" + source.Rows.Count + " 行）");
            }
            catch (Exception ex) { return place + Path.GetFileName(path) + "（取り込めない: " + ex.Message + "）"; }
        }

        private static void ImportError(Exception ex)
        {
            AddImportLog("取り込めませんでした: " + ex.Message);
            PoseForcePlugin.Logger.LogWarning(Tag + " " + _importStatus);
        }

        private static void ProcessImport()
        {
            if (_pendingImport == null) return;
            string path = _pendingImport;
            _pendingImport = null;
            try
            {
                AddImportLog("読み込み開始: " + Path.GetFileName(path));
                Action<string> warning = message => { AddImportLog("警告: " + message); PoseForcePlugin.Logger.LogWarning(Tag + " " + message); };
                var merged = PoseForceImport.ReadAndMerge(TablePath, path, warning);
                AddImportLog("検証完了: " + (merged.Source.Kind == PoseForceImport.SourceKind.KPlug ? "kPlug の書き出し " : "共有データ「" + merged.Source.Name + "」 ")
                    + merged.SourceRows.Count + " 行" + (merged.Source.Skipped > 0 ? "（取り込まない行 " + merged.Source.Skipped + "：kPlug・ほかの共有データ・内蔵の値、体位を止める設定）" : "")
                    + "。利用者の表へ保存します。");
                var next = PoseForceTable.Create(null, warning);
                next.Overlay(merged.Rows);
                string compatibility = ImportCompatibility(merged.SourceRows);
                PoseForceTable.Write(TablePath, merged.Rows);
                InstallTable(next);
                _lastImportPath = path;
                AddImportLog("取り込み完了: 追加 " + merged.Added + "・置き換え " + merged.Replaced + "・保持 " + merged.Kept + "・削除 " + merged.Removed
                    + (merged.Shadowed > 0 ? "・優先の高い値があるため使わない " + merged.Shadowed : "") + "。" + compatibility);
                PoseForcePlugin.Logger.LogInfo(Tag + " " + _importStatus + " / " + Path.GetFileName(path));
            }
            catch (Exception ex) { ImportError(ex); }
        }

        private static string ImportCompatibility(IEnumerable<PoseForceRow> rows)
        {
            var field = typeof(HSceneProc).GetField("lstAnimInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            var lists = _proc != null && field != null ? field.GetValue(_proc) as List<HSceneProc.AnimationListInfo>[] : null;
            if (lists == null) return "KKS のアセット対応数は Hシーンで取り込むと表示します。";
            var all = new HashSet<string>(StringComparer.Ordinal);
            var insertion = new HashSet<string>(StringComparer.Ordinal);
            var threePeople = new HashSet<string>(StringComparer.Ordinal);
            foreach (var list in lists)
            {
                if (list == null) continue;
                foreach (var info in list)
                {
                    string asset = info?.paramFemale?.path?.file;
                    if (string.IsNullOrEmpty(asset)) continue;
                    all.Add(asset);
                    if (info.mode == HFlag.EMode.sonyu) insertion.Add(asset);
                    else if (info.mode == HFlag.EMode.sonyu3P) threePeople.Add(asset);
                }
            }
            if (all.Count == 0) return "アセットの一覧の初期化後に対応数を確認できます。";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int supported = 0, three = 0, missing = 0, excluded = 0;
            foreach (var row in rows)
            {
                if (!seen.Add(row.Asset)) continue;
                if (insertion.Contains(row.Asset)) ++supported;
                else if (threePeople.Contains(row.Asset)) ++three;
                else if (!all.Contains(row.Asset)) ++missing;
                else ++excluded;
            }
            return "２人挿入 " + supported + " 体位・３人挿入 " + three + " 体位（アナル用の値を含む）・KKS にアセットが無い " + missing + "・対象外の種類 " + excluded + "。";
        }
    }
}
