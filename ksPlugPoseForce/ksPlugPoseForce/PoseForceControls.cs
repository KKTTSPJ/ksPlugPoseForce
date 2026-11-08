using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ksPlugPoseForce
{
    internal static class PoseForceControls
    {
        private static ConfigEntry<KeyboardShortcut> _up, _down;
        private static PoseForceAmounts _amounts;
        private static HSceneProc _scene;
        private static string _asset;
        private static bool _loading, _keyWrite, _keysChanged;
        private static readonly PoseForceKeyRepeat UpRepeat = new PoseForceKeyRepeat(), DownRepeat = new PoseForceKeyRepeat();
        private static int _inputFrame = -1;
        private static bool _upDown, _upHeld, _downDown, _downHeld;
        private static int _keyDirection;

        internal static bool SuppressForceLog { get { return _loading || _keyWrite; } }

        internal static void BindConfig(ConfigFile config)
        {
            _up = config.Bind("PoseForce", "ForceUpKey", KeyboardShortcut.Empty,
                "Hシーンで補正量を今の値から 0.1 上げるキー。既定は未割り当て。0.4 秒長押し後は 0.1 秒ごとに繰り返します。文字の入力中は効きません。ほかのキーを同時に押していると効かないので、修飾キーは組み合わせに含めてください。");
            _down = config.Bind("PoseForce", "ForceDownKey", KeyboardShortcut.Empty,
                "Hシーンで補正量を今の値から 0.1 下げるキー。既定は未割り当て。長押しと入力中の扱いは ForceUpKey と同じです。");
            string directory = PoseForcePlugin.DataDirectory;
            _amounts = new PoseForceAmounts(Path.Combine(directory, "PoseForceAmounts.tsv"),
                message => PoseForcePlugin.Logger.LogWarning("[PoseForce] " + message));
            PoseForce.Force.SettingChanged += ForceChanged;
        }

        private static bool InScene(HSceneProc scene)
        {
            return scene != null && scene.flags != null && !scene.flags.isHSceneEnd;
        }

        private static string Asset(HSceneProc scene)
        {
            return InScene(scene) ? scene.flags.nowAnimationInfo?.paramFemale?.path?.file ?? "" : "";
        }

        internal static void Refresh(HSceneProc scene)
        {
            if (_amounts == null) return;
            string asset = Asset(scene);
            if (!InScene(scene) || string.IsNullOrEmpty(asset))
            {
                if (!ReferenceEquals(_scene, null) || _asset != null) EndScene();
                return;
            }
            if (_scene == scene && _asset == asset) return;
            FinishKeys();
            _amounts.Save(Time.unscaledTime, true);
            _scene = scene;
            _asset = asset;
            _loading = true;
            try { PoseForce.Force.Value = _amounts.Get(asset); }
            finally { _loading = false; }
            PoseForcePlugin.Logger.LogInfo("[PoseForce] 体位の補正量を読み込みました（" + asset + "・" + Format(PoseForce.Force.Value) + "）。");
        }

        private static void ForceChanged(object sender, EventArgs args)
        {
            if (_loading || _amounts == null || !InScene(_scene) || _asset != Asset(_scene)) return;
            _amounts.Set(_asset, PoseForce.Force.Value, Time.unscaledTime);
        }

        // 画面上の入力をリセットするより前に取得する。焦点の判定と適用は通常の Update で行う。
        internal static void CaptureInput()
        {
            if (_up == null || _inputFrame == Time.frameCount) return;
            _inputFrame = Time.frameCount;
            _upDown = _up.Value.IsDown(); _upHeld = _up.Value.IsPressed();
            _downDown = _down.Value.IsDown(); _downHeld = _down.Value.IsPressed();
        }

        internal static bool EditingText()
        {
            if (GUIUtility.keyboardControl != 0) return true;
            var current = EventSystem.current;
            var selected = current != null ? current.currentSelectedGameObject : null;
            if (selected == null) return false;
            var input = selected.GetComponent<InputField>();
            return input != null && input.isFocused;
        }

        internal static void Tick(HSceneProc scene, bool enabled)
        {
            Refresh(scene);
            if (_amounts == null) return;
            _amounts.Save(Time.unscaledTime, false);
            CaptureInput();
            if (!enabled || !InScene(_scene) || string.IsNullOrEmpty(_asset) || !Application.isFocused || PoseForce.ModalOpen || EditingText())
            {
                FinishKeys();
                return;
            }
            if ((_upDown || _upHeld) && (_downDown || _downHeld)) { FinishKeys(); return; }
            // 反対のキーに切り替えたときも、前の操作のログを先に確定する。
            int direction = _upDown || _upHeld ? 1 : _downDown || _downHeld ? -1 : 0;
            if (_keysChanged && direction != _keyDirection) FinishKeys();
            int steps = UpRepeat.Tick(_upDown, _upHeld, Time.unscaledTime) - DownRepeat.Tick(_downDown, _downHeld, Time.unscaledTime);
            if (steps == 0) return;
            float next = PoseForceAmounts.Step(PoseForce.Force.Value, steps);
            if (next == PoseForce.Force.Value) return;
            _keyWrite = true;
            try { PoseForce.Force.Value = next; _keysChanged = true; _keyDirection = Math.Sign(steps); }
            finally { _keyWrite = false; }
        }

        private static void FinishKeys()
        {
            UpRepeat.Reset(); DownRepeat.Reset();
            _keyDirection = 0;
            if (!_keysChanged) return;
            _keysChanged = false;
            PoseForcePlugin.Logger.LogInfo("[PoseForce] キーで補正量を変更しました（" + _asset + "・" + Format(PoseForce.Force.Value) + "）。");
        }

        internal static void EndScene(HSceneProc scene)
        {
            if (ReferenceEquals(_scene, scene)) EndScene();
        }

        private static void EndScene()
        {
            FinishKeys();
            Flush();
            _scene = null; _asset = null;
        }

        internal static void Flush()
        {
            if (_amounts != null) _amounts.Save(Time.unscaledTime, true);
        }

        private static string Format(float value) { return PoseForceAmounts.Normalize(value).ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
