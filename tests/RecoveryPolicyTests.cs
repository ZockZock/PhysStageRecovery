using System;
using BoosterWatch;

class RecoveryPolicyTests
{
    static int checks;
    static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    static DescentSample Good(double time, double height)
    {
        return new DescentSample { Time = time, Clearance = height, Sink = 4, Horizontal = 1, Angular = 0.01,
            PhysicsActive = true, TerrainKnown = true, ChutesOpen = true, Eligible = true };
    }
    static RecoveryPolicy Ready(RecoveryLimits limits)
    {
        RecoveryPolicy p = new RecoveryPolicy();
        for (int i = 0; i <= 40; i++) p.Evaluate(Good(i * 0.1, 25), limits);
        return p;
    }
    static int Main()
    {
        RecoveryLimits limits = new RecoveryLimits();
        Check(Ready(limits).Evaluate(Good(4.1, 10), limits).StableDescent, "Stable descent supplies contact evidence at 10 m");
        Check(Ready(limits).Evaluate(Good(4.1, 1000), limits).StableDescent, "Stable descent is independent of an obsolete recovery altitude");
        Check(!new RecoveryPolicy().Evaluate(Good(0, 5), limits).StableDescent, "No instant recovery without history");
        Check(!Ready(limits).Evaluate(Good(4.1, -1), limits).StableDescent, "Below terrain never counts as recovery");
        DescentSample sample = Good(4.1, 5); sample.Sink = 20;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Undersized chutes / high sink rejected");
        sample = Good(4.1, 5); sample.Horizontal = 8;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Fast lateral motion rejected");
        sample = Good(4.1, 5); sample.Sink = -0.1;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Ascending booster rejected");
        sample = Good(4.1, 5); sample.Angular = 1;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Tumbling booster rejected");
        sample = Good(4.1, 5); sample.HasThrust = true;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Powered descent does not prove sufficient parachute braking");
        sample = Good(4.1, 5); sample.ChutesOpen = false;
        RecoveryPolicy broken = Ready(limits);
        Check(!broken.Evaluate(sample, limits).StableDescent, "Cut or partly deployed chute rejected");
        Check(!broken.Evaluate(Good(4.2, 4), limits).StableDescent, "Reopening must rebuild stable history");
        sample = Good(4.1, 5); sample.PhysicsActive = false;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Packed booster rejected");
        sample = Good(4.1, 5); sample.Eligible = false;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Active, crewed, foreign-body or disabled recovery rejected");
        sample = Good(4.1, 5); sample.TerrainKnown = false;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Unknown terrain rejected");
        sample = Good(4.1, double.NaN);
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "NaN height rejected");
        sample = Good(4.1, 5); sample.Sink = double.PositiveInfinity;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Infinite velocity rejected");
        Check(!Ready(limits).Evaluate(Good(15, 5), limits).StableDescent, "Timewarp or unloaded time gap resets history");
        Check(!Ready(limits).Evaluate(Good(2, 5), limits).StableDescent, "Time reversal resets history");
        sample = Good(4.1, 5); sample.Sink = 4.5;
        Check(!Ready(limits).Evaluate(sample, limits).StableDescent, "Sudden acceleration resets stability");
        RecoveryPolicy accelerate = new RecoveryPolicy(); RecoveryDecision d = null;
        RecoveryLimits projectionLimits = new RecoveryLimits { TotalSpeed = 6 };
        for (int i = 0; i <= 40; i++) { sample = Good(i * 0.1, 10); sample.Sink = 2.5 + i * 0.06; d = accelerate.Evaluate(sample, projectionLimits); }
        Check(!d.StableDescent, "Projected impact above limit rejected despite currently slow descent");
        RecoveryPolicy reset = Ready(limits); reset.Reset();
        Check(!reset.Evaluate(Good(4.1, 5), limits).StableDescent, "Explicit reset clears history");
        RecoveryPolicy seven = new RecoveryPolicy();
        double recoveryClearance = -1;
        for (int i = 0; i <= 150; i++)
        {
            double height = 100 - i * 0.7;
            sample = Good(i * 0.1, height); sample.Sink = 7;
            if (seven.Evaluate(sample, limits).StableDescent) { recoveryClearance = height; break; }
        }
        Check(recoveryClearance > 10,
            "Reported 7 m/s descent supplies evidence without waiting for a recovery altitude");
        RecoveryPolicy tooFast = new RecoveryPolicy();
        for (int i = 0; i <= 40; i++) { sample = Good(i * 0.1, 8); sample.Sink = 8.1; d = tooFast.Evaluate(sample, limits); }
        Check(!d.StableDescent && d.Reason.Contains("Grenze"), "Above configured sink limit is rejected with a specific reason");
        RecoveryPolicy noChutes = new RecoveryPolicy();
        for (int i = 0; i <= 40; i++) { sample = Good(i * 0.1, 8); sample.Sink = 7; sample.ChutesOpen = false; d = noChutes.Evaluate(sample, limits); }
        Check(!d.StableDescent, "Seven m/s without fully open chutes is not rescued");
        RecoveryPolicy physicsWarp = new RecoveryPolicy();
        RecoveryLimits userLimits = new RecoveryLimits { SinkSpeed = 12, TotalSpeed = 13 };
        double recoveredAt = -1;
        for (int i = 0; i < 150; i++)
        {
            sample = Good(i * 0.16, 180 - i * 0.16 * 9.3); sample.Sink = 9.3;
            if (physicsWarp.Evaluate(sample, userLimits).StableDescent) { recoveredAt = sample.Clearance; break; }
        }
        Check(recoveredAt > 30, "Physics-warp samples still confirm descent evidence");
        RecoveryPolicy powered = new RecoveryPolicy();
        for (int i = 0; i <= 40; i++)
        {
            sample = Good(i * 0.1, 8); sample.ChutesOpen = false; sample.HasThrust = true;
            sample.PoweredControlled = true; sample.Sink = 2;
            d = powered.Evaluate(sample, limits);
        }
        Check(d.StableDescent, "Proven stable powered descent supplies contact evidence without parachutes");
        sample.Time = 4.1; sample.HasThrust = false;
        Check(!powered.Evaluate(sample, limits).StableDescent, "Lost actual thrust immediately cancels powered recovery");
        sample.Time = 4.2; sample.HasThrust = true;
        Check(!powered.Evaluate(sample, limits).StableDescent, "Thrust restoration needs a new stable measurement sequence");
        RecoveryPolicy transition = Ready(limits);
        sample = Good(4.1, 5); sample.PoweredControlled = true; sample.HasThrust = true;
        Check(!transition.Evaluate(sample, limits).StableDescent, "Switching from chute to powered recovery cannot reuse chute stability");
        powered = new RecoveryPolicy();
        for (int i = 0; i <= 40; i++)
        {
            sample = Good(i * 0.1, 8); sample.ChutesOpen = false; sample.HasThrust = true;
            sample.PoweredControlled = false; sample.Sink = 2; d = powered.Evaluate(sample, limits);
        }
        Check(!d.StableDescent, "Thrust without control, fuel reserve or correct alignment cannot recover");
        Console.WriteLine(checks + " checks passed.");
        return 0;
    }
}
