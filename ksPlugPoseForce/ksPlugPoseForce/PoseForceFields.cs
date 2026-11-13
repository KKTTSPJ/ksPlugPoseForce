using System;
using System.Collections.Generic;

namespace ksPlugPoseForce
{
    internal static class PoseForceFields
    {
        internal enum Operation { Position, Euler, MultiplyRotation }

        internal sealed class Binding
        {
            internal readonly string Bone;
            internal readonly Operation Mode;
            internal readonly bool OnlyX;
            internal Binding(string bone, Operation mode, bool onlyX = false) { Bone = bone; Mode = mode; OnlyX = onlyX; }
        }

        // Build() が使うので Fields より前に置く（静的な初期化は書いた順）
        private static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "little" };
        private static readonly string[] Joints = { "01", "02", "03" };
        private static readonly Dictionary<string, Binding[]> Fields = Build();

        private static Dictionary<string, Binding[]> Build()
        {
            var fields = new Dictionary<string, Binding[]>(StringComparer.Ordinal)
            {
                { "root_pos", new[] { new Binding("cf_j_hips", Operation.Position) } },
                { "waist_rot", new[] { new Binding("cf_j_waist01", Operation.Euler) } },
                { "spine_rot", new[] { new Binding("cf_j_spine01", Operation.Euler) } },
                { "spine_roll", new[] { new Binding("cf_j_spine01", Operation.Euler, true), new Binding("cf_j_spine02", Operation.Euler, true) } }
            };
            foreach (string side in new[] { "L", "R" })
            {
                Add(fields, side + "_shld_rot", "cf_j_shoulder_" + side, Operation.Euler);
                Add(fields, side + "_hand_pos", "cf_pv_hand_" + side, Operation.Position);
                Add(fields, side + "_elbow_pos", "cf_pv_elbo_" + side, Operation.Position);
                Add(fields, side + "_foot_pos", "cf_pv_leg_" + side, Operation.Position);
                Add(fields, side + "_foot_rot", "cf_pv_leg_" + side, Operation.MultiplyRotation);
                Add(fields, side + "_knee_pos", "cf_pv_knee_" + side, Operation.Position);
                Add(fields, side + "_hipShp_pos", "cf_s_leg_" + side, Operation.Position);
                Add(fields, side + "_thighFK_rot", "cf_j_thigh00_" + side, Operation.Euler);
                Add(fields, side + "_shinFK_rot", "cf_j_leg01_" + side, Operation.Euler);
                Add(fields, side + "_footFK_rot", "cf_j_foot_" + side, Operation.Euler);
                Add(fields, side + "_toeFK_rot", "cf_j_toes_" + side, Operation.Euler);
                // 0.1.5: 手首と指（IK の後に足す）。fingers_rot は親指以外の 4 本・各 3 関節にまとめて足す
                Add(fields, side + "_hand_rot", "cf_j_hand_" + side, Operation.Euler);
                var four = new List<Binding>();
                foreach (string finger in new[] { "index", "middle", "ring", "little" })
                    foreach (string joint in Joints) four.Add(new Binding("cf_j_" + finger + joint + "_" + side, Operation.Euler));
                fields.Add(side + "_fingers_rot", four.ToArray());
                foreach (string finger in Fingers)
                    foreach (string joint in Joints) Add(fields, side + "_" + finger + joint + "_rot", "cf_j_" + finger + joint + "_" + side, Operation.Euler);
            }
            return fields;
        }


        private static void Add(Dictionary<string, Binding[]> fields, string field, string bone, Operation mode)
        {
            fields.Add(field, new[] { new Binding(bone, mode) });
        }

        /// <summary>
        /// 欄の種類。効く条件が違う。
        /// Body: 体の骨。IK が後から書き直さない（IK が止まっていても足す）。
        /// IKTarget: 手足の IK の目標（cf_pv_*）。ゲームの IK がその手足を自分の目標に付けているときだけ効く。
        /// LegFK: IK の脚の鎖の中の骨（太もも・すね）。脚の IK が働いていると IK が書き直す。
        /// Hand: 手の付け替え（ik_hand）。
        /// AfterIK: 手首・指（0.1.5）。手首は腕の IK の端なので、いつも IK を解いた後に足す（指は IK の外）。
        /// </summary>
        internal enum Kind { Body, IKTarget, LegFK, Hand, AfterIK }

        internal static Kind KindOf(string field)
        {
            if (IsHand(field)) return Kind.Hand;
            if (IsAfterIK(field)) return Kind.AfterIK;
            if (field.EndsWith("_thighFK_rot", StringComparison.Ordinal) || field.EndsWith("_shinFK_rot", StringComparison.Ordinal)) return Kind.LegFK;
            if (field.EndsWith("_hand_pos", StringComparison.Ordinal) || field.EndsWith("_elbow_pos", StringComparison.Ordinal)
                || field.EndsWith("_foot_pos", StringComparison.Ordinal) || field.EndsWith("_foot_rot", StringComparison.Ordinal)
                || field.EndsWith("_knee_pos", StringComparison.Ordinal)) return Kind.IKTarget;
            return Kind.Body;
        }

        private static bool IsAfterIK(string field)
        {
            if (field.Length < 3 || (field[0] != 'L' && field[0] != 'R') || field[1] != '_') return false;
            string rest = field.Substring(2);
            if (rest == "hand_rot" || rest == "fingers_rot") return true;
            foreach (string finger in Fingers)
                foreach (string joint in Joints) if (rest == finger + joint + "_rot") return true;
            return false;
        }

        // 0.1.5: 編集画面の「補正項目を追加」に出す順と日本語名。指の関節は FingerFields に分ける
        private static readonly string[][] MainLabels =
        {
            new[] { "root_pos", "腰全体 位置" }, new[] { "waist_rot", "腰 回転" }, new[] { "spine_rot", "背骨 回転" }, new[] { "spine_roll", "背骨 ひねり" },
            new[] { "L_shld_rot", "左肩 回転" }, new[] { "R_shld_rot", "右肩 回転" }, new[] { "L_elbow_pos", "左ひじ 位置" }, new[] { "R_elbow_pos", "右ひじ 位置" },
            new[] { "L_hand_pos", "左手 位置" }, new[] { "R_hand_pos", "右手 位置" }, new[] { "L_hand_rot", "左手首 回転" }, new[] { "R_hand_rot", "右手首 回転" },
            new[] { "ik_hand_L", "左手 付け替え" }, new[] { "ik_hand_R", "右手 付け替え" }, new[] { "L_fingers_rot", "左 4 指まとめて" }, new[] { "R_fingers_rot", "右 4 指まとめて" },
            new[] { "L_foot_pos", "左足 位置" }, new[] { "R_foot_pos", "右足 位置" }, new[] { "L_foot_rot", "左足 回転" }, new[] { "R_foot_rot", "右足 回転" },
            new[] { "L_knee_pos", "左ひざ 位置" }, new[] { "R_knee_pos", "右ひざ 位置" }, new[] { "L_hipShp_pos", "左尻 位置" }, new[] { "R_hipShp_pos", "右尻 位置" },
            new[] { "L_thighFK_rot", "左太もも 回転" }, new[] { "R_thighFK_rot", "右太もも 回転" }, new[] { "L_shinFK_rot", "左すね 回転" }, new[] { "R_shinFK_rot", "右すね 回転" },
            new[] { "L_footFK_rot", "左足首 回転" }, new[] { "R_footFK_rot", "右足首 回転" }, new[] { "L_toeFK_rot", "左つま先 回転" }, new[] { "R_toeFK_rot", "右つま先 回転" },
        };
        private static readonly Dictionary<string, string> FingerNames = new Dictionary<string, string>(StringComparer.Ordinal)
        { { "thumb", "親指" }, { "index", "人差し指" }, { "middle", "中指" }, { "ring", "薬指" }, { "little", "小指" } };

        /// <summary>「補正項目を追加」の主な欄（指の関節以外）。</summary>
        internal static string[] MainFields()
        {
            var list = new List<string>();
            foreach (var pair in MainLabels) list.Add(pair[0]);
            return list.ToArray();
        }

        /// <summary>指の関節ごとの欄（左右 × 5 本 × 3 関節）。</summary>
        internal static string[] FingerFields()
        {
            var list = new List<string>();
            foreach (string side in new[] { "L", "R" })
                foreach (string finger in Fingers)
                    foreach (string joint in Joints) list.Add(side + "_" + finger + joint + "_rot");
            return list.ToArray();
        }

        /// <summary>欄の日本語名（無ければ欄の名前）。</summary>
        internal static string Label(string field)
        {
            foreach (var pair in MainLabels) if (pair[0] == field) return pair[1];
            if (IsAfterIK(field))
            {
                string rest = field.Substring(2);
                foreach (var finger in FingerNames)
                    if (rest.StartsWith(finger.Key, StringComparison.Ordinal))
                        return (field[0] == 'L' ? "左" : "右") + finger.Value + rest.Substring(finger.Key.Length, 2);
            }
            return field;
        }

        internal static bool IsHand(string field) { return field == "ik_hand_L" || field == "ik_hand_R"; }
        internal static string[] Names()
        {
            var names = new List<string>(Fields.Keys) { "ik_hand_L", "ik_hand_R" };
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }
        internal static bool Supports(string field) { return Fields.ContainsKey(field) || IsHand(field); }
        internal static Binding[] Get(string field)
        {
            Binding[] result;
            return Fields.TryGetValue(field, out result) ? result : null;
        }
    }
}
