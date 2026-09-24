using System;
using System.Runtime.CompilerServices;
using BoosterWatch;
namespace UnityEngine { public static class Time { public static float unscaledTime = 10; } }
public enum ScreenMessageStyle { UPPER_CENTER }
public static class ScreenMessages { public static void PostScreenMessage(string text, float seconds, ScreenMessageStyle style) { } }
public class TimeWarp
{
    public enum Modes { HIGH, LOW }
    public Modes Mode = Modes.HIGH;
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
        bool tracking = false;
        TimeWarp warp = new TimeWarp();
        RailWarpGuard.Install(() => tracking, "notice");
        Check(warp.Rate(2) && TimeWarp.CurrentRate == 20, "Normal warp is unchanged with no tracked boosters");
        warp.Rate(0); tracking = true;
        Check(!warp.Rate(2) && TimeWarp.CurrentRate == 1, "On-rails warp is rejected before changing the rate");
        Check(warp.ChangeMode(TimeWarp.Modes.LOW) && warp.Rate(3) && TimeWarp.CurrentRate == 4,
            "Physics warp reaches 4x while boosters are tracked");
        Check(!warp.ChangeMode(TimeWarp.Modes.HIGH) && warp.Mode == TimeWarp.Modes.LOW,
            "Cannot switch an accelerated physics simulation to on-rails mode");
        Check(warp.Rate(0) && TimeWarp.CurrentRate == 1, "Returning to normal speed remains allowed");
        Check(warp.ChangeMode(TimeWarp.Modes.HIGH) && !warp.Rate(1), "At 1x HIGH mode is harmless but cannot accelerate");
        warp.DirectRate(50);
        Check(TimeWarp.CurrentRate == 1, "Direct internal high-warp requests are also blocked");
        tracking = false;
        Check(warp.Rate(2) && TimeWarp.CurrentRate == 20, "Finishing booster tracking releases ordinary timewarp");
        RailWarpGuard.Remove(); tracking = true;
        Check(warp.Rate(3) && TimeWarp.CurrentRate == 30, "Scene cleanup removes only this mod's guard");
        Console.WriteLine(count + " warp integration checks passed (fixture, not in-game).");
        return 0;
    }
}
