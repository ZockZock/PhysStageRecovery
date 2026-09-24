using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BoosterWatch.MechJebPort;
using BoosterWatch.MechJebPort.Landing;
using UnityEngine;
internal class LandingPortTests
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
            string path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run(); Console.WriteLine(checks + " source-port checks passed (fixture, not an in-game flight)."); return 0;
    }
    internal static MechJebCore Craft(double height, double sink, double horizontal = 0)
    {
        var c = new MechJebCore();
        c.VesselState.CoM = new Vector3d(0, c.MainBody.Radius + height, 0);
        c.VesselState.AltitudeBottom = c.VesselState.AltitudeASL = c.VesselState.AltitudeTrue = height;
        c.VesselState.SurfaceVelocity = new Vector3d(horizontal, -sink, 0);
        c.VesselState.GravityForce = new Vector3d(0, -9.81, 0);
        c.VesselState.Forward = -c.VesselState.SurfaceVelocity.normalized;
        return c;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void Run()
    {
        SeparationTerrainRegression();
        var c = Craft(1000,100); var s = new FlightCtrlState();
        Check(new UntargetedDeorbit(c).Drive(s) is CoastToDeceleration, "Suborbital booster enters the deceleration phase instead of the final descent");
        c.Orbit.PeA = 10000; c.VesselState.OrbitalVelocity = new Vector3d(2000,0,0);
        c.VesselState.Forward = Vector3d.left;
        var deorbit = new UntargetedDeorbit(c); deorbit.Drive(s);
        Check(c.Thrust.TargetThrottle == 1, "Untargeted orbital booster burns horizontal retrograde");
        c.VesselState.Forward = Vector3d.up; deorbit.Drive(s);
        Check(c.Thrust.TargetThrottle == 0, "Deorbit burn waits for alignment");
        c = Craft(1000,100);
        var coast = new CoastToDeceleration(c);
        Check(coast.Drive(s) == coast && c.Thrust.TargetThrottle == 0,
            "Coast keeps the engines dark above the deceleration end altitude");
        Check(c.Attitude.Direction == -c.VesselState.SurfaceVelocity.normalized,
            "Coast turns the booster retrograde while the engines are dark");
        Check(c.Landing.MaxAllowedSpeed() == double.MaxValue,
            "Atmosphere coast has no speed limit above the deceleration end altitude");
        c.Landing.DecelerationEndAltitudeValue = 1100;
        Check(new CoastToDeceleration(c).Drive(s) is DecelerationBurn,
            "Coast starts the burn below the deceleration end altitude");
        // A booster arriving from a high orbit: the atmospheric limit is unlimited above the end
        // altitude, so only the drag-free stopping envelope can start the burn in time.
        //
        // The step it falls into is FinalDescent, not DecelerationBurn, and that is deliberate: the
        // atmospheric deceleration policy is still unbounded this far out, so handing the descent
        // back to it would leave the booster coasting again. FinalDescent carries the bounded
        // descent-speed policy and turns retrograde immediately.
        c = Craft(30000, 2000, 2000);
        c.Landing.UseBrakingEnvelope = false;
        c.Landing.DragFreeAllowedSpeedValue = 1500;
        Check(new CoastToDeceleration(c).Drive(s) is FinalDescent,
            "A speed above the drag-free envelope starts the bounded approach even when the air limit is unlimited");
        c.Landing.DragFreeAllowedSpeedValue = 3000;
        var stillCoasting = new CoastToDeceleration(c);
        Check(stillCoasting.Drive(s) == stillCoasting,
            "Below the envelope the booster keeps coasting");
        c = Craft(1000,100); c.Landing.AtmosphereToBrake = false;
        double allowed = c.Landing.MaxAllowedSpeed();
        Check(allowed > 100 && allowed < 130, "Vacuum policy returns MechJeb's closed-form speed limit");
        var burn = new DecelerationBurn(c); burn.Drive(s);
        Check(c.Thrust.TargetThrottle == 0, "Deceleration burn stays dark while the speed is below the limit");
        // Isolate the original throttle law; the full approach with the guard is tested below.
        c.Landing.UseBrakingEnvelope = false;
        c.VesselState.SurfaceVelocity = new Vector3d(0,-300,0); c.VesselState.Forward = Vector3d.up;
        burn.Drive(s);
        Check(c.Thrust.TargetThrottle == 1, "Deceleration burn opens the throttle above the speed limit");
        Check(c.Attitude.Direction == Vector3d.up, "Deceleration burn holds the retrograde attitude");
        c = Craft(100,100);
        c.Landing.UseBrakingEnvelope = false;
        Check(new DecelerationBurn(c).Drive(s) is FinalDescent,
            "Deceleration burn hands over to the final descent in an atmosphere");
        c.Landing.AtmosphereToBrake = false;
        Check(new DecelerationBurn(c).Drive(s) is KillHorizontalVelocity,
            "Deceleration burn hands over to the horizontal kill in vacuum");
        c = Craft(1000,100,40);
        new KillHorizontalVelocity(c).Drive(s);
        Check(c.Thrust.TargetThrottle == 1 && c.Attitude.Direction.y > 0.9,
            "Horizontal kill hovers upright until the drift is gone");
        c.VesselState.Forward = new Vector3d(0.371, -0.928, 0);
        Check(new KillHorizontalVelocity(c).Drive(s) is FinalDescent,
            "Horizontal kill hands over once the nose no longer points downrange");
        c.VesselState.Forward = Vector3d.up;
        Check(new KillHorizontalVelocity(c).Drive(s) is FinalDescent,
            "Horizontal kill hands over when there is no horizontal direction at all");
        var safe = new SafeDescentSpeedPolicy(600200, 9.81, 20);
        Check(Math.Abs(safe.MaxAllowedSpeed(new Vector3d(0,601200,0), new Vector3d(0,-100,0))
            - 0.9 * Math.Sqrt(2 * (20 - 9.81) * 1000)) < 1e-9, "Vacuum policy keeps the upstream closed form");
        c = Craft(1000,-30); new FinalDescent(c).Drive(s);
        Check(c.Thrust.Tmode == MechJebModuleThrustController.TMode.DIRECT && c.Thrust.TransSpdAct == 0,
            "Ascending vessel waits with zero throttle");
        c = Craft(1000,100); c.VesselState.Forward = Vector3d.down; new FinalDescent(c).Drive(s);
        Check(c.Thrust.Tmode == MechJebModuleThrustController.TMode.DIRECT && c.Thrust.TransSpdAct == 0,
            "Tumbling booster cannot ignite before it faces retrograde");
        c = Craft(1000,100); new FinalDescent(c).Drive(s);
        Check(c.Thrust.Tmode == MechJebModuleThrustController.TMode.KEEP_SURFACE && c.Thrust.TransSpdAct > 0,
            "High descent uses original gravity-turn speed search");
        c = Craft(100,50,40); new FinalDescent(c).Drive(s);
        Check(c.Thrust.TargetThrottle == 1 && c.Thrust.Tmode == MechJebModuleThrustController.TMode.OFF,
            "Low sideways drift triggers original full retrograde burn");
        // Hysteresis in the last 300 m: a single threshold flipped the commanded attitude between
        // upright and retrograde and drove a booster into the ground at 30 m.
        c = Craft(100,50,9); var hyster = new FinalDescent(c); hyster.Drive(s);
        Check(c.Thrust.TargetThrottle == 1, "Sideways speed above the entry threshold burns retrograde");
        c.VesselState.SurfaceVelocity = new Vector3d(5,-50,0);
        hyster.Drive(s);
        Check(c.Thrust.TargetThrottle == 1, "Between the thresholds the branch stays where it was");
        c.VesselState.SurfaceVelocity = new Vector3d(2,-50,0);
        hyster.Drive(s);
        Check(c.Thrust.Tmode == MechJebModuleThrustController.TMode.KEEP_VERTICAL && c.Thrust.TransKillH,
            "Below the exit threshold the landing ramp takes over again");
        c = Craft(100,1); var final = new FinalDescent(c); final.Drive(s);
        Check(c.Thrust.TargetThrottle == 0, "Low slow descent coasts until the final ramp is reached");
        c.VesselState.SurfaceVelocity = new Vector3d(0,-30,0); final.Drive(s);
        Check(c.Thrust.Tmode == MechJebModuleThrustController.TMode.KEEP_VERTICAL && c.Thrust.TransKillH,
            "Final descent controls vertical speed and kills horizontal drift");
        c.Vessel.LandedOrSplashed = true;
        Check(final.Drive(s) == null && c.Landing.Stopped && c.Thrust.TargetThrottle == 0,
            "Original autopilot stops thrust at real ground contact");
        c = Craft(100,10); c.VesselState.LimitedMaxThrustAcceleration = c.VesselState.MaxThrustAcceleration = 8;
        new FinalDescent(c).Drive(s);
        Check(c.Thrust.TransSpdAct == 0 && c.Thrust.TransKillH, "Insufficient TWR enters original maximum deceleration mode");
        var policy = new GravityTurnDescentSpeedPolicy(600000,9.81,20);
        double low = policy.MaxAllowedSpeed(new Vector3d(0,600500,0), new Vector3d(0,-200,0));
        double high = policy.MaxAllowedSpeed(new Vector3d(0,601000,0), new Vector3d(0,-200,0));
        Check(low > 0 && high > low, "Allowed speed falls as the ground approaches");
        var tracker = new DirectionTracker(); tracker.Update(QuaternionD.identity);
        var target = tracker.Desired(MathExtensions.Euler(10,0,0));
        Check(Math.Abs(target.error.x - 10 * Math.PI / 180) < 1e-8, "Original attitude tracker measures the pitch error in radians");
        var pid = new PIDLoop2 { Kp = 7.98, Ti = 0, Ts = 0.02, MinOutput = -2, MaxOutput = 2 };
        Check(pid.Update(10,0) == 2 && pid.Update(-10,0) == -2, "Original velocity PID respects available angular acceleration");
        Flight("vertical", 0.02, 3000,120,0,20);
        Flight("physics warp", 0.08,3000,120,0,20);
        Flight("sideways entry",0.02,9331,120,141,28);
        Flight("low TWR",0.02,3000,80,0,12);
        AttitudeFlight(0.02); AttitudeFlight(0.08);
        Approach("no gear / negligible drag", 0.02, 0, true);
        Approach("gear drag", 0.02, 0.00012, true);
        Approach("no gear / physics warp", 0.08, 0, true);
        Approach("no gear / configured 5 m/s", 0.02, 0, true, 5);
        Approach("gear / configured 5 m/s / warp", 0.08, 0.00012, true, 5);
        Approach("old unbounded coast regression", 0.02, 0, false);
        c = Craft(20000,100); c.VesselState.LimitedMaxThrustAcceleration = 8;
        Check(new CoastToDeceleration(c).Drive(s) is FinalDescent,
            "Low TWR does not wait for atmospheric braking indefinitely");
        c = Craft(20000,-100);
        Check(!c.Landing.NeedsBraking(), "Braking envelope cannot start a burn in ascent");
        c = Craft(20000,100); c.VesselState.LimitedMaxThrustAcceleration = 0;
        Check(!c.Landing.NeedsBraking(), "No available engine is not a finite braking prediction");
        c = Craft(1000,100); c.Attitude.torque = Vector3d.zero;
        c.Attitude.CurrentAttitude = MathExtensions.Euler(45,0,0);
        var ac = new BetterController(c.Attitude); ac.OnModuleEnabled();
        Vector3d act, error; ac.DrivePre(s,out act,out error);
        Check(act == Vector3d.zero, "No available torque produces no fictitious attitude authority");
    }
    static void SeparationTerrainRegression()
    {
        // Actual 0.8.1 KSP.log: altitude ~68909 m, PQS ~424 m, hull bottom -10.59 m.
        // A Kerbin scaled-space collider at 21.88 m replaced the true altitude with 11.29 m.
        bool scaled = BoosterWatch.GroundSurface.IsWorldSurface(10, false, false);
        Check(!scaled && (BoosterWatch.GroundSurface.WorldLayerMask & (1 << 10)) == 0,
            "Scaled planet is excluded both from raycast mask and hit selection");
        double trueAltitude = 68909.81 - 424.08;
        double clearance = BoosterWatch.GroundSurface.Clearance(trueAltitude - 10.59, -10.59, scaled, 21.88);
        Check(clearance > 68000, "Logged separation keeps its real 68 km height instead of 11 m");
        var c = Craft(trueAltitude, 261.96, 1958.87);
        c.VesselState.AltitudeBottom = clearance;
        c.VesselState.LocalGravity = 7.9; c.VesselState.GravityForce = new Vector3d(0,-7.9,0);
        c.VesselState.ThrustAvailable = c.VesselState.MaxThrustAcceleration = c.VesselState.LimitedMaxThrustAcceleration = 27.3;
        c.Landing.DecelerationEndAltitudeValue = 2715.7;
        var s = new FlightCtrlState { mainThrottle = 1 };
        var coast = new CoastToDeceleration(c);
        Check(coast.Drive(s) == coast, "Logged separation remains in coast with corrected terrain");
        c.Thrust.Drive(s);
        Check(s.mainThrottle == 0, "Corrected separation explicitly commands zero throttle");
        c.VesselState.AltitudeBottom = 21.88 - 10.59;
        Check(new CoastToDeceleration(c).Drive(s) is FinalDescent,
            "Regression reproduces premature final descent if the bad scaled hit is admitted");
        c.Thrust.Drive(s);
        Check(s.mainThrottle == 1, "The original false 11 m reading reproduces immediate full throttle");
        bool terrain = BoosterWatch.GroundSurface.IsWorldSurface(15, false, false);
        Check(terrain && BoosterWatch.GroundSurface.Clearance(30,-10,terrain,12) == 2,
            "Real local terrain still overrides procedural height near touchdown");
        Check(!BoosterWatch.GroundSurface.IsWorldSurface(15,false,true)
            && !BoosterWatch.GroundSurface.IsWorldSurface(15,true,false),
            "Vessel parts and triggers never become landing surfaces");
        Check(BoosterWatch.GroundSurface.Clearance(-0.2,-10,scaled,12) == -0.2,
            "Scaled planet cannot suppress distant procedural ground contact");
        Check(BoosterWatch.GroundSurface.Clearance(30,-10,true,double.NaN) == 30,
            "Invalid raycast distance never corrupts landing height");
    }
    static void Approach(string name, double dt, double drag, bool guard, double touchdownSpeed = 0.5)
    {
        TimeWarp.fixedDeltaTime = (float)dt;
        var c = Craft(25000,350,1600); var s = new FlightCtrlState();
        c.Landing.UseBrakingEnvelope = guard;
        c.Landing.TouchdownSpeed = touchdownSpeed;
        // Deliberately optimistic atmospheric estimate: coast until 200 m despite the actual
        // low drag. This reproduces the missing-prediction failure without needing a gear flag.
        c.Landing.DecelerationEndAltitudeValue = 200;
        AutopilotStep step = new CoastToDeceleration(c);
        c.VesselState.DeltaT = dt;
        c.VesselState.ThrustAvailable = c.VesselState.MaxThrustAcceleration = c.VesselState.LimitedMaxThrustAcceleration = 30;
        Vector3d position = c.VesselState.CoM, velocity = c.VesselState.SurfaceVelocity;
        double height = 25000, sink = 350, horizontal = 1600, firstBurn = -1, peak = 0;
        for (int i = 0; i < 900 / dt && height > 0; i++)
        {
            Vector3d up = position.normalized;
            c.VesselState.CoM = position; c.VesselState.Up = up;
            c.VesselState.AltitudeBottom = c.VesselState.AltitudeASL = c.VesselState.AltitudeTrue = height;
            c.VesselState.SurfaceVelocity = c.VesselState.OrbitalVelocity = velocity;
            c.VesselState.GravityForce = -9.81 * up;
            // Ideal attitude fixture. Real KSP torque and engine ignition remain protected by
            // the adapter and are not simulated here.
            c.VesselState.Forward = c.Attitude.Direction;
            step = step.Drive(s); c.VesselState.Forward = c.Attitude.Direction; c.Thrust.Drive(s);
            if (!PortMath.IsFinite(s.mainThrottle) || s.mainThrottle < 0 || s.mainThrottle > 1)
                throw new Exception("Invalid approach throttle");
            if (firstBurn < 0 && s.mainThrottle > 0.1) firstBurn = height;
            peak = Math.Max(peak, s.mainThrottle);
            Vector3d acceleration = -9.81 * up + 30 * s.mainThrottle * c.Attitude.Direction
                - drag * Math.Exp(-height / 6000) * velocity.magnitude * velocity;
            position += velocity * dt + 0.5 * acceleration * dt * dt;
            velocity += acceleration * dt;
            height = position.magnitude - 600000;
            sink = -Vector3d.Dot(velocity, position.normalized);
            horizontal = Vector3d.Exclude(position.normalized, velocity).magnitude;
        }
        Console.WriteLine(name + ": first burn=" + firstBurn + " sink=" + sink + " horizontal=" + horizontal + " peak=" + peak);
        if (guard)
            Check(height <= 0 && sink >= 0 && Math.Abs(sink - touchdownSpeed) < 1 && horizontal < 1 && firstBurn > 1000 && peak > 0.95,
                "Complete coast-to-contact approach brakes and lands: " + name);
        else
            Check(height <= 0 && (sink > 12 || horizontal > 3),
                "Fixture reproduces impact when the stopping envelope is disabled");
    }
    static void AttitudeFlight(double dt)
    {
        var c = Craft(1000,100); c.VesselState.DeltaT = dt;
        var controller = new BetterController(c.Attitude); controller.OnModuleEnabled();
        var s = new FlightCtrlState(); double angle = Math.PI/4, omega = 0;
        for(int i=0;i<30/dt;i++)
        {
            c.Attitude.CurrentAttitude = MathExtensions.Euler(angle*180/Math.PI,0,0);
            c.Attitude.AngularVelocity = new Vector3d(omega,0,0);
            Vector3d act,error; controller.DrivePre(s,out act,out error);
            if (!PortMath.IsFinite(act.x)) throw new Exception("Attitude output is invalid");
            omega += -PortMath.Clamp(act.x,-1,1)*2/5*dt;
            angle += omega*dt;
        }
        Check(Math.Abs(angle)<0.002 && Math.Abs(omega)<0.002,
            "Original BetterController settles a 45-degree error, physics step="+dt);
    }
    static void Flight(string name, double dt, double height, double sink, double horizontal, double accel)
    {
        TimeWarp.fixedDeltaTime = (float)dt;
        var c = Craft(height,sink,horizontal); var s = new FlightCtrlState();
        var step = new FinalDescent(c);
        c.VesselState.ThrustAvailable = c.VesselState.MaxThrustAcceleration = c.VesselState.LimitedMaxThrustAcceleration = accel;
        double seconds = 0, peak = 0;
        for (int i=0; i<60000 && height>0; i++)
        {
            c.VesselState.AltitudeBottom = c.VesselState.AltitudeASL = c.VesselState.AltitudeTrue = height;
            c.VesselState.CoM = new Vector3d(0,600000+height,0);
            c.VesselState.SurfaceVelocity = new Vector3d(horizontal,-sink,0);
            // Ideal orientation fixture; actual torque/rigid-body flight needs KSP.
            c.VesselState.Forward = c.Attitude.Direction;
            step.Drive(s); c.VesselState.Forward = c.Attitude.Direction;
            c.Thrust.Drive(s);
            if (float.IsNaN(s.mainThrottle) || s.mainThrottle<0 || s.mainThrottle>1) throw new Exception("Invalid throttle");
            peak = Math.Max(peak,s.mainThrottle); seconds += dt*s.mainThrottle;
            sink += (9.81 - accel*s.mainThrottle*c.Attitude.Direction.y)*dt;
            horizontal += accel*s.mainThrottle*c.Attitude.Direction.x*dt;
            height -= sink*dt;
        }
        Console.WriteLine(name+": sink="+sink+" horizontal="+horizontal+" burn="+seconds+" peak="+peak);
        Check(height<=0 && sink>=0 && sink<1.5 && Math.Abs(horizontal)<1,
            "Vendored landing + thrust controller reaches soft touchdown: " + name);
    }
}


