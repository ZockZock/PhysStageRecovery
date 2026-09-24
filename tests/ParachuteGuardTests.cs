// Executes the production Harmony hooks against a small, controllable stock-state fixture.
// Does not claim to run the Unity game; the real 1.12.5 method signatures/call site were
// inspected separately with tools/InspectApi.cs.
using System;
using System.Runtime.CompilerServices;
using BoosterWatch;

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object value) { }
        public static void LogError(object value) { Console.WriteLine(value); }
    }
}
public class Part { public uint flightID = 42; }
public class ModuleParachute
{
    public enum deploymentStates { STOWED, ACTIVE, SEMIDEPLOYED, DEPLOYED, CUT }
    public deploymentStates deploymentState;
    public enum deploymentSafeStates { UNSAFE, RISKY, SAFE }
    public deploymentSafeStates deploymentSafeState;
    public int DeployCalls;
    public float deployAltitude = 1000;
    public double chuteMaxTemp = 650;
    public float autoCutSpeed;
    public void Deploy() { DeployCalls++; deploymentState = deploymentStates.ACTIVE; }
    public Part part = new Part();
    public bool StockAllows = true;
    public int PhysicsTicks, DisarmCalls;
    public void Disarm() { DisarmCalls++; deploymentState = deploymentStates.STOWED; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual bool PassedAdditionalDeploymentChecks() { return StockAllows; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FixedUpdate()
    {
        PhysicsTicks++;
        if (deploymentState == deploymentStates.ACTIVE && PassedAdditionalDeploymentChecks())
            deploymentState = deploymentStates.SEMIDEPLOYED;
    }
    public void PhysicsTick() { FixedUpdate(); }
}
namespace BoosterWatch
{
    public class BoosterWatchFlight
    {
        public bool Block = true;
        public bool BlockParachuteOpening(ModuleParachute chute) { return Block; }
    }
}
class ParachuteGuardTests
{
    static int checks;
    static void Check(bool value, string name)
    {
        checks++;
        if (!value) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    static int Main()
    {
        BoosterWatchFlight owner = new BoosterWatchFlight();
        ParachuteGuard.Install(owner);
        ModuleParachute staged = new ModuleParachute { deploymentState = ModuleParachute.deploymentStates.ACTIVE };
        staged.PhysicsTick();
        Check(staged.deploymentState == ModuleParachute.deploymentStates.STOWED && staged.DisarmCalls == 1,
            "Stage-armed chute is disarmed before its first physical opening");
        Check(staged.PhysicsTicks == 1, "Blocking opening does not skip stock physics processing");
        Check(!staged.PassedAdditionalDeploymentChecks(), "Final opening check blocks unsafe managed chutes");
        owner.Block = false;
        staged.deploymentState = ModuleParachute.deploymentStates.ACTIVE;
        staged.PhysicsTick();
        Check(staged.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED,
            "Re-armed chute opens after the descent/safety gate is released");
        owner.Block = true;
        staged.PhysicsTick();
        Check(staged.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED && staged.DisarmCalls == 1,
            "Already-open chute is never cut, repacked or disarmed");
        owner.Block = false;
        ModuleParachute unsafeStock = new ModuleParachute { StockAllows = false, deploymentState = ModuleParachute.deploymentStates.ACTIVE };
        unsafeStock.PhysicsTick();
        Check(unsafeStock.deploymentState == ModuleParachute.deploymentStates.ACTIVE,
            "BoosterWatch never overrides a stock refusal to open");
        ParachuteGuard.Remove();
        owner.Block = true;
        ModuleParachute afterUnload = new ModuleParachute { deploymentState = ModuleParachute.deploymentStates.ACTIVE };
        afterUnload.PhysicsTick();
        Check(afterUnload.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED,
            "Scene cleanup restores stock behavior");
        ParachuteGuard.Install(owner);
        ModuleParachute nextFlight = new ModuleParachute { deploymentState = ModuleParachute.deploymentStates.ACTIVE };
        nextFlight.PhysicsTick();
        Check(nextFlight.deploymentState == ModuleParachute.deploymentStates.STOWED && nextFlight.DisarmCalls == 1,
            "Next flight installs a single working guard again");
        ParachuteGuard.Remove();
        ModuleParachute early = new ModuleParachute();
        Check(!ParachuteDeployment.TryArm(early, true, true), "Unsafe chute is never armed");
        early.deploymentSafeState = ModuleParachute.deploymentSafeStates.RISKY;
        Check(ParachuteDeployment.Allowed(early.deploymentSafeState),
            "A risky opening is armed: as early as the canopy allows, and stock uses risky too");
        early.deploymentSafeState = ModuleParachute.deploymentSafeStates.UNSAFE;
        Check(!ParachuteDeployment.Allowed(early.deploymentSafeState), "An unsafe opening is never armed");
        early.deploymentSafeState = ModuleParachute.deploymentSafeStates.SAFE;
        Check(!ParachuteDeployment.TryArm(early, true, false), "Safe chute still waits for confirmed descent");
        Check(!ParachuteDeployment.TryArm(early, false, true), "Disabled automation does not arm chutes");
        Check(ParachuteDeployment.TryArm(early, true, true) && early.DeployCalls == 1,
            "First safe descending sample arms immediately without an altitude/stage/engine restriction");
        Check(!ParachuteDeployment.TryArm(early, true, true) && early.DeployCalls == 1,
            "Active parachute is not repeatedly deployed");
        early.deploymentState = ModuleParachute.deploymentStates.CUT;
        Check(!ParachuteDeployment.TryArm(early, true, true), "Cut canopy is never repacked");
        ModuleParachute overHighGround = new ModuleParachute { deploymentSafeState = ModuleParachute.deploymentSafeStates.SAFE };
        string note;
        Check(ParachuteDeployment.TryArm(overHighGround, true, true, 1500, out note), "Canopy arms over high ground");
        Check(Math.Abs(overHighGround.deployAltitude - ParachuteDeployment.OpenAboveGround) < 0.5,
            "Opening height is a plain " + ParachuteDeployment.OpenAboveGround + " m: KSP itself compares it"
            + " against the part altitude and a ray to the ground");
        Check(Math.Abs(ParachuteDeployment.SetOpeningHeight(overHighGround, 1500)
            - ParachuteDeployment.OpenAboveGround) < 0.5,
            "Repeating the opening height keeps the same value");
        ModuleParachute fragile = new ModuleParachute { deploymentSafeState = ModuleParachute.deploymentSafeStates.SAFE };
        ParachuteDeployment.Protect(fragile);
        Check(fragile.chuteMaxTemp >= ParachuteDeployment.ChuteMaxTemperature,
            "A canopy that is opened early is protected from being burnt off by the deployment");
        string opened;
        Check(!ParachuteDeployment.TryOpen(fragile, true, true, ParachuteDeployment.OpenAboveGround + 100, 0, out opened),
            "Nothing opens above the configured height");
        Check(ParachuteDeployment.TryOpen(fragile, true, true, ParachuteDeployment.OpenAboveGround - 100, 0, out opened),
            "Canopy opens below the configured height above the ground");
        Check(fragile.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED,
            "From there KSP's own update inflates the canopy");
        Check(!ParachuteDeployment.TryOpen(fragile, true, true, ParachuteDeployment.OpenAboveGround - 200, 0, out opened),
            "A canopy already on its way is not opened twice");
        ModuleParachute overLand = new ModuleParachute { deploymentSafeState = ModuleParachute.deploymentSafeStates.SAFE };
        Check(ParachuteDeployment.TryOpen(overLand, true, true,
            2000 + ParachuteDeployment.OpenAboveGround - 100, 2000, out opened),
            "Over high ground the height above the ground counts, not the height above the sea");
        Check(ParachuteDeployment.TryOpen(overLand, true, true,
            2000 + ParachuteDeployment.OpenAboveGround + 100, 2000, out opened) == false,
            "Over high ground a height above the configured one still waits");
        Console.WriteLine(checks + " Harmony integration checks passed (fixture, not in-game).");
        return 0;
    }
}
