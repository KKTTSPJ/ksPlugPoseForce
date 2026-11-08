namespace ksPlugPoseForce
{
    internal static class PoseForceActors
    {
        internal static bool IsRole(string role) { return role == "female" || role == "male" || role == "female2"; }
        internal static string Label(string role) { return role == "female" ? "女" : role == "female2" ? "女２" : "男"; }
        internal static string TargetRole(string target)
        {
            if (string.IsNullOrEmpty(target)) return "";
            int colon = target.IndexOf(':');
            if (colon < 1 || colon == target.Length - 1) return "";
            string role = target.Substring(0, colon);
            return IsRole(role) ? role : "";
        }
    }
}
