using System;
using BoosterWatch;

class TouchdownTests
{
    static int checks;
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static DescentSample Before()
    {
        return new DescentSample { Time = 10, Clearance = 1, Sink = 2, Horizontal = 0.2, Angular = 0.01,
            PhysicsActive = true, TerrainKnown = true, HasThrust = true, PoweredControlled = true };
    }
    static int Main()
    {
        RecoveryLimits limits = new RecoveryLimits { SinkSpeed = 12, TotalSpeed = 13 };
        RecoveryDecision proof = new RecoveryDecision { StableSeconds = 3.5, PredictedImpactSpeed = 2.1 };
        DescentSample s = Before();
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Safe,
            "Confirmed stable braking permits a safe touchdown label, without awarding funds");
        s = Before(); s.Sink = 204.694; s.Horizontal = 153.792; s.Angular = 2.137; s.HasThrust = s.PoweredControlled = false;
        Check(TouchdownPolicy.Evaluate(s, new RecoveryDecision(), 10.1, limits, false) == TouchdownOutcome.Crashed,
            "Replay of logged tumbling 205 m/s water impact is a crash");
        s = Before(); s.Sink = 29.528; s.Horizontal = 10.238; s.Angular = 0.404; s.PoweredControlled = false;
        Check(TouchdownPolicy.Evaluate(s, new RecoveryDecision(), 10.1, limits, false) == TouchdownOutcome.Crashed,
            "Replay of last logged failed landing cannot be labelled landed");
        s = Before(); s.Angular = 4.46;
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Crashed, "Severe tumbling invalidates touchdown");
        s = Before();
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, true) == TouchdownOutcome.Crashed,
            "Stock collision damage overrides previously safe descent");
        s = Before(); s.HasThrust = s.PoweredControlled = false;
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Unconfirmed,
            "Slow contact without an actual braking source is not a successful landing");
        s = Before();
        Check(TouchdownPolicy.Evaluate(s, new RecoveryDecision(), 10.1, limits, false) == TouchdownOutcome.Unconfirmed,
            "A single slow measurement cannot stand in for stable descent");
        Check(TouchdownPolicy.Evaluate(s, proof, 11, limits, false) == TouchdownOutcome.Unconfirmed, "Stale pre-impact data cannot confirm landing");
        Check(TouchdownPolicy.Evaluate(s, proof, 9, limits, false) == TouchdownOutcome.Unconfirmed, "Time reversal invalidates touchdown proof");
        s = Before(); s.Sink = double.NaN;
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Unconfirmed, "Invalid speed never proves a safe contact");
        s = Before(); s.HasThrust = s.PoweredControlled = false; s.ChutesOpen = true;
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Safe, "Stable parachute touchdown remains supported");
        s = Before(); s.Clearance = -0.6; s.Sink = 0.5;
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Safe,
            "Landing gear dipping below the procedural surface is a normal touchdown, not a crash");
        s = Before(); s.Clearance = -1;
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Safe,
            "A slightly negative clearance does not invalidate an otherwise clean contact");
        s = Before(); s.Clearance = -8;
        Check(TouchdownPolicy.Evaluate(s, proof, 10.1, limits, false) == TouchdownOutcome.Crashed, "A booster already below ground cannot be a safe landing");
        Check(TouchdownPolicy.RecoveredOnContact(true, TouchdownOutcome.Safe),
            "A booster that touched down safely is recovered immediately");
        Check(TouchdownPolicy.RecoveredOnContact(true, TouchdownOutcome.Unconfirmed),
            "A slow contact without the full braking proof is still recovered immediately");
        Check(!TouchdownPolicy.RecoveredOnContact(true, TouchdownOutcome.Crashed),
            "A crashed contact is never recovered");
        Check(!TouchdownPolicy.RecoveredOnContact(false, TouchdownOutcome.Safe),
            "An airborne vessel is not recovered");
        Check(TouchdownPolicy.HeightContact(true, true, false, 0),
            "Reaching the exact terrain height counts as contact when KSP built no ground collider");
        Check(TouchdownPolicy.HeightContact(true, true, false, -0.4),
            "Sinking slightly past the terrain height stays a contact");
        Check(!TouchdownPolicy.HeightContact(true, true, true, -0.4),
            "A ground collider KSP did build always decides the contact, never the height");
        Check(!TouchdownPolicy.HeightContact(true, true, false, 0.4),
            "A booster above the terrain is still flying");
        Check(!TouchdownPolicy.HeightContact(false, true, false, -0.4),
            "Without active physics there is no contact");
        Check(!TouchdownPolicy.HeightContact(true, false, false, -0.4),
            "An unknown terrain height cannot produce a contact");
        Check(!TouchdownPolicy.HeightContact(true, true, false, double.NaN),
            "An invalid clearance cannot produce a contact");
        Console.WriteLine(checks + " touchdown checks passed."); return 0;
    }
}
