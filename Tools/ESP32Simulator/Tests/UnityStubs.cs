// Only for running the production receiver under Unity's bundled Mono outside
// the Editor. No receive/parsing code is replaced by these clock/attribute stubs.
using System;
using System.Diagnostics;
namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
    public static class Time
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static float realtimeSinceStartup => (float)Clock.Elapsed.TotalSeconds;
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public static class Debug
    {
        public static void LogError(object value) { Console.Error.WriteLine(value); }
    }
}
public static class KeyboardDriveInputSource
{
    public static bool IsResetPressed(int index) => false;
}
