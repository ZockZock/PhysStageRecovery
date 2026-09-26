using System;
using System.Runtime.CompilerServices;
using BoosterWatch;
namespace UnityEngine { public static class Time { public static float unscaledTime = 10; } public static class Debug { public static void LogError(object o) { System.Console.WriteLine(o); } } }
public enum ScreenMessageStyle { UPPER_CENTER }
public static class ScreenMessages { public static void PostScreenMessage(string text, float seconds, ScreenMessageStyle style) { } }
public class TimeWarp
{
    public enum Modes { HIGH, LOW }
    public Modes Mode = Modes.HIGH;
    public float[] physicsWarpRates = { 1, 2, 3, 4 };
    public static float CurrentRate = 1;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool setMode(Modes mode) { Mode = mode; return true; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool setRate(int rateIdx, bool instant, bool lower, bool force, bool message)
    { assumeWarpRate(rateIdx == 0 ? 1 : Mode == Modes.LOW ? rateIdx + 1 : 10 * rateIdx, instant, message); return true; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void assumeWarpRate(float rate, bool instant, bool message) { CurrentRate = rate; }
    public bool Rate(int index) { return setRate(index, true, true, false, false); }
    public bool ChangeMode(Modes mode) { return setMode(mode); }
    public void DirectRate(float rate) { assumeWarpRate(rate, true, false); }
}
class RailWarpGuardTests
{
    static int count;
    static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
    static int Main()
    {
        bool tracking = false, landing = false;
        TimeWarp warp = new TimeWarp();
        RailWarpGuard.Install(() => tracking, "notice", () => landing, "landing");
        Check(warp.Rate(2) && TimeWarp.CurrentRate == 20, "Normal warp is unchanged with no tracked boosters");
        warp.Rate(0); tracking = true;
        Check(warp.Rate(1) && warp.Mode == TimeWarp.Modes.LOW && TimeWarp.CurrentRate == 2,
            "On-rails warp request becomes physics warp of the same step (2x)");
        Check(warp.Rate(3) && TimeWarp.CurrentRate == 4, "Further steps run in physics warp up to 4x");
        Check(!warp.ChangeMode(TimeWarp.Modes.HIGH) && warp.Mode == TimeWarp.Modes.LOW,
            "Cannot switch an accelerated physics simulation to on-rails mode");
        Check(warp.Rate(0) && TimeWarp.CurrentRate == 1, "Returning to normal speed remains allowed");
        Check(warp.ChangeMode(TimeWarp.Modes.HIGH) && warp.Rate(6) && warp.Mode == TimeWarp.Modes.LOW
            && TimeWarp.CurrentRate == 4, "A high on-rails step is capped at the highest physics step");
        warp.Rate(0); warp.ChangeMode(TimeWarp.Modes.HIGH);
        warp.DirectRate(50);
        Check(TimeWarp.CurrentRate == 1, "Direct internal high-warp requests are still blocked");
        landing = true;
        Check(!warp.Rate(1) && TimeWarp.CurrentRate == 1, "No warp at all while a powered landing burns");
        warp.ChangeMode(TimeWarp.Modes.LOW);
        Check(!warp.Rate(2) && TimeWarp.CurrentRate == 1, "Not even physics warp while a powered landing burns");
        Check(RailWarpGuard.PhysicsIndex(7, 4) == 3 && RailWarpGuard.PhysicsIndex(0, 4) == 0
            && RailWarpGuard.PhysicsIndex(2, 1) == 0, "Step mapping");
        landing = false; tracking = false; warp.ChangeMode(TimeWarp.Modes.HIGH);
        Check(warp.Rate(2) && TimeWarp.CurrentRate == 20, "Finishing booster tracking releases ordinary timewarp");
        RailWarpGuard.Remove(); tracking = true; warp.Rate(0);
        Check(warp.Rate(3) && TimeWarp.CurrentRate == 30, "Scene cleanup removes only this mod's guard");
        Console.WriteLine(count + " warp integration checks passed (fixture, not in-game).");
        return 0;
    }
}
