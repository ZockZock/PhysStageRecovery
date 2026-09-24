using System;
using BoosterWatch;

class DescentGateTests
{
    static int checks;
    static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    static DescentGate Falling()
    {
        DescentGate gate = new DescentGate();
        for (int i = 0; i <= 30; i++) gate.Observe(i * 0.1, 1000 - i, -10, true, false);
        return gate;
    }
    static int Main()
    {
        DescentGate gate = new DescentGate();
        for (int i = 0; i <= 40; i++) gate.Observe(i * 0.1, 1000 + i, 10, true, false);
        Check(!gate.AllowsOpening(4, 10), "Ascent never opens a chute");
        gate = new DescentGate();
        for (int i = 0; i <= 40; i++) gate.Observe(i * 0.1, 1000 + i, -10, true, false);
        Check(!gate.Ready, "Incorrect negative velocity during actual climb is rejected");
        gate = new DescentGate();
        for (int i = 0; i <= 40; i++) gate.Observe(i * 0.1, 1000 - i, 10, true, false);
        Check(!gate.Ready, "Altitude decrease alone does not override upward velocity");
        gate = new DescentGate();
        gate.Observe(0, 1000, -10, true, false);
        gate.Observe(0.1, 999, -10, true, false);
        Check(!gate.Ready, "Single negative separation tick is insufficient");
        gate = new DescentGate();
        for (int i = 0; i <= 30; i++) gate.Observe(i * 0.1, 1000 - i * 0.1, -1, true, false);
        Check(!gate.Ready, "Less than five metres below observed peak stays blocked");
        Check(Falling().AllowsOpening(3, -10), "Sustained descent and altitude loss opens gate");
        Check(!Falling().AllowsOpening(3, 1), "Current upward motion immediately blocks a formerly ready gate");
        Check(!Falling().AllowsOpening(3.31, -10), "Stale readiness is not reused");
        Check(!Falling().AllowsOpening(2.9, -10), "Past-time readiness is not reused");
        gate = Falling(); gate.Observe(3.1, 971, 10, true, false);
        Check(!gate.Ready, "Renewed ascent resets confirmation");
        gate = Falling(); gate.Observe(3.1, 969, -10, false, false);
        Check(!gate.Ready, "Packing a booster resets confirmation");
        gate = Falling(); gate.Observe(3.1, 969, -10, true, true);
        Check(!gate.Ready, "Powered motion does not permit automatic deployment");
        gate = Falling(); gate.Observe(3.1, double.NaN, -10, true, false);
        Check(!gate.Ready, "Invalid altitude resets confirmation");
        gate = Falling(); gate.Observe(20, 800, -10, true, false);
        Check(!gate.Ready, "Skipped time after load or warp requires new history");
        gate = Falling(); gate.Observe(2, 980, -10, true, false);
        Check(!gate.Ready, "Rewinding requires new history");
        gate = Falling(); gate.Reset();
        Check(!gate.Ready, "Disabling the mod can clear readiness");
        gate = new DescentGate();
        for (int i = 0; i <= 50; i++) gate.Observe(i * 0.1, 1000 + Math.Sin(i), -0.1, true, false);
        Check(!gate.Ready, "Noise around the apex never opens a chute");
        Check(!Falling().AllowsOpening(double.NaN, -10), "Invalid current time blocks opening");
        Check(!Falling().AllowsOpening(3, double.NaN), "Invalid current velocity blocks opening");
        Console.WriteLine(checks + " descent gate checks passed.");
        return 0;
    }
}
