using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace ksPlugPoseForce
{
    /// <summary>自分の値の書き出しの画面（0.1.3）。書き出しは次の Update で行う（取り込みと同じ）。</summary>
    internal static partial class PoseForce
    {
        private static ConfigEntry<string> _shareName, _shareAuthor;
        private static string _shareNameText = "", _shareAuthorText = "", _exportStatus = "", _exportAsset;
        private static bool _shareCurrentPose, _pendingExport;

        private static string ImportDirectory { get { return Path.Combine(PoseForcePlugin.DataDirectory, PoseForceShare.ImportFolder); } }
        private static string ExportDirectory { get { return Path.Combine(PoseForcePlugin.DataDirectory, PoseForceShare.ExportFolder); } }

        private static void BindShareConfig(ConfigFile config)
        {
            const string s = "PoseForce";
            _shareName = config.Bind(s, "ShareName", "", new ConfigDescription("自分の値を書き出すときのデータの名前（書き出しの画面で入力）。", null,
                new ConfigurationManagerAttributes { Browsable = false }));
            _shareAuthor = config.Bind(s, "ShareAuthor", "", new ConfigDescription("自分の値を書き出すときの作者名（書き出しの画面で入力）。", null,
                new ConfigurationManagerAttributes { Browsable = false }));
            config.Bind(s, "ExportShare", false, new ConfigDescription("自分で編集・保存した値を、ほかの人と共有するファイルに書き出します（export フォルダ）。", null,
                new ConfigurationManagerAttributes { CustomDrawer = entry => { if (GUILayout.Button("自分の値の書き出し画面を開く…")) OpenExportDialog(); } }));
        }

        private static void OpenExportDialog()
        {
            if (ModalOpen) return;
            _dialogScope = null;
            _shareNameText = _shareName.Value; _shareAuthorText = _shareAuthor.Value;
            _exportAsset = _proc != null && _proc.flags != null && !_proc.flags.isHSceneEnd ? _proc.flags.nowAnimationInfo?.paramFemale?.path?.file : null;
            if (string.IsNullOrEmpty(_exportAsset)) { _exportAsset = null; _shareCurrentPose = false; }
            _exportStatus = "";
            TryCreateDirectory(ExportDirectory);
            ShowDialog(DialogKind.Export);
        }

        private static void DrawExportDialog()
        {
            GUILayout.Label("自分で編集して保存した値（origin=user の行と、欄を消す指定）を、ほかの人と共有するファイルに書き出します。"
                + "体位を止める設定・取り込んだ kPlug の値・取り込んだ共有データは含めません。保存していない編集も含めません。", EditorLabel);
            GUILayout.Label("kPlug の書き出し（import フォルダなど）と同じ値の行は書き出しません。kPlug の値を写しただけの行が配られないようにするためです。"
                + "ただし、kPlug の値を別の状態に写した行などは見分けられないので、配る前に中身を確かめてください。", EditorLabel);
            if (_editor != null && _editor.Dirty) GUILayout.Label("編集画面に保存していない編集があります（書き出しには含まれません）。", EditorLabel);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !_pendingExport;
            GUILayout.BeginHorizontal();
            GUILayout.Label("データの名前", GUILayout.Width(110));
            _shareNameText = GUILayout.TextField(_shareNameText ?? "", PoseForceTable.MaxPackName);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("作者", GUILayout.Width(110));
            _shareAuthorText = GUILayout.TextField(_shareAuthorText ?? "", 64);
            GUILayout.EndHorizontal();
            GUILayout.Label("名前は取り込む側で、どのデータの値かを見分けるのに使います（同じ名前で取り込み直すと置き換わります）。空なら \"share\"。", EditorLabel);
            GUI.enabled = enabled && !_pendingExport && _exportAsset != null;
            _shareCurrentPose = GUILayout.Toggle(_shareCurrentPose && _exportAsset != null,
                _exportAsset != null ? "今の体位だけ（" + _exportAsset + "）" : "今の体位だけ（Hシーンの中で選べます）");
            GUI.enabled = enabled && !_pendingExport;
            if (GUILayout.Button(_shareCurrentPose ? "今の体位の自分の値を書き出す" : "全体位の自分の値を書き出す")) _pendingExport = true;
            GUI.enabled = enabled;
            GUILayout.Label("書き出し先: " + ExportDirectory, EditorLabel);
            if (_exportStatus.Length > 0) GUILayout.Label(_exportStatus, EditorLabel);
        }

        private static void ProcessExport()
        {
            if (!_pendingExport) return;
            _pendingExport = false;
            try
            {
                _shareName.Value = (_shareNameText ?? "").Trim();
                _shareAuthor.Value = (_shareAuthorText ?? "").Trim();
                var references = PoseForceShare.ImportFiles(PoseForcePlugin.DataDirectory);
                var result = PoseForceShare.Export(TablePath, ExportDirectory, _shareName.Value, _shareAuthor.Value,
                    _shareCurrentPose ? _exportAsset : null, references, PoseForcePlugin.Version, DateTime.Now);
                _exportStatus = "書き出しました: " + Path.GetFileName(result.Path) + "（データの名前「" + result.Name + "」、" + result.Assets + " 体位・" + result.Rows + " 行）。"
                    + (result.KPlugReference ? "kPlug の書き出しと同じ値の行 " + result.SameAsKPlug + " 行は除きました。"
                        : "kPlug の書き出しが見つからないため、kPlug と同じ値の行は見分けていません。");
                PoseForcePlugin.Logger.LogInfo(Tag + " " + _exportStatus + " / " + result.Path);
            }
            catch (Exception ex)
            {
                _exportStatus = "書き出せませんでした: " + ex.Message;
                PoseForcePlugin.Logger.LogWarning(Tag + " " + _exportStatus);
            }
        }

        private static void TryCreateDirectory(string path)
        {
            try { Directory.CreateDirectory(path); }
            catch (Exception ex) { PoseForcePlugin.Logger.LogWarning(Tag + " フォルダを作れません: " + path + ": " + ex.Message); }
        }
    }
}
