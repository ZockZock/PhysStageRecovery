using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BoosterWatch
{
    // One propellant and how much of it sits in the tanks that belong to one engine. `Capacity` is
    // what those tanks hold when full, `Amount` what is in them now, both in resource units.
    public sealed class FuelStock
    {
        public readonly string Name;
        public readonly double Capacity;
        public readonly double Density;
        // The amount moves as the stage burns, so it is read fresh every tick.
        public double Amount;

        public FuelStock(string name, double amount, double capacity, double density)
        {
            Name = name;
            Amount = Math.Max(0, amount);
            Capacity = Math.Max(0, capacity);
            Density = Math.Max(0, density);
        }
    }

    // The landing reserve: the share of the fuel in the engine's OWN tanks that has to stay for the
    // landing, and what that share is worth. The arithmetic is free of KSP and Unity, so the test run
    // checks it without the game (tests/FuelReserveTests.cs).
    //
    // Which tanks are "own" tanks is decided by ModuleFuelReserve: the engine's branch of the part
    // tree up to the first decoupler, docking port or part that does not pass fuel on. A drop tank
    // behind a decoupler, and a tank that is only reached through a fuel line, is not part of it.
    public static class FuelReserve
    {
        // Upper end of the slider. Past four fifths of the tanks this is no longer a landing reserve
        // but a stage that never burns out.
        public const double MaxPercent = 80;
        // KSP's standard gravity, for the ideal delta-v a reserve is worth.
        public const double StandardGravity = 9.80665;
        // Below this a resource is not a tank. An engine burning intake air has no tank of its own,
        // and a reserve over nothing would hold the engine back from the first tick on.
        private const double TankEpsilon = 1e-6;

        public static double ClampPercent(double percent)
        {
            if (double.IsNaN(percent)) return 0;
            return Math.Max(0, Math.Min(MaxPercent, percent));
        }

        public static double ReserveAmount(FuelStock stock, double percent)
        {
            return stock == null ? 0 : ClampPercent(percent) / 100.0 * stock.Capacity;
        }

        // Does the engine's own tank group still hold enough? The binding propellant decides: the
        // first one that reaches its share stops the stage, exactly as running dry would. An engine
        // with no own tank never reaches anything - there is no reserve to hold.
        public static bool Reached(IList<FuelStock> stocks, double percent)
        {
            if (!(percent > 0) || stocks == null) return false;
            for (int i = 0; i < stocks.Count; i++)
            {
                FuelStock stock = stocks[i];
                if (stock.Capacity > TankEpsilon && stock.Amount <= ReserveAmount(stock, percent) + 1e-9) return true;
            }
            return false;
        }

        // Whether the engine has a tank of its own at all.
        public static bool HasTanks(IList<FuelStock> stocks)
        {
            if (stocks == null) return false;
            for (int i = 0; i < stocks.Count; i++) if (stocks[i].Capacity > TankEpsilon) return true;
            return false;
        }

        // Whether the reserve acts at all right now.
        //
        // The home world is part of the rule and not a detail: the reserve keeps fuel for a booster
        // that comes back to Kerbin. Around another body - or on the way there - it would darken
        // engines the player still needs for the flight. That is exactly what happened in a test
        // flight: a stage separated around the Mun was held back although its fuel was meant for the
        // ship, and the stage could no longer be used.
        public static bool Acts(bool homeWorld, bool shuttable, double percent, bool released, bool hasTanks)
        {
            return homeWorld && shuttable && percent > 0 && !released && hasTanks;
        }

        // How full the own tanks still are, as a share of their capacity. The emptiest propellant
        // decides; NaN means the engine has no tank of its own.
        public static double RemainingShare(IList<FuelStock> stocks)
        {
            double share = double.NaN;
            if (stocks == null) return share;
            for (int i = 0; i < stocks.Count; i++)
            {
                FuelStock stock = stocks[i];
                if (stock.Capacity <= TankEpsilon) continue;
                double candidate = Math.Max(0, Math.Min(1, stock.Amount / stock.Capacity));
                share = double.IsNaN(share) ? candidate : Math.Min(share, candidate);
            }
            return share;
        }

        // What the reserve holds in tonnes. Together with the dry mass of the engine's own branch and
        // its vacuum/atmospheric Isp this turns "25 %" into something a landing can be planned with.
        public static double ReserveMass(IList<FuelStock> stocks, double percent)
        {
            double mass = 0;
            if (stocks == null) return mass;
            for (int i = 0; i < stocks.Count; i++)
            {
                FuelStock stock = stocks[i];
                mass += ReserveAmount(stock, percent) * stock.Density;
            }
            return mass;
        }

        // What the reserve is worth in ideal delta-v for the dry group it sits in. Gravity losses and
        // drag of the real braking burn are not in this number; it is a reading, not a promise.
        public static double IdealDeltaV(double dryMass, double fuelMass, double isp)
        {
            if (!(dryMass > 0) || !(fuelMass > 0) || !(isp > 0)) return 0;
            return isp * StandardGravity * Math.Log((dryMass + fuelMass) / dryMass);
        }

        // --- Text for the menu and the log -------------------------------------------------------

        public static string PercentText(double percent)
        {
            return ClampPercent(percent).ToString("0", CultureInfo.InvariantCulture) + " %";
        }

        public static string ShareText(double share)
        {
            return double.IsNaN(share) ? "--" : (100 * share).ToString("0.0", CultureInfo.InvariantCulture) + " %";
        }

        public static string MassText(double tonnes)
        {
            return tonnes.ToString("0.00", CultureInfo.InvariantCulture) + " t";
        }

        public static string SpeedText(double speed)
        {
            return speed.ToString("0", CultureInfo.InvariantCulture) + " m/s";
        }

        // "Treibstoff 1200/1750, Oxidator 1429/2144": what the own tanks hold now and when full.
        public static string TankText(IList<FuelStock> stocks)
        {
            StringBuilder text = new StringBuilder();
            if (stocks == null) return text.ToString();
            for (int i = 0; i < stocks.Count; i++)
            {
                FuelStock stock = stocks[i];
                if (stock.Capacity <= TankEpsilon) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(stock.Name).Append(' ')
                    .Append(stock.Amount.ToString("0", CultureInfo.InvariantCulture)).Append('/')
                    .Append(stock.Capacity.ToString("0", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        // "Treibstoff 300, Oxidator 358": what has to stay in them.
        public static string ReserveText(IList<FuelStock> stocks, double percent)
        {
            StringBuilder text = new StringBuilder();
            if (stocks == null) return text.ToString();
            for (int i = 0; i < stocks.Count; i++)
            {
                FuelStock stock = stocks[i];
                if (stock.Capacity <= TankEpsilon) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(stock.Name).Append(' ')
                    .Append(ReserveAmount(stock, percent).ToString("0", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }
    }
}
