using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace kPlugPoseForceExport
{
    internal sealed class PoseForceExportSampler : IDisposable
    {
        internal sealed class Value
        {
            internal string Chara, Field, Target = "";
            internal Vector3 Offset;
        }

        private sealed class Hand
        {
            internal string Chara, Field, Target;
            internal Transform Scratch;
        }

        private static PoseForceExportSampler _active;
        private readonly Harmony _harmony = new Harmony(PoseForceExportPlugin.GUID);
        private readonly List<MethodInfo> _patched = new List<MethodInfo>();
        private readonly List<GameObject> _scratch = new List<GameObject>();
        private readonly List<Hand> _hands = new List<Hand>();
        private readonly PoseForceExportState _saved = new PoseForceExportState();
        private readonly object _pose, _flag, _ctrl, _config;
        private readonly Type _core, _toolH, _editType;
        private readonly MethodInfo _adapt, _force, _bodyIk;
        private readonly Type _boyListType, _girlListType;
        private readonly Dictionary<string, ChaControl> _characters = new Dictionary<string, ChaControl>();
        private readonly Dictionary<string, object> _offsets = new Dictionary<string, object>();
        private readonly FieldInfo[] _offsetFields;
        private bool _disposed;
        private int _applyCount;

        internal PoseForceExportSampler(Assembly assembly)
        {
            _core = RequireType(assembly, "kPlug.Core");
            var poseType = RequireType(assembly, "kPlug.CmpH.MainPoseCtrl");
            _toolH = RequireType(assembly, "kPlug.Tools.ToolH");
            var chaTools = RequireType(assembly, "kPlug.Tools.ChaTools");
            _editType = RequireType(assembly, "kPlug.Tools.ToolAction+MainIkEdit");
            _bodyIk = RequireMethod(chaTools, "GetBodyIkSolver");
            _ctrl = PoseForceExportState.Get(_core, "hCtrl");
            _flag = PoseForceExportState.Get(_core, "hFlag");
            var proc = PoseForceExportState.Get(_core, "hProc") as UnityEngine.Object;
            _config = PoseForceExportState.Get(_core, "config");
            if (!Alive(_ctrl) || !Alive(_flag) || proc == null || !Alive(_config)
                || (bool)PoseForceExportState.Get(_core, "leavingH") || (bool)PoseForceExportState.Get(_flag, "isHSceneEnd"))
                throw new InvalidOperationException("Hシーンの初期化後に使えます。");
            _pose = PoseForceExportState.Get(_ctrl, "MainPosCtrl");
            if (!Alive(_pose) || !(bool)PoseForceExportState.Get(_pose, "isInit")
                || !Alive(PoseForceExportState.Get(_pose, "bcB")) || !Alive(PoseForceExportState.Get(_pose, "bcG")))
                throw new InvalidOperationException("姿勢補正の初期化を待ってください。");
            if ((bool)PoseForceExportState.Get(_pose, "InChange") || (bool)PoseForceExportState.Get(_pose, "InClearing"))
                throw new InvalidOperationException("体位・状態の切り替えが終わってから使ってください。");
            _adapt = RequireMethod(poseType, "AdaptIkTarget");
            _force = RequireMethod(poseType, "ForceOnPiston");
            _boyListType = PoseForceExportState.Field(poseType, "boyIkEdits").FieldType;
            _girlListType = PoseForceExportState.Field(poseType, "girlIkEdits").FieldType;
            _offsetFields = RequireType(assembly, "kPlug.CmpH.MainPoseCtrl+Offsets").GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in _offsetFields)
                if (field.FieldType != typeof(Vector3) && field.FieldType != typeof(float))
                    throw new NotSupportedException("未対応の補正欄: " + field.Name);
            // 必要なメソッド・欄を、変更を始める前に検証する。
            RequireMethod(poseType, "IE_BoyHandTarget");
            RequireMethod(poseType, "IE_GirlHandTarget");
            RequireMethod(poseType, "ApplyOffsetToBonesCtrl");
            foreach (string name in new[] { "newTarget", "baseLocOffst" }) PoseForceExportState.Field(_editType, name);
            _characters.Add("female", PoseForceExportState.Get(_core, "heroineCha") as ChaControl);
            _characters.Add("male", PoseForceExportState.Get(PoseForceExportState.Get(_flag, "player"), "chaCtrl") as ChaControl);
            var second = PoseForceExportState.Get(_core, "heroine2Cha") as ChaControl;
            if (second != null) _characters.Add("female2", second);
            foreach (var pair in _characters)
                _saved.SaveCharacter(pair.Value, _bodyIk.Invoke(null, new object[] { pair.Value }));
            _saved.Save(_core, "hAnim");
            _saved.Save(_flag, "nowAnimStateName");
            _saved.Save(_flag, "motion");
            _saved.Save(_ctrl, "posMod");
            _saved.Save(_config, "animUseIks");
            foreach (string name in new[] { "Force", "InCum", "OutCum", "InClearing", "stateChange", "animChange", "thisCmp",
                "girl", "boy", "girl2", "boyIkEdits", "girlIkEdits" }) _saved.Save(_pose, name);
            var bc = PoseForceExportState.Get(_ctrl, "bcG");
            if (!Alive(bc) || !(PoseForceExportState.Get(bc, "kokTarg") is Transform))
                throw new InvalidOperationException("目標の初期化を待ってください。");
            _saved.Save(bc, "kokTarg");
            var look = PoseForceExportState.Get(proc, "lookDan");
            if (look == null) throw new InvalidOperationException("Hシーンの初期化を待ってください。");
            _saved.Save(look, "bTopStick");
            try
            {
                Patch(RequireMethod(poseType, "IE_BoyHandTarget"), nameof(BoyPrefix));
                Patch(RequireMethod(poseType, "IE_GirlHandTarget"), nameof(GirlPrefix));
                Patch(RequireMethod(poseType, "ApplyOffsetToBonesCtrl"), nameof(ApplyPrefix));
                _active = this;
                PoseForceExportState.Set(bc, "kokTarg", NewScratch("target"));
                PoseForceExportState.Set(_ctrl, "posMod", Enum.Parse(PoseForceExportState.Get(_ctrl, "posMod").GetType(), "Piston"));
                PoseForceExportState.Set(_config, "animUseIks", true);
                PoseForceExportState.Set(_pose, "stateChange", false);
                PoseForceExportState.Set(_pose, "animChange", false);
                PoseForceExportState.Set(_pose, "InClearing", false);
                PoseForceExportState.Set(_pose, "thisCmp", _pose);
                foreach (string name in new[] { "girl", "boy", "girl2" })
                {
                    var fresh = Activator.CreateInstance(PoseForceExportState.Get(_pose, name).GetType());
                    PoseForceExportState.Set(_pose, name, fresh);
                    _offsets.Add(name == "girl" ? "female" : name == "boy" ? "male" : "female2", fresh);
                }
                for (int i = 0; i < 4; ++i) NewScratch("hand" + i);
            }
            catch { Dispose(); throw; }
        }

        internal static Type RequireType(Assembly assembly, string name)
        {
            var type = assembly.GetType(name, false);
            if (type == null) throw new TypeLoadException(name);
            return type;
        }

        internal static MethodInfo RequireMethod(Type type, string name)
        {
            var method = AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static bool Alive(object value)
        {
            return value != null && (!(value is UnityEngine.Object) || (UnityEngine.Object)value != null);
        }

        private Transform NewScratch(string name)
        {
            var go = new GameObject("PoseForceExport_" + name) { hideFlags = HideFlags.HideAndDontSave };
            _scratch.Add(go);
            return go.transform;
        }

        private void Patch(MethodInfo method, string prefix)
        {
            // 途中で適用が失敗した場合も解除の対象に含める。
            _patched.Add(method);
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(PoseForceExportSampler), prefix));
        }

        internal List<Value> Sample(string poseName, string state, float force, float motion)
        {
            PoseForceExportState.Set(_core, "hAnim", poseName);
            PoseForceExportState.Set(_flag, "nowAnimStateName", state);
            PoseForceExportState.Set(_flag, "motion", motion);
            PoseForceExportState.Set(_pose, "Force", force);
            PoseForceExportState.Set(_pose, "InCum", ((bool)PoseForceExportState.Get(_toolH, "IsInMaleOrgasm")
                || (bool)PoseForceExportState.Get(_toolH, "IsInGirlCum")) && !(bool)PoseForceExportState.Get(_toolH, "IsInOutCum"));
            PoseForceExportState.Set(_pose, "OutCum", PoseForceExportState.Get(_toolH, "IsInOutCum"));
            PoseForceExportState.Set(_pose, "boyIkEdits", Activator.CreateInstance(_boyListType));
            PoseForceExportState.Set(_pose, "girlIkEdits", Activator.CreateInstance(_girlListType));
            _hands.Clear();
            foreach (var go in _scratch) go.transform.localPosition = Vector3.zero;
            _adapt.Invoke(_pose, null);
            _applyCount = 0;
            _force.Invoke(_pose, null);
            if (_applyCount < 2) throw new InvalidOperationException("補正値の受け渡しを捕捉できませんでした: " + poseName + "/" + state);
            var result = new List<Value>();
            foreach (var pair in _offsets)
                foreach (var field in _offsetFields)
                {
                    object raw = field.GetValue(pair.Value);
                    var v = raw is Vector3 ? (Vector3)raw : new Vector3((float)raw, 0, 0);
                    if (!v.Equals(Vector3.zero)) result.Add(new Value { Chara = pair.Key, Field = field.Name, Offset = v });
                }
            foreach (var hand in _hands)
                result.Add(new Value { Chara = hand.Chara, Field = hand.Field, Target = hand.Target, Offset = hand.Scratch.localPosition });
            return result;
        }

        private void CaptureHands(ChaControl cha, Transform left, Transform right)
        {
            string chara = null;
            foreach (var pair in _characters) if (pair.Value == cha) chara = pair.Key;
            if (chara == null) throw new InvalidOperationException("手の対象のキャラを識別できません。");
            var list = (IList)PoseForceExportState.Get(_pose, chara == "male" ? "boyIkEdits" : "girlIkEdits");
            var targets = new[] { left, right };
            for (int side = 0; side < targets.Length; ++side)
            {
                var target = targets[side];
                if (target == null) continue;
                string owner = null;
                foreach (var pair in _characters)
                    if (target == pair.Value.transform || target.IsChildOf(pair.Value.transform)) owner = pair.Key;
                if (owner == null) throw new InvalidOperationException("手の目標のキャラを識別できません: " + target.name);
                if (_hands.Count >= 4) throw new NotSupportedException("手の付け替えが 4 個を超えています。");
                var scratch = _scratch[_hands.Count + 1].transform;
                var edit = Activator.CreateInstance(_editType);
                PoseForceExportState.Set(edit, "newTarget", scratch);
                PoseForceExportState.Set(edit, "baseLocOffst", Vector3.zero);
                list.Add(edit);
                _hands.Add(new Hand { Chara = chara, Field = side == 0 ? "ik_hand_L" : "ik_hand_R",
                    Target = owner + ":" + target.name, Scratch = scratch });
            }
        }

        private static IEnumerator Empty() { yield break; }
        private static bool BoyPrefix(Transform __0, Transform __1, ref IEnumerator __result)
        {
            if (_active == null) return true;
            _active.CaptureHands(_active._characters["male"], __0, __1);
            __result = Empty();
            return false;
        }
        private static bool GirlPrefix(ChaControl __0, Transform __1, Transform __2, ref IEnumerator __result)
        {
            if (_active == null) return true;
            _active.CaptureHands(__0, __1, __2);
            __result = Empty();
            return false;
        }
        private static bool ApplyPrefix()
        {
            if (_active == null) return true;
            ++_active._applyCount;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _saved.RestoreAndVerify(); }
            finally
            {
                _active = null;
                try
                {
                    var errors = new List<string>();
                    foreach (var method in _patched)
                    {
                        try { _harmony.Unpatch(method, HarmonyPatchType.All, _harmony.Id); }
                        catch (Exception ex) { errors.Add(method.Name + ": " + ex.Message); }
                    }
                    if (errors.Count != 0) throw new InvalidOperationException("採取用パッチを解除できません: " + string.Join("; ", errors.ToArray()));
                }
                finally { foreach (var go in _scratch) if (go != null) UnityEngine.Object.Destroy(go); }
            }
        }
    }
}
