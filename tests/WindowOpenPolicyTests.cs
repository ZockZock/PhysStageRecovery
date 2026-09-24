using System;
using BoosterWatch;
class WindowOpenPolicyTests
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
    static int Main()
    {
        var policy = new WindowOpenPolicy();
        Guid resume = Guid.NewGuid(), newBooster = Guid.NewGuid();
        Check(!policy.Observe(resume, false, true, true, true), "Save restoration never opens the window");
        Check(!policy.Observe(resume, true, true, true, true), "Rescanning a restored booster cannot reopen it");
        Check(policy.Observe(newBooster, true, true, true, true), "A newly separated landable booster opens the window");
        Check(!policy.Observe(newBooster, true, true, true, true), "Closing stays respected for an already observed booster");
        Check(!policy.Observe(Guid.NewGuid(), true, false, true, true), "Debris or an empty engine stage does not open the window");
        Check(!policy.Observe(Guid.NewGuid(), true, true, false, true), "The config auto-open preference is respected");
        Check(!policy.Observe(Guid.NewGuid(), true, true, true, false), "A disabled mod cannot open the window automatically");
        Check(policy.Observe(Guid.NewGuid(), true, true, true, true), "A later eligible separation may open the window again");
        Console.WriteLine(count + " window opening checks passed."); return 0;
    }
}
