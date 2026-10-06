namespace UnityEngine
{
    public class MonoBehaviour { public MonoBehaviour(IntPtr p) { } }
    public static class Debug { public static void LogWarning(object message) { } }
}
namespace Il2CppInterop.Runtime.Injection
{
    public static class ClassInjector
    {
        public static IntPtr DerivedConstructorPointer<T>() => IntPtr.Zero;
        public static void DerivedConstructorBody(object value) { }
    }
}
