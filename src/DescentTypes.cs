using System;

namespace BoosterWatch.Guidance
{
    // One accelerometer trip through the landing: what the guidance measured, what it decided,
    // and what it wants the engines and the attitude to do about it. All of it is plain data so
    // the whole flight can be replayed without KSP.
    public struct DescentState
    {
        public double Time;
        // Height of the LOWEST hull point above the ground, not the centre of mass.
        public double Clearance;
        // Atmosphere/gravity use ASL; the stopping target uses hull clearance above terrain.
        public double AltitudeAsl;
        // Local horizon frame at the vehicle: up, east and north as world-space unit vectors.
        // Carrying the frame instead of two speeds is what keeps the law free of the singularity
        // the old chain hit at low speed: the direction the engines have to point is built from
        // vectors, never by normalising a velocity that is going to zero.
        public double UpX, UpY, UpZ;
        public double EastX, EastY, EastZ;
        public double NorthX, NorthY, NorthZ;
        // Surface-relative velocity in that frame. Sink is positive downwards.
        public double VelocityUp, VelocityEast, VelocityNorth;
        // Everything the guidance can ask of the vehicle.
        public double Gravity, ThrustAcceleration, AirDensity;
        // Drag coefficient of the whole vessel along the flight direction: the sum of the drag-cube
        // faces facing the airstream times DragCubeMultiplier, exactly what MechJeb's VesselAverageDrag
        // returns. a_drag = 0.5 * rho * v^2 * DragCoefficient / mass, because mass is not constant
        // and the forecast has to burn propellant as it integrates.
        public double DragCoefficient;
        // Cd*A, mit dem die Vorhersage die Bremszuendung rechnet (0 = DragCoefficient). Kleiner als
        // der aktuelle Wert, wenn die Stufe gerade schief oder gleitend fliegt: unter Schub richtet
        // sie sich auf (DescentAdapter.Build).
        public double BurnDragCoefficient;
        public double AvailableDeltaV, AvailableBurnTime;
        // Drag along the velocity direction, positive = braking. The adapter recomputes it from the
        // real drag cubes every tick, because aero data belongs to the game and not to the law.
        public bool DragValid;
        // DragCoefficient is the value measured while flying straight backwards (not the current,
        // possibly gliding attitude). Without it the glide must not start: the forecast would plan
        // the whole descent with the 3-4x larger glide drag and ignite too late.
        public bool AxialDragKnown;
        public double DragAcceleration;
        // Auftrieb des Rumpfes im Verhaeltnis zum Widerstand, quer zur Bahn in der senkrechten
        // Ebene, positiv = nach oben. Gemessen im antriebslosen Flug (DescentAdapter.MeasureAero).
        // Flug vom 25.09.2026, 21:21: die Stufe hing ohne Steuerflaechen 10 Grad schief im Luftstrom
        // und der Rumpf drueckte sie mit 0,45 x Widerstand nach UNTEN - bis 40 m/s^2, so viel wie
        // das Triebwerk. Die Vorhersage kannte das nicht und zuendete 5 km zu spaet.
        public double LiftRatio;
        // Sekunden, die die Stufe vor der Zuendung noch ohne Schub faellt, weil sie aus dem Gleiten
        // zurueckdreht (DescentGuidance setzt sie waehrend des Gleitens). 0 = sofort zuendbereit.
        public double PreBurnSeconds;
        public bool LiftKnown;
        // Slope under the projected touchdown point [deg]. On a slope the booster lands on one edge
        // of its base before its centre reaches the ground, so the engines have to be cut higher.
        public double SlopeDegrees;
        public bool Valid;

        // Positive downwards, which is the sign the guidance works in.
        public double Sink { get { return -VelocityUp; } }
        public double LateralSpeed
        {
            get { return Math.Sqrt(VelocityEast * VelocityEast + VelocityNorth * VelocityNorth); }
        }
        // Unit vector along the horizontal VELOCITY in world space - the direction the booster is
        // drifting towards - or zero when there is none. Callers that want to oppose the drift or to
        // point retrograde have to carry the sign themselves; returning the magnitude's direction
        // here and a magnitude elsewhere is how a coasting booster ended up aimed along its own
        // velocity instead of against it.
        public void LateralDirection(out double x, out double y, out double z)
        {
            double speed = LateralSpeed;
            if (speed < 1e-9) { x = 0; y = 0; z = 0; return; }
            x = (EastX * VelocityEast + NorthX * VelocityNorth) / speed;
            y = (EastY * VelocityEast + NorthY * VelocityNorth) / speed;
            z = (EastZ * VelocityEast + NorthZ * VelocityNorth) / speed;
        }
    }

    public enum DescentPhase
    {
        // Nothing to do yet: before decoupling, or the autopilot is switched off.
        Idle = 0,
        // Turn the nose retrograde. No thrust, and no prediction - the booster is still flying
        // forwards with the upper stage and any reading would be meaningless.
        Align = 1,
        // In the atmosphere, engines dark. Drag is doing the work; the prediction decides when
        // that stops being true.
        Entry = 2,
        // The braking burn. One continuous law from the ignition altitude down.
        Burn = 3,
        // Inside the terminal band: the same law, but the target sink rate is now the fixed
        // touchdown speed and the allowed tilt is fading out.
        Terminal = 4,
        // Below the engine cut-off height. Thrust off, waiting for ground contact.
        Touchdown = 5,
        // The descent is over: landed and handed over, or splashed down.
        Done = 6,
        // There is nothing left to try: an impact cannot be avoided any more. The vehicle is still
        // flown - engines full, upright - but the outcome is a crash.
        Abort = 7
    }

    public struct GuidanceStep
    {
        public bool Valid;
        public DescentPhase Phase;
        // Net commanded acceleration, positive vertical = upwards.
        public double VerticalAcceleration, LateralAcceleration;
        public double Throttle;
        // Unit vertical acceleration command. The engine axis has to point along this vector.
        public double Up, East, North;
        // The same vector carrying its magnitude [m/s^2]: what the engines are actually being asked
        // to produce, component by component. Use these rather than the unit vector - the unit one
        // is only the attitude target, and scaling it by the throttle does not reproduce the
        // command, because the throttle is |a|/(T/m) of the whole vector and not of its vertical
        // part alone.
        public double AccelerationUp, AccelerationEast, AccelerationNorth;
        // The engines are being lit by this decision, not by an earlier one.
        public bool IgnitionDue;
        // The descent can no longer be stopped; the command is best effort from here.
        public bool Abort, Unstoppable;
        // The booster is crossing the sky with the engines dark, leaving the braking to the air.
        public bool Coasting;
        // Die Stufe gleitet schraeg statt rueckwaerts zu fallen (DescentConfig.GlideSettings).
        public bool Gliding;
        public double GlideDegrees;
        // Diagnostics for the coast decision: the total speed the air still has to remove, and the
        // speed the engines could remove over the height that is left.
        public double CoastSpeed, CoastBudget;
        // Drag deceleration the coast credited at the current speed [m/s^2].
        public double CoastDrag;
        // The profile asks for more braking than the engines have: the descent rate is at the
        // ceiling. Not an abort by itself, but the number that tells a weak booster from an easy one.
        public bool DescentRateLimited;
        public bool VectorBraking, EmergencyBraking;
        // Diagnostics that explain the decision.
        public double TargetSink, FreeAcceleration, TiltDegrees, TerminalTiltLimit;
        // Height above ground at which the engines are cut, raised for the slope under the
        // touchdown point.
        public double CutoffAltitude;
        public double IgnitionAltitude, IgnitionMargin, PredictedTouchdownSpeed;
        public double RequiredDeltaV, BurnSeconds, DampingGain;
    }

    public static class DescentPhases
    {
        public static string Name(DescentPhase phase)
        {
            switch (phase)
            {
                case DescentPhase.Align: return "Ausrichten";
                case DescentPhase.Entry: return "Eintritt";
                case DescentPhase.Burn: return "Bremszuendung";
                case DescentPhase.Terminal: return "Endphase";
                case DescentPhase.Touchdown: return "Aufsetzen";
                case DescentPhase.Done: return "Fertig";
                case DescentPhase.Abort: return "Absturz";
                default: return "Bereit";
            }
        }

        // The same phase for the window. Name() keeps its German wording because the flight recorder
        // writes it into the CSV, where a column that changes with the game language would break every
        // comparison; the window asks for the tag and gets the player's language.
        public static string Tag(DescentPhase phase)
        {
            switch (phase)
            {
                case DescentPhase.Align: return "#PSR_Phase_Align";
                case DescentPhase.Entry: return "#PSR_Phase_Entry";
                case DescentPhase.Burn: return "#PSR_Phase_Burn";
                case DescentPhase.Terminal: return "#PSR_Phase_Terminal";
                case DescentPhase.Touchdown: return "#PSR_Phase_Touchdown";
                case DescentPhase.Done: return "#PSR_Phase_Done";
                case DescentPhase.Abort: return "#PSR_Phase_Abort";
                default: return "#PSR_Phase_Ready";
            }
        }
    }
}
