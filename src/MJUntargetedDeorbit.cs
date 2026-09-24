// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.


namespace BoosterWatch.MechJebPort
{
    namespace Landing
    {
        internal class UntargetedDeorbit : AutopilotStep
        {
            public UntargetedDeorbit(MechJebCore core) : base(core)
            {
            }

            public override AutopilotStep Drive(FlightCtrlState s)
            {
                if (Orbit.PeA < -0.1 * MainBody.Radius)
                {
                    Core.Thrust.TargetThrottle = 0;
                    // Upstream returns new FinalDescent(Core) here. That is the targeted-landing
                    // shape: by the time MechJeb's own untargeted autopilot is invoked the vessel is
                    // already inside the atmosphere and FinalDescent's gravity-turn limit is enough.
                    // A booster handed over right after separation is still high and fast, and
                    // FinalDescent then only ever waits above 300 m: it turns retrograde and holds
                    // the throttle at zero until the attitude is within 45 degrees of retrograde.
                    // The port therefore enters the upstream steps in between, which coast with the
                    // engines dark and a retrograde attitude command and brake against the same
                    // speed limit MechJeb uses, so the final descent starts at the deceleration end
                    // altitude instead of at the top of the reentry.
                    return new CoastToDeceleration(Core);
                }

                Core.Attitude.attitudeTo(Vector3d.back, AttitudeReference.ORBIT_HORIZONTAL, Core.Landing);
                Core.Thrust.TargetThrottle = Core.Attitude.attitudeAngleFromTarget() < 5 ? 1 : 0;

                Status = "Deorbit-Bremsung"; //"Doing deorbit burn."

                return this;
            }
        }
    }
}
