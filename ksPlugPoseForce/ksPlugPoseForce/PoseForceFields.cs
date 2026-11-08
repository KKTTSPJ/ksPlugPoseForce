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
            }
            return fields;
        }

        private static void Add(Dictionary<string, Binding[]> fields, string field, string bone, Operation mode)
        {
            fields.Add(field, new[] { new Binding(bone, mode) });
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
