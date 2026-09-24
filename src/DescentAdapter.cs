using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BoosterWatch.Guidance;

namespace BoosterWatch
{
    // Bridges the game and the landing law.
    //
    // Everything the guidance needs from KSP is gathered here, once per physics tick, and the one
    // command it returns - a net acceleration in the local horizon frame - is turned back into a
    // throttle and a target attitude for the attitude controller. Nothing in Guidance.cs knows that
    // KSP exists, which is what lets the whole approach be flown in the test suite.
    public sealed class DescentAdapter : IDescentModel, IDescentEngineModel
    {
        // Air sampled over the corridor around the booster. KSP's own pressure curve, so the
        // forecast works with the atmosphere the booster is really in rather than an exponential
        // guess. Rebuilt when the booster has moved or the profile has gone stale.
        private double profileStep = 100;
        private double[] density = new double[0];
        private CelestialBody profileBody;
        private ModuleEngines[] profileEngines = new ModuleEngines[0];
        public double FullMassFlow { get; private set; }
        public double BodyRadius { get { return profileBody == null ? 0 : profileBody.Radius; } }
        private double profileLatitude = double.NaN, profileLongitude = double.NaN;
        private double profileTime = double.NegativeInfinity;

        public double VacuumThrustAcceleration { get; private set; }
        public double SeaLevelThrustAcceleration { get; private set; }
        // IDescentModel: the forecast reads the engine capability in the same two forms.
        double IDescentModel.ThrustAccelerationVacuum { get { return VacuumThrustAcceleration; } }
        double IDescentModel.ThrustAccelerationSeaLevel { get { return SeaLevelThrustAcceleration; } }
        public double AvailableAcceleration { get { return VacuumThrustAcceleration; } }
        public double Mass { get; private set; }
        public double AvailableDeltaV { get; private set; }
        public double AvailableBurnTime { get; private set; }
        // Diagnostics for the log line.
        public double VesselDragCoefficient { get; private set; }
        public int Engines { get; private set; }
        public bool EnginesUsable { get { return Engines > 0; } }
        public string EngineNote = "";

        public double AirDensity(double altitudeAsl)
        {
            if (profileBody == null || !profileBody.atmosphere || altitudeAsl >= profileBody.atmosphereDepth) return 0;
            return Sample(density, altitudeAsl);
        }

        public double Gravity(double altitudeAsl)
        {
            if (profileBody == null) return 9.81;
            double radius = profileBody.Radius + altitudeAsl;
            return profileBody.gravParameter / (radius * radius);
        }

        private double Sample(double[] table, double altitude)
        {
            if (table.Length == 0) return 0;
            double top = profileStep * (table.Length - 1);
            if (altitude >= top) return table[table.Length - 1];
            if (altitude <= 0) return table[0];
            double x = altitude / profileStep;
            int i = (int)x;
            if (i + 1 >= table.Length) return table[table.Length - 1];
            return table[i] + (table[i + 1] - table[i]) * (x - i);
        }

        // Body gravity, exact, from the same inverse square KSP uses.
        private double GravityAt(Vessel vessel, double altitudeAsl)
        {
            double radius = vessel.mainBody.Radius + altitudeAsl;
            return vessel.mainBody.gravParameter / (radius * radius);
        }

        // Atmosphere and gravity of the descent corridor. One pass of pressure evaluations, which
        // is why it is not redone on every tick.
        public void RefreshProfile(Vessel vessel, double now)
        {
            bool moved = double.IsNaN(profileLatitude)
                || Math.Abs(vessel.latitude - profileLatitude) > 0.2
                || Math.Abs(vessel.longitude - profileLongitude) > 0.2;
            if (!moved && profileBody == vessel.mainBody && now - profileTime < 10) return;
            profileLatitude = vessel.latitude;
            profileLongitude = vessel.longitude;
            profileTime = now;
            CelestialBody body = vessel.mainBody;
            profileBody = body;
            profileStep = Math.Max(100, body.atmosphereDepth / 1024);
            int count = Math.Max(2, (int)Math.Ceiling(body.atmosphereDepth / profileStep) + 1);
            density = new double[count];
            for (int i = 0; i < count; i++)
            {
                double altitude = i * profileStep;
                density[i] = !body.atmosphere || altitude >= body.atmosphereDepth ? 0
                    : Math.Max(0, body.GetDensity(body.GetPressure(altitude), body.GetTemperature(altitude)));
            }
            Calibrate(vessel);
        }

        // The corridor table against the game's own reading for the vessel.
        //
        // Everything in this table comes out of one KSP call, and a wrong unit in it is invisible
        // until the air gets thick. Third flight: at 9 km the table said ~500 times the density the
        // game reported for the vessel itself, so the forecast integrated a wall of air (arrival
        // speed 1e23 m/s) and the drag credit in the law went to a four-digit deceleration, which
        // zeroed the vertical command and dropped the booster the last nine kilometres unpowered.
        // `Vessel.atmDensity` is the same number KSP feeds its own drag force, so it is the
        // reference: where the two disagree, the table is scaled onto it. The scale is reported in
        // the log rather than applied silently, because a table that needs scaling is a call that
        // needs fixing.
        private void Calibrate(Vessel vessel)
        {
            DensityScale = 1;
            double live = vessel.atmDensity;
            double mine = Sample(density, vessel.altitude);
            if (!RecoveryPolicy.Finite(live) || live <= 1e-12) return;
            if (!RecoveryPolicy.Finite(mine) || mine <= 1e-12) return;
            double scale = live / mine;
            if (!RecoveryPolicy.Finite(scale) || scale <= 0) return;
            DensityScale = scale;
            // Close enough is left alone; a factor is corrected, and a wild one is refused outright
            // rather than multiplied into the table.
            if (scale > 0.98 && scale < 1.02) return;
            if (scale < 0.01 || scale > 100) return;
            for (int i = 0; i < density.Length; i++) density[i] *= scale;
        }

        // How far the corridor table had to be scaled onto the game's own density. 1 is agreement.
        public double DensityScale { get; private set; }

        // All engines that can actually fly this landing: axial, throttleable, restartable and
        // pointing where the guidance wants the thrust to go.
        public List<ModuleEngines> UsableEngines(Vessel vessel, Vector3d thrustAxis, out string note)
        {
            note = "";
            List<ModuleEngines> all = vessel.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).ToList();
            List<ModuleEngines> usable = new List<ModuleEngines>();
            bool unsupported = false;
            foreach (ModuleEngines engine in all)
            {
                if (!EngineSelection.Suitable(engine) || !EngineSelection.HasPropellant(engine)) continue;
                if (engine.part.ShieldedFromAirstream && !engine.shieldedCanActivate) continue;
                if (engine.thrustTransforms.Count == 0) continue;
                Vector3 axis = Vector3.zero;
                foreach (Transform transform in engine.thrustTransforms) axis -= transform.forward;
                if (Vector3d.Dot(axis.normalized, thrustAxis) < 0.9) { unsupported = true; continue; }
                usable.Add(engine);
            }
            if (all.Any(e => e.EngineIgnited && !EngineSelection.Suitable(e))) unsupported = true;
            if (unsupported) note = "ungeeignetes Triebwerk";
            else if (usable.Count == 0) note = "kein nutzbarer Schub";
            return usable;
        }

        // Thrust acceleration at full throttle, in vacuum and at sea level. The ratio is what the
        // forecast needs; the adapter's own command uses the current atmospheric value.
        public void Measure(Vessel vessel, List<ModuleEngines> engines, double throttleCeiling)
        {
            profileEngines = engines.ToArray();
            Engines = engines.Count;
            Mass = Math.Max(0.001, vessel.GetTotalMass());
            double pressure = (float)(vessel.staticPressurekPa / 101.325);
            double vacuum = 0, sea = 0;
            foreach (ModuleEngines engine in engines)
            {
                double now = engine.MaxThrustOutputVac(true);
                double atSea = engine.MaxThrustOutputAtm(false, true, 1f, vessel.externalTemperature, 1.225f);
                if (!RecoveryPolicy.Finite(now) || now < 0) now = 0;
                if (!RecoveryPolicy.Finite(atSea) || atSea < 0) atSea = now;
                vacuum += now;
                sea += atSea;
            }
            VacuumThrustAcceleration = vacuum / Mass;
            SeaLevelThrustAcceleration = sea / Mass;
            // Limit the burn by the first resource that runs out at the actual mixture ratio.
            // Summing fuel and oxidizer masses overestimates endurance if either is left over.
            var available = new Dictionary<int, double>();
            var demand = new Dictionary<int, double>();
            double totalFlow = 0;
            foreach (ModuleEngines engine in engines)
            {
                double isp = engine.atmosphereCurve == null ? 0 : engine.atmosphereCurve.Evaluate(0f);
                double thrust = engine.MaxThrustOutputVac(true);
                if (!RecoveryPolicy.Finite(isp) || isp <= 1 || !RecoveryPolicy.Finite(thrust) || thrust <= 0) continue;
                double mixtureMass = 0;
                foreach (Propellant p in engine.propellants)
                {
                    var definition = PartResourceLibrary.Instance.GetDefinition(p.id);
                    if (definition != null && p.ratio > 0) mixtureMass += p.ratio * definition.density;
                }
                if (mixtureMass <= 0) continue;
                double flow = thrust / (isp * 9.80665); // tonnes/s
                totalFlow += flow;
                foreach (Propellant p in engine.propellants)
                {
                    var definition = PartResourceLibrary.Instance.GetDefinition(p.id);
                    if (definition == null || definition.density <= 0 || p.ratio <= 0) continue;
                    double have, capacity, previous;
                    engine.part.GetConnectedResourceTotals(p.id, p.GetFlowMode(), out have, out capacity, true);
                    // Conservative for separate feed networks, and counts shared tanks only once.
                    available[p.id] = available.TryGetValue(p.id, out previous) ? Math.Min(previous, have) : have;
                    demand[p.id] = (demand.TryGetValue(p.id, out previous) ? previous : 0) + flow * p.ratio / mixtureMass;
                }
            }
            AvailableBurnTime = double.PositiveInfinity;
            foreach (var entry in demand)
                AvailableBurnTime = Math.Min(AvailableBurnTime, Math.Max(0, available[entry.Key]) / entry.Value);
            if (!RecoveryPolicy.Finite(AvailableBurnTime)) AvailableBurnTime = 0;
            double dryMass = Math.Max(0.001, Mass - totalFlow * AvailableBurnTime);
            AvailableDeltaV = totalFlow > 0 ? vacuum / totalFlow * Math.Log(Mass / dryMass) : 0;
            FullMassFlow = 1000 * totalFlow;
        }

        // Rocket thrust follows pressure/Isp, not a blend based on air density.
        // Return acceleration at the measured mass; the predictor scales it as fuel is spent.
        public double ThrustAccelerationAt(double altitudeAsl)
        {
            if (profileBody == null) return VacuumThrustAcceleration;
            float pressure = (float)profileBody.GetPressureAtm(altitudeAsl);
            double force = 0;
            foreach (ModuleEngines engine in profileEngines)
            {
                if (engine.atmosphereCurve == null) continue;
                double vacuumIsp = engine.atmosphereCurve.Evaluate(0);
                if (vacuumIsp > 0)
                    force += engine.MaxThrustOutputVac(true)
                        * Math.Max(0, engine.atmosphereCurve.Evaluate(pressure)) / vacuumIsp;
            }
            return Math.Max(0, force / Math.Max(0.001, Mass));
        }

        // The real drag the hull is producing right now, as an acceleration. KSP's drag cubes already
        // carry the direction: facing backwards a booster presents its smallest area and this number
        // is small, which is exactly why an aligned reentry is hot and a tumbling one is not.
        //
        // This is fed to the guidance, not just logged. In the upper atmosphere a returning booster
        // meets more drag than its own weight - around 20 m/s^2 at 2 km/s - and a law that does not
        // know that reads its rising altitude as "too slow" and opens the engines to push down.
        //
        // The area comes from KSP's own `DragCubeList.AreaDrag`, the way MechJeb reads it
        // (`VesselState`: `AreaDrag += p.DragCubes.AreaDrag * PhysicsGlobals.DragCubeMultiplier *
        // PhysicsGlobals.DragMultiplier`). This used to be rebuilt by hand from
        // `WeightedDrag[face] * AreaOccluded[face] / 6`, which silently drops `DragMultiplier` and
        // divides a sum that is already a whole-hull product by six. The error multiplies v^2: at
        // 100 m/s it is invisible, at 2000 m/s it turned 2 m/s^2 of drag into 884 and the law
        // believed it - two flights in a row. Take the game's number, not a reconstruction of it.
        public void MeasureDrag(Vessel vessel)
        {
            double areaDrag = 0, coefficient = 0;
            foreach (Part part in vessel.parts)
            {
                if (part.DragCubes.None || part.ShieldedFromAirstream) continue;
                areaDrag += part.DragCubes.AreaDrag * PhysicsGlobals.DragCubeMultiplier
                    * PhysicsGlobals.DragMultiplier;
                coefficient += part.DragCubes.DragCoeff;
            }
            VesselDragCoefficient = areaDrag;
            VesselDragCd = coefficient;
            double speed = vessel.srf_velocity.magnitude;
            double density = AirDensityNow(vessel);
            double mass = Math.Max(0.001, vessel.GetTotalMass());
            // 0.5 * rho * v^2 * Cd*A is a force in newtons; the mass is in tonnes.
            DragAcceleration = areaDrag <= 0 || density <= 0 ? 0
                : 0.5 * density * speed * speed * areaDrag / (1000 * mass);
            if (!RecoveryPolicy.Finite(DragAcceleration) || DragAcceleration < 0) DragAcceleration = 0;
        }

        // The game's own density for the vessel, and the corridor table as the fallback. Both come
        // from the same KSP call MechJeb uses; `Vessel.atmDensity` is the value KSP itself builds the
        // drag force from, so it is the one to trust.
        public double AirDensityNow(Vessel vessel)
        {
            double live = vessel.atmDensity;
            if (RecoveryPolicy.Finite(live) && live > 0) return live;
            return AirDensity(vessel.altitude);
        }

        // Dimensionless drag coefficient of the hull, for the log. Cd*A is what the force uses.
        public double VesselDragCd { get; private set; }

        // Drag deceleration along the flight path, positive = braking.
        public double DragAcceleration { get; private set; }

        // The local horizon frame at the booster: up, east and north as world-space unit vectors.
        public static void HorizonFrame(Vessel vessel, out Vector3d up, out Vector3d east, out Vector3d north)
        {
            up = (vessel.CoMD - vessel.mainBody.position).normalized;
            north = Vector3d.Exclude(up, (vessel.mainBody.position + (Vector3d)vessel.mainBody.transform.up * vessel.mainBody.Radius) - vessel.CoMD).normalized;
            if (north.sqrMagnitude < 0.5) north = Vector3d.Exclude(up, vessel.mainBody.transform.forward).normalized;
            east = Vector3d.Cross(up, north).normalized;
        }

        // Everything the guidance reads, in one struct.
        //
        // `trackedSlope` is the slope the tracker measured under the projected touchdown point,
        // using the same ground sources and plausibility rules as the landing height itself. When
        // it is not available (no tracker reading yet) the adapter's own forward scan stands.
        public DescentState Build(Vessel vessel, double clearance, double time, double bottomOffset,
            double trackedSlope)
        {
            Vector3d up, east, north;
            HorizonFrame(vessel, out up, out east, out north);
            Vector3d velocity = vessel.srf_velocity;
            // The ground ahead of the booster, not only the ground below it. While the booster is
            // drifting, a rising slope in its path is closer than its own radar altitude says, and
            // the smaller of the two readings is the one the descent has to respect.
            GroundClearance = clearance;
            SlopeDegrees = RecoveryPolicy.Finite(trackedSlope) ? Math.Max(0, trackedSlope) : 0;
            LookingAhead = false;
            Vector3d drift = Vector3d.Exclude(up, velocity);
            double horizontalSpeed = drift.magnitude;
            if (horizontalSpeed > GroundScan.MinimumDrift)
            {
                drift = drift / horizontalSpeed;
                double ahead, slope;
                if (GroundScan.AlongFlightPath(vessel.CoMD, vessel.altitude, up, drift, horizontalSpeed,
                    bottomOffset, TerrainHeightAt(vessel), out ahead, out slope))
                {
                    LookingAhead = true;
                    // The tracker's slope wins when it has one: it comes from the ground sources the
                    // landing height itself is built from.
                    if (!RecoveryPolicy.Finite(trackedSlope)) SlopeDegrees = slope;
                    if (ahead < clearance) clearance = ahead;
                }
            }
            DescentState state = new DescentState
            {
                Time = time,
                Clearance = clearance,
                AltitudeAsl = vessel.altitude,
                UpX = up.x, UpY = up.y, UpZ = up.z,
                EastX = east.x, EastY = east.y, EastZ = east.z,
                NorthX = north.x, NorthY = north.y, NorthZ = north.z,
                VelocityUp = Vector3d.Dot(velocity, up),
                VelocityEast = Vector3d.Dot(velocity, east),
                VelocityNorth = Vector3d.Dot(velocity, north),
                Gravity = GravityAt(vessel, vessel.altitude),
                ThrustAcceleration = ThrustAccelerationAt(vessel.altitude),
                AirDensity = AirDensityNow(vessel),
                DragCoefficient = VesselDragCoefficient,
                DragValid = true,
                DragAcceleration = DragAcceleration,
                AvailableDeltaV = AvailableDeltaV,
                AvailableBurnTime = AvailableBurnTime,
                SlopeDegrees = SlopeDegrees,
                Valid = true
            };
            return state;
        }

        // The clearance of the ground directly below, before the flight-path scan is applied - the
        // number the old code produced and the one the log line compares the scan against.
        public double GroundClearance { get; private set; }
        // Slope under the projected touchdown point [deg].
        public double SlopeDegrees { get; private set; }
        // The scan found lower ground ahead than below.
        public bool LookingAhead { get; private set; }

        // Terrain height at an arbitrary world position, from the procedural surface. This is the
        // only height source that works at every distance, which is why the forward scan uses it:
        // KSP builds ground colliders around the active vessel only.
        public static GroundScan.HeightAt TerrainHeightAt(Vessel vessel)
        {
            CelestialBody body = vessel.mainBody;
            return position =>
            {
                double latitude = body.GetLatitude(position), longitude = body.GetLongitude(position);
                double ground = body.TerrainAltitude(latitude, longitude, true);
                if (!RecoveryPolicy.Finite(ground)) ground = body.TerrainAltitude(latitude, longitude);
                return body.ocean ? Math.Max(0, ground) : ground;
            };
        }

        // The command back into the game: the thrust axis as a world-space direction, and the
        // throttle that produces the commanded acceleration.
        public static void ThrustAxis(Vessel vessel, GuidanceStep step, Vector3d up, Vector3d east,
            Vector3d north, out Vector3d axis)
        {
            axis = (up * step.Up + east * step.East + north * step.North).normalized;
            if (axis.sqrMagnitude < 0.5) axis = up;
        }
    }
}

