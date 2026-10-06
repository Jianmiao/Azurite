namespace UnityEngine
{
    public static class Application
    {
        private static int _target = -1;
        public static bool RejectTargetOnce;
        public static int targetFrameRate { get => _target; set { if (RejectTargetOnce) { RejectTargetOnce = false; throw new InvalidOperationException("target write failed"); } _target = value; } }
    }
    public static class QualitySettings
    {
        private static int _sync;
        public static bool RejectVSyncOnce;
        public static int vSyncCount { get => _sync; set { if (RejectVSyncOnce) { RejectVSyncOnce = false; throw new InvalidOperationException("vsync write failed"); } _sync = value; } }
    }
}
namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public readonly List<string> Warnings = new();
        public void LogInfo(object value) { }
        public void LogWarning(object value) => Warnings.Add(value.ToString()!);
    }
}
namespace BepInEx.Core.Logging.Interpolation
{
    public struct BepInExInfoLogInterpolatedStringHandler
    {
        public BepInExInfoLogInterpolatedStringHandler(int literal, int formatted, out bool enabled) => enabled = true;
        public void AppendLiteral(string value) { }
        public void AppendFormatted<T>(T value) { }
    }
    public struct BepInExWarningLogInterpolatedStringHandler
    {
        public BepInExWarningLogInterpolatedStringHandler(int literal, int formatted, out bool enabled) => enabled = true;
        public void AppendLiteral(string value) { }
        public void AppendFormatted<T>(T value) { }
    }
}
namespace Il2CppSystem { public class Action<T> { internal readonly System.Action<T> Callback; public Action(System.Action<T> callback) => Callback = callback; } }
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T ConvertDelegate<T>(System.Delegate callback) where T : class => (T)(object)new Il2CppSystem.Action<int>((System.Action<int>)callback);
    }
}
public class Singleton<T> { public static T? Instance; }
public class UserSettings
{
    private static int _next;
    public IntPtr Pointer = new(++_next);
    public bool isReady = true;
    public int fpsTier = 2;
    private Il2CppSystem.Action<int>? _changed;
    public void add_OnFpsTierChanged(Il2CppSystem.Action<int> changed) => _changed = changed;
    public void remove_OnFpsTierChanged(Il2CppSystem.Action<int> changed) { if (_changed == changed) _changed = null; }
    public void ChangeTier(int tier)
    {
        fpsTier = tier;
        UnityEngine.Application.targetFrameRate = -1;
        UnityEngine.QualitySettings.vSyncCount = 2 - tier;
        _changed?.Callback(tier);
    }
}
namespace Azurite
{
    internal static class CurrentDisplayRate
    {
        internal static double Rate = 240;
        internal static bool TryRead(out DisplayRatePolicy display) => DisplayRatePolicy.TryCreate(Rate, out display);
    }
}
