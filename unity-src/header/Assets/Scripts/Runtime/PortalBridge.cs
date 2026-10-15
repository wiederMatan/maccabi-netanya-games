using System.Runtime.InteropServices;

namespace MaccabiShared
{
    /// <summary>
    /// Progress shared with the website: total stars, per-game best and the last
    /// game played, in the same localStorage keys the portal reads. Plus haptics.
    /// Calls are no-ops outside a WebGL build.
    /// </summary>
    public static class PortalBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PortalBridgeAddStars(int count);
        [DllImport("__Internal")] static extern void PortalBridgeReportBest(string slug, int score);
        [DllImport("__Internal")] static extern void PortalBridgeMarkPlayed(string slug);
        [DllImport("__Internal")] static extern void PortalBridgeHaptic(int ms);

        public static void AddStars(int count) { if (count > 0) PortalBridgeAddStars(count); }
        public static void ReportBest(string slug, int score) => PortalBridgeReportBest(slug, score);
        public static void MarkPlayed(string slug) => PortalBridgeMarkPlayed(slug);
        public static void Haptic(int ms) => PortalBridgeHaptic(ms);
#else
        public static void AddStars(int count) { }
        public static void ReportBest(string slug, int score) { }
        public static void MarkPlayed(string slug) { }
        public static void Haptic(int ms) { }
#endif
    }
}
