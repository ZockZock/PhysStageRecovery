using System;
using System.Collections.Generic;
using BoosterWatch;

// The landing reserve is plain arithmetic, so it is checked here without the game: which share of the
// engine's own tanks holds the engine back, what that share is worth, and the text the menu shows.
class FuelReserveTests
{
    static int checks;
    static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }

    static FuelStock Stock(double amount, double capacity)
    {
        return new FuelStock("Treibstoff", amount, capacity, 0.005);
    }

    static List<FuelStock> One(double amount, double capacity)
    {
        return new List<FuelStock> { Stock(amount, capacity) };
    }

    static List<FuelStock> LfOx(double fuel, double fuelMax, double ox, double oxMax)
    {
        return new List<FuelStock>
        {
            new FuelStock("Treibstoff", fuel, fuelMax, 0.005),
            new FuelStock("Oxidator", ox, oxMax, 0.005)
        };
    }

    static int Main()
    {
        // The trigger itself.
        Check(!FuelReserve.Reached(One(500, 1000), 25), "Tank above its reserve leaves the engine alone");
        Check(FuelReserve.Reached(One(250, 1000), 25), "Tank exactly at its reserve holds the engine");
        Check(FuelReserve.Reached(One(249, 1000), 25), "Tank below its reserve holds the engine");
        Check(!FuelReserve.Reached(One(251, 1000), 25), "One unit above the reserve does not hold yet");
        Check(!FuelReserve.Reached(One(0, 1000), 0), "A reserve of zero never holds an engine");
        Check(!FuelReserve.Reached(One(0, 0), 25), "An engine without a tank of its own has no reserve");
        Check(!FuelReserve.Reached(One(10, 0), 25), "A resource without capacity counts as no tank");
        Check(!FuelReserve.Reached(null, 25), "No propellant list at all holds nothing");
        Check(!FuelReserve.Reached(One(100, 1000), double.NaN), "An unreadable percentage does not hold the engine");
        Check(FuelReserve.Reached(One(700, 1000), 100), "A percentage above the limit is clamped, so 80 percent still holds");
        Check(!FuelReserve.Reached(One(900, 1000), 100), "The clamp leaves the top 20 percent usable");

        // Two propellants: the emptiest one decides, exactly as running dry on it would.
        Check(FuelReserve.Reached(LfOx(250, 1000, 900, 1000), 25), "The fuel that reaches its reserve first holds the engine");
        Check(FuelReserve.Reached(LfOx(900, 1000, 250, 1000), 25), "Either propellant can hold the engine");
        Check(!FuelReserve.Reached(LfOx(260, 1000, 260, 1000), 25), "Both above their reserve leaves the engine running");

        // What the menu reports.
        Check(FuelReserve.HasTanks(One(1, 1000)), "A tank of its own is found");
        Check(!FuelReserve.HasTanks(One(0, 0)), "An engine without a tank is named as such");
        Check(Math.Abs(FuelReserve.RemainingShare(LfOx(250, 1000, 900, 1000)) - 0.25) < 1e-9,
            "The share left is the emptiest propellant");
        Check(double.IsNaN(FuelReserve.RemainingShare(One(0, 0))), "Without a tank there is no share to report");
        Check(Math.Abs(FuelReserve.ReserveAmount(Stock(0, 1000), 25) - 250) < 1e-9, "The reserve is a quarter of the tank");
        Check(Math.Abs(FuelReserve.ReserveMass(LfOx(0, 1000, 0, 1000), 25) - 2.5) < 1e-9,
            "The reserve mass adds both propellants");
        Check(Math.Abs(FuelReserve.RemainingShare(One(1500, 1000)) - 1) < 1e-9, "An overfilled tank reports full, not more");
        Check(new FuelStock("x", -5, -10, 0).Amount == 0 && new FuelStock("x", -5, -10, 0).Capacity == 0,
            "Negative amounts and capacities are read as empty");

        // The ideal delta-v the reserve is worth.
        Check(Math.Abs(FuelReserve.IdealDeltaV(10, 2, 300) - 536.4) < 0.5,
            "Two tonnes of twenty give about 536 m/s at Isp 300");
        Check(Math.Abs(FuelReserve.IdealDeltaV(10, 4, 300) - 989.9) < 1.0, "Four tonnes give about 990 m/s");
        Check(Math.Abs(FuelReserve.IdealDeltaV(20, 2, 300) - 280.4) < 0.5, "The same fuel is worth less on a heavier stage");
        Check(FuelReserve.IdealDeltaV(0, 2, 300) == 0, "Without dry mass there is no delta-v");
        Check(FuelReserve.IdealDeltaV(10, 0, 300) == 0, "Without fuel there is no delta-v");
        Check(FuelReserve.IdealDeltaV(10, 2, 0) == 0, "Without Isp there is no delta-v");

        // Text, in the invariant format the log uses.
        Check(FuelReserve.PercentText(25) == "25 %", "A percentage reads as a whole number");
        Check(FuelReserve.PercentText(200) == "80 %", "A percentage above the limit reads as the limit");
        Check(FuelReserve.ShareText(0.4123) == "41.2 %", "The share left carries one decimal");
        Check(FuelReserve.ShareText(double.NaN) == "--", "An unknown share reads as two dashes");
        Check(FuelReserve.MassText(0.6234) == "0.62 t", "Mass reads in tonnes with two decimals");
        Check(FuelReserve.SpeedText(209.7) == "210 m/s", "Delta-v reads in whole metres per second");
        Check(FuelReserve.TankText(LfOx(1200, 1760, 1429, 2148)) == "Treibstoff 1200/1760, Oxidator 1429/2148",
            "The tank line names every propellant with amount and capacity");
        Check(FuelReserve.ReserveText(LfOx(1200, 1760, 1429, 2148), 25) == "Treibstoff 440, Oxidator 537",
            "The reserve line names every propellant with the amount that has to stay");
        Check(FuelReserve.TankText(One(0, 0)) == "", "A propellant without a tank is left out of the lines");

        Console.WriteLine(checks + " checks passed.");
        return 0;
    }
}
