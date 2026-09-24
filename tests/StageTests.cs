using System;
using BoosterWatch;

class AutomationTests
{
    static int checks;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }

    static int Main()
    {
        int[] stages = { 7, 5, 3, 1, 0, -1 };
        Check(StagePolicy.Next(stages, 7, 1) == 5, "Only future stages of the detached vessel are selected");
        Check(StagePolicy.Next(stages, 3, 1) == 1, "Missing stage numbers are skipped and final stage is inclusive");
        Check(StagePolicy.Next(stages, 1, 1) == -1, "Stages below the selected boundary cannot fire");
        Check(StagePolicy.Next(stages, 1, 0) == 0 && StagePolicy.Next(stages, 0, 0) == -1, "Stage zero executes only once");
        Check(StagePolicy.Next(stages, 3, 5) == -1, "A higher boundary cannot re-fire previous stages");
        Check(StagePolicy.CanFire(true, true, 5000, 10, 5000, 2, 0), "Autostaging starts at configured descent height");
        Check(!StagePolicy.CanFire(true, true, 5001, 10, 5000, 2, 0), "Above trigger altitude cannot stage");
        Check(!StagePolicy.CanFire(true, true, 1000, -10, 5000, 2, 0), "Ascent cannot stage");
        Check(!StagePolicy.CanFire(true, true, 1000, 10, 5000, 0.9, 0), "Separate stages wait at least one simulation second");
        Check(!StagePolicy.CanFire(true, true, 1000, 10, 5000, 2, 0) == false, "Below trigger altitude may stage");
        Check(!StagePolicy.CanFire(true, false, 1000, 10, 5000, 2, 0), "Active, crewed, unloaded or out-of-range craft cannot stage");
        Check(!StagePolicy.CanFire(false, true, 1000, 10, 5000, 2, 0), "Disabled autostaging has no effect");
        Check(!StagePolicy.CanFire(true, true, double.NaN, 10, 5000, 2, 0)
            && !StagePolicy.CanFire(true, true, -1, 10, 5000, 2, 0), "Unknown or below-ground altitude cannot stage");

        Console.WriteLine(checks + " staging checks passed."); return 0;
    }
}
