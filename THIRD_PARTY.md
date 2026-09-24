# MechJeb source used by PhysStageRecovery 0.8.2

Upstream: https://github.com/MuMech/MechJeb2

Pinned revision: `a295713498582847ef7f8f50f913d03f099e9494`, retrieved 2026-09-22.
MechJeb2 copyright (C) 2013 MuMech and the MechJeb contributors.
MechJeb2 is licensed under GNU GPL version 3. PhysStageRecovery's combined distribution is GPL-3.0; see LICENSE.
The complete corresponding PhysStageRecovery source and build scripts accompany the DLL in PhysStageRecovery-Source.zip.

## Source mapping

| Included file | Upstream source |
| --- | --- |
| src/MJUntargetedDeorbit.cs | MechJeb2/LandingAutopilot/UntargetedDeorbit.cs |
| src/MJCoastToDeceleration.cs | MechJeb2/LandingAutopilot/CoastToDeceleration.cs |
| src/MJDecelerationBurn.cs | MechJeb2/LandingAutopilot/DecelerationBurn.cs |
| src/MJKillHorizontalVelocity.cs | MechJeb2/LandingAutopilot/KillHorizontalVelocity.cs |
| src/MJFinalDescent.cs | MechJeb2/LandingAutopilot/FinalDescent.cs |
| src/MJGravityTurn.cs | GravityTurnDescentSpeedPolicy, SafeDescentSpeedPolicy and PoweredCoastDescentSpeedPolicy in MechJeb2/MechJebModuleLandingAutopilot.cs |
| src/MJThrustDrive.cs | Speed-control section of Drive in MechJeb2/MechJebModuleThrustController.cs |
| src/MJPIDController.cs | Scalar PIDController in MechJeb2/PIDController.cs |
| src/MJBetterController.cs | MechJeb2/AttitudeControllers/BetterController.cs (default controller, index 3) |
| src/MJDirectionTracker.cs | MechJeb2/AttitudeControllers/DirectionTracker.cs |
| src/MJMathExtensions.cs | Euler/EulerAngles in MechJeb2/MathExtensions.cs |
| src/MJPIDLoop2.cs | MechJebLib/Control/PIDLoop2.cs |

The landing controller methods `DecelerationEndAltitude`, `UseAtmosphereToBrake`, `VesselAverageDrag`, `PickDescentSpeedPolicy`, `MaxAllowedSpeed` and `MaxAllowedSpeedAfterDt` in MJLandingContext.cs come from MechJebModuleLandingAutopilot.cs, and `DragLength`, `RealMaxAtmosphereAltitude` and `TerrainAltitude` in MJPortMath.cs come from CelestialBodyExtensions.cs.

Gear deployment in LandingSystems.cs follows DeployLandingGears in MechJebModuleLandingAutopilot.cs. The earlier ParachutePlan.StartPlanning multiplier and its height/stage conditions were removed in 0.8.1; ParachuteDeployment.cs arms stock-safe chutes immediately after confirmed descent. The RCS torque calculation in PoweredLanding.cs comes from VesselState.UpdateRCSThrustAndTorque. AltitudeForPressure is adapted from CelestialBodyExtensions.cs and the remaining math helpers in MJPortMath.cs from MechJebLib/Utils/Statics.cs.

MechJebLib files retain their original multi-license header. For the included PIDLoop2 and Statics helpers we select the offered MIT-0 license:

Copyright Lamont Granquist, Sebastien Gaggini and the MechJeb contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Adaptations, 2026-09-22

* Private `BoosterWatch.MechJebPort` namespace; no assembly references to MechJeb2, MechJebLib, MechJebLibBindings or alglib. No runtime discovery/reflection bridge. No MechJeb part required.
* Original untargeted state transitions and gravity-turn search retained. Target selection, target-site corrections, prediction UI, MechJeb's optional limiters and its unrelated autopilots are excluded.
* BetterController's numeric defaults and control equations retained; UI, persistence attributes and annotation dependencies removed. Attitude, moment of inertia and angular rate are injected by the per-vessel KSP adapter. Roll is uncontrolled as in the direction-only upstream landing API.
* PSR gathers stock-engine thrust, resources, torque and local terrain itself. It supports restartable, throttleable axial rocket engines. It uses normal vessel input callbacks; no position, rotation, resource or force is overwritten.
* The incremental thrust PID output is scaled by `fixedDeltaTime / 0.02` for PSR physics warp. This is a deliberate numerical adaptation, not byte-for-byte upstream behavior. Regression: without normalization, the 0.08 s vertical fixture hit at 30.75 m/s; with it, about 0.50 m/s.
* PSR retains its descent confirmation and 45-degree ignition/alignment guard for detached stages, plus watchdog, active-vessel and crew exclusions. Parachute deployment uses stock safety with PSR descent confirmation; there is no target-site optimization.
* Gear uses KSP's public CurrentState API and per-module deployment, including newly staged gear. Brakes and RCS use normal action groups. Physical touchdown confirmation belongs to PSR, not MechJeb. Recovery above the ground was removed in 0.8.0.

`tools/ImportLanding.ps1` reproduces the extracted files from the pinned repository in `build/mechjeb-upstream`. The imported files are already supplied, so neither the repository nor an installed MechJeb mod is required to build or run PhysStageRecovery.

## Adaptations, 2026-09-23

* `CoastToDeceleration`, `DecelerationBurn` and `KillHorizontalVelocity` are ported without the branches that need MechJeb's reentry simulation (`ReentrySimulation`, `Prediction`, `PredictionReady`), its warp module, its RCS balancer and its target selection: the warp-to-burn-start block, the target course correction (zero without a target anyway), the parachute plan calls, the `LandAtTarget` block and the `PredictionReady` waits. The underlying speed limits, throttle law and attitude commands are retained; 0.8.1 adds an early handover to FinalDescent based on the stopping envelope.
* `MJLandingContext.LandingController` rebuilds the descent speed policy on every physics tick instead of once in `StartLanding`, because the readings it depends on (thrust, drag, mass) only exist inside the flight loop. The drag sum is memoised for the current tick. With no usable thrust above local gravity the policy is absent and the speed limit is treated as unbounded, which is what MechJeb's `NaN` result does to the same comparisons.
* `ParachuteAddedDragCoef` was removed in 0.8.1. Only current weighted drag cubes contribute to the estimate, and `VesselAverageDrag` now sums all six faces instead of assigning only the final face.
* `UntargetedDeorbit` enters `CoastToDeceleration`, not `FinalDescent` as upstream does. Upstream reaches its untargeted autopilot with the vessel already deep in the atmosphere; a booster handed over at separation is still high and fast, and `FinalDescent` above 300 m only turns retrograde and holds the throttle at zero until the attitude is within 45 degrees of retrograde. Entering the upstream deceleration steps first coasts with the engines dark and a retrograde attitude command, brakes against MechJeb's own speed limit, and starts the final descent at the deceleration end altitude. Regression: the 0.5.2 flight log shows `Endanflug` with 0 % throttle from 22 km down to contact.
* `KillHorizontalVelocity` hands a vessel that points exactly straight up to `FinalDescent` instead of hovering, because there is no horizontal pointing direction to compare against.
* PSR keeps its own descent confirmation, ignition/alignment guard, watchdog, active-vessel and crew exclusions. The guard limits ignition to 45 degrees of attitude error and only cuts a burn that is already running past 100 degrees, so a commanded braking burn is not switched off while the booster is still slewing.
* Landing diagnostics `err` (attitude error) and `cmd` (throttle requested before the guard) are PSR additions.

## 0.8.1 stopping envelope

BrakingEnvelope.cs is a PSR addition using the vendored GravityTurnDescentSpeedPolicy. CoastToDeceleration and DecelerationBurn hand over to FinalDescent in the same physics tick when the predicted state reaches its stopping limit. Prediction allows at least one second or two physics ticks of reaction time and 200 m of approach reserve, uses current thrust and gravity, and assumes no future atmospheric or parachute braking. Low but nonzero thrust at or below local gravity immediately enters FinalDescent's low-TWR branch. No gear state enters this decision; no calibration burn is added. FinalDescent and the normal ignition/attitude gate continue to control actual thrust.

The test fixture runs full coast-to-contact trajectories with different actual drag, target touchdown speeds and physics timesteps, including the old unbounded-coast failure with this addition disabled. It uses ideal attitude and simplified dynamics and is not a KSP flight validation.
## 0.8.2 ground filtering

GroundSurface.cs is PSR-specific. Both the terrain raycast mask and hit selection exclude KSP 1.12.5 layer 10 (Scaled Scenery), whose miniature planet colliders are not physical flight-coordinate terrain. The 0.8.1 stopping-envelope regression was caused by interpreting one of those hits at 21.88 m as ground while the booster was at approximately 68 km above terrain. The fixture replays that failure through CoastToDeceleration and the thrust controller and verifies zero throttle with the corrected ground measurement. The MechJeb-derived guidance equations are unchanged in 0.8.2.
## 0.8.15 approach adaptation

ApproachDescentSpeedPolicy is a PSR addition combining the original gravity-turn limit with a conservative total-speed braking limit (300 m final-approach reserve, 80 percent of available thrust). FinalDescent uses this policy with local gravity and the higher of current hull clearance terrain and projected terrain. If the vessel rises below 300 m, it uses the existing upright vertical/drift controller instead of tracking retrograde below the horizon. CoastToDeceleration immediately executes FinalDescent when its independent drag-free limit is exceeded, avoiding an unbounded atmospheric DecelerationBurn target. These are intentional changes to the upstream guidance; actual attitude/ignition restrictions remain in the KSP adapter.
## 0.8.16 terminal guidance

PSR latches vertical/drift control below 300 m instead of switching between it and a full-throttle retrograde branch. The near-ground drift target in MJThrustDrive is capped at 30 degrees from up. ApproachDescentSpeedPolicy no longer subtracts 300 m, and the terminal ramp uses its same 80-percent-thrust braking coefficient, removing the zero-to-high-speed target discontinuity at the phase boundary. Attitude diagnostics were added without changing BetterController or fabricating torque. No tests were run for this change at user request.
## 0.8.17 MechJeb handover and PID reset

FinalDescent restores the upstream >= 5 m/s horizontal-speed retrograde/full-throttle branch below 300 m; vertical/drift speed control is used below that threshold. PSR retains exceptions for upward bounce and insufficient thrust. The 30-degree drift cap now applies only during upward bounce. The thrust-mode setter resets the scalar PID on mode changes, corresponding to MechJeb's Tmode setter plus OnUpdate reset, executed synchronously because PSR has no separate module OnUpdate. Earlier approach policy and ignition protections remain in effect. No tests were run at user request.
## 0.8.18 ground consistency and terminal phase

PSR accepts terrain ray hits only within 250 m of the procedural terrain distance from the ray origin. Rejected hits do not suppress the existing procedural ground-contact fallback. FinalDescent evaluates its 300 m terminal condition on each physics tick instead of permanently latching it after one low sample. The MechJeb horizontal-speed handover and throttle-mode PID reset remain. No tests were run at user request.

## 0.9.11 atmosphere-aware predictive ignition

DescentPrediction, DescentGuidance, and DescentAdapter are PSR's standalone prediction/flight bridge. The predictor evaluates unpowered descent followed by the same powered controller used in flight, including full-body atmosphere coverage, AGL-to-ASL conversion, pressure-based engine performance, actual mass flow and propellant limits. It targets the 100 m capture before the existing terminal controller. Competing ignition heuristics and the 20 km ceiling were removed; invalid forecasts retain an explicitly logged conservative fallback. The ported MechJeb attitude controller and legacy landing path remain unchanged in this release. No tests were run at user request; flight validation remains outstanding.
## 0.9.12 coast attitude and persistent capture altitude

PSR's predictive guidance retains its coast attitude until the first positive burn command, even if the internal phase already says Burn. This fixes the radial fallback for a zero thrust vector without changing the powered/terminal controller. The capture altitude is now loaded and saved in settings.cfg; prediction and flight use the same setting. No upstream MechJeb code changed. No tests were run at user request.
## 0.9.14 vector braking and live powered prediction

CaptureBraking is a PSR addition shared by flight guidance and the trajectory predictor. It assigns vertical and lateral braking the same capture deadline, credits measured drag by component, releases the thrust reserve when required, and prioritizes vertical stopping authority if the requested vector exceeds available thrust. Slow terminal drift retains its tilt limit. Powered predictions copy current controller history and elapsed time since the first positive command instead of restarting the burn ramp. Forecasts remain live after ignition; pending dark burns can be replanned. Existing MechJeb-derived attitude control and ignition gates are unchanged. No tests were run at user request; KSP flight confirmation remains outstanding.
