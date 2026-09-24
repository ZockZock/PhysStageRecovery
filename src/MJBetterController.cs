// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
using UnityEngine;
using static BoosterWatch.MechJebPort.PortMath;
using static System.Math;

namespace BoosterWatch.MechJebPort
{
    internal class BetterController : BaseAttitudeController
    {
        private const int SETTINGS_VERSION = 16;

        private const double POS_KP_DEFAULT = 2.03;
        private const double POS_TI_DEFAULT = 1.97;
        private const double POS_TD_DEFAULT = 0.0;
        private const double POS_N_DEFAULT = 1.0;
        private const double POS_B_DEFAULT = 1.0;
        private const double POS_C_DEFAULT = 1.0;
        private const double POS_DEADBAND_DEFAULT = 0.0;
        private const bool POS_CLEGG_DEFAULT = false;
        private const double POS_SMOOTH_IN_DEFAULT = 1.0;
        private const double POS_SMOOTH_OUT_DEFAULT = 1.0;

        private const double VEL_KP_DEFAULT = 7.98;
        private const double VEL_TI_DEFAULT = 0;
        private const double VEL_TD_DEFAULT = 0;
        private const double VEL_N_DEFAULT = 1.0;
        private const double VEL_B_DEFAULT = 1.0;
        private const double VEL_C_DEFAULT = 1.0;
        private const double VEL_DEADBAND_DEFAULT = 0.0;
        private const bool VEL_CLEGG_DEFAULT = false;
        private const double VEL_SMOOTH_IN_DEFAULT = 1.0;
        private const double VEL_SMOOTH_OUT_DEFAULT = 1.0;

        private const double MAX_STOPPING_TIME_DEFAULT = 2;
        private const double MIN_FLIP_TIME_DEFAULT = 120;
        private const double ROLL_CONTROL_RANGE_DEFAULT = 5;
        private const double SMOOTH_TORQUE_DEFAULT = 0.10;
        private const double SOFTEN_DEFAULT = 0.5;

        private readonly PIDLoop2[] _velPID = { new PIDLoop2(), new PIDLoop2(), new PIDLoop2() };
        private readonly PIDLoop2[] _posPID = { new PIDLoop2(), new PIDLoop2(), new PIDLoop2() };
        private readonly DirectionTracker _directionTracker = new DirectionTracker();
        public double MaxStoppingTime = MAX_STOPPING_TIME_DEFAULT;
        public double MinFlipTime = MIN_FLIP_TIME_DEFAULT;
        public double PosDeadband = POS_DEADBAND_DEFAULT;
        public double PosKp = POS_KP_DEFAULT;
        public double PosTi = POS_TI_DEFAULT;
        public double PosTd = POS_TD_DEFAULT;
        public double PosN = POS_N_DEFAULT;
        public double PosB = POS_B_DEFAULT;
        public double PosC = POS_C_DEFAULT;
        public bool PosClegg = POS_CLEGG_DEFAULT;
        public double PosSmoothIn = POS_SMOOTH_IN_DEFAULT;
        public double PosSmoothOut = POS_SMOOTH_OUT_DEFAULT;
        public double RollControlRange = ROLL_CONTROL_RANGE_DEFAULT;
        public double VelB = VEL_B_DEFAULT;
        public double VelC = VEL_C_DEFAULT;
        public double VelDeadband = VEL_DEADBAND_DEFAULT;
        public double VelKp = VEL_KP_DEFAULT;
        public double VelN = VEL_N_DEFAULT;
        public double VelSmoothIn = VEL_SMOOTH_IN_DEFAULT;
        public double VelSmoothOut = VEL_SMOOTH_OUT_DEFAULT;
        public double VelTd = VEL_TD_DEFAULT;
        public double VelTi = VEL_TI_DEFAULT;

        // Soften should run between (0,1] to reduce overshoot on large angle maneuvers
        public double Soften = SOFTEN_DEFAULT;

        private Vector3d _actuation = Vector3d.zero;

        /* error in pitch, roll, yaw */
        private Vector3d _error = Vector3d.zero;
        private Vector3d _current = Vector3d.zero;
        private Vector3d _desired = Vector3d.zero;

        /* error */
        private double _distance;

        /* max angular acceleration */
        private Vector3d _maxAlpha = Vector3d.zero;

        /* max angular rotation */
        private Vector3d _targetOmega = Vector3d.zero;
        private Vector3d _targetAlpha = Vector3d.zero;
        private Vector3d _targetTorque = Vector3d.zero;
        private Vector3d _controlTorque = Vector3d.zero;
        public double SmoothTorque = SMOOTH_TORQUE_DEFAULT;
        public bool UseControlRange = true;
        public bool UseFlipTime = true;
        public bool UseStoppingTime = true;
        public bool VelClegg = VEL_CLEGG_DEFAULT;
        public int Version = -1;

        public BetterController(MechJebModuleAttitudeController controller) : base(controller)
        {
        }



        private void Defaults()
        {
            // Position PID defaults
            PosKp = POS_KP_DEFAULT;
            PosTi = POS_TI_DEFAULT;
            PosTd = POS_TD_DEFAULT;
            PosN = POS_N_DEFAULT;
            PosB = POS_B_DEFAULT;
            PosC = POS_C_DEFAULT;
            PosDeadband = POS_DEADBAND_DEFAULT;
            PosSmoothOut = POS_SMOOTH_OUT_DEFAULT;
            PosSmoothIn = POS_SMOOTH_IN_DEFAULT;
            PosClegg = POS_CLEGG_DEFAULT;

            // Velocity PID defaults
            VelKp = VEL_KP_DEFAULT;
            VelTi = VEL_TI_DEFAULT;
            VelTd = VEL_TD_DEFAULT;
            VelN = VEL_N_DEFAULT;
            VelB = VEL_B_DEFAULT;
            VelC = VEL_C_DEFAULT;
            VelDeadband = VEL_DEADBAND_DEFAULT;
            VelSmoothIn = VEL_SMOOTH_IN_DEFAULT;
            VelSmoothOut = VEL_SMOOTH_OUT_DEFAULT;
            VelClegg = VEL_CLEGG_DEFAULT;

            // Miscellaneous defaults
            MaxStoppingTime = MAX_STOPPING_TIME_DEFAULT;
            MinFlipTime = MIN_FLIP_TIME_DEFAULT;
            RollControlRange = ROLL_CONTROL_RANGE_DEFAULT;
            UseControlRange = true;
            UseFlipTime = true;
            UseStoppingTime = true;
            SmoothTorque = SMOOTH_TORQUE_DEFAULT;
            Soften = SOFTEN_DEFAULT;

            Version = SETTINGS_VERSION;
        }

        public override void OnModuleEnabled()
        {
            if (Version < SETTINGS_VERSION)
                Defaults();
            Reset();
        }

        public override void DrivePre(FlightCtrlState s, out Vector3d act, out Vector3d deltaEuler)
        {
            UpdatePredictionPI();

            deltaEuler = _error * Mathf.Rad2Deg;

            for (int i = 0; i < 3; i++)
                if (Abs(_actuation[i]) < EPS || double.IsNaN(_actuation[i]))
                    _actuation[i] = 0;

            act = _actuation;
        }

        private void UpdatePredictionPI()
        {
            QuaternionD currentAttitude = Ac.CurrentAttitude;

            _current = _directionTracker.Update(currentAttitude);
            (_desired, _error, _distance) = _directionTracker.Desired(Ac.RequestedAttitude);

            // low-pass filter the control torque
            _controlTorque = _controlTorque == Vector3d.zero ? Ac.torque : _controlTorque + SmoothTorque * (Ac.torque - _controlTorque);

            // if torque is really zero, set it zero
            for (int i = 0; i < 3; i++)
                if (Ac.torque[i] == 0)
                    _controlTorque[i] = 0;

            // needed to stop wiggling at higher phys warp
            double warpFactor = Ac.VesselState.DeltaT / 0.02;

            // see https://archive.is/NqoUm and the "Alt Hold Controller", the acceleration PID is not implemented, so we only
            // have the first two PIDs in the cascade.
            for (int i = 0; i < 3; i++)
            {
                _maxAlpha[i] = _controlTorque[i] / Ac.MomentOfInertia[i];

                if (_maxAlpha[i] == 0)
                    _maxAlpha[i] = 1;

                if (IsFinite(Ac.OmegaTarget[i]))
                {
                    _targetOmega[i] = Ac.OmegaTarget[i];
                }
                else
                {
                    double soften = Clamp01(Soften);
                    double posKp = PosKp / warpFactor;
                    double effLD = soften * soften * _maxAlpha[i] / (2 * posKp * posKp);

                    double maxOmega = double.PositiveInfinity;

                    if (UseStoppingTime)
                    {
                        maxOmega = _maxAlpha[i] * MaxStoppingTime;
                        if (UseFlipTime) maxOmega = Max(maxOmega, PI / MinFlipTime);
                    }

                    if (Abs(_error[i]) <= 2 * effLD)
                    {
                        _posPID[i].Kp = posKp;
                        _posPID[i].Ti = PosTi;
                        _posPID[i].Td = PosTd;
                        _posPID[i].N = PosN;
                        _posPID[i].B = PosB;
                        _posPID[i].C = PosC;
                        _posPID[i].Ts = Ac.VesselState.DeltaT;
                        _posPID[i].SmoothIn = Clamp01(PosSmoothIn);
                        _posPID[i].SmoothOut = Clamp01(PosSmoothOut);
                        _posPID[i].MinOutput = -maxOmega;
                        _posPID[i].MaxOutput = maxOmega;
                        _posPID[i].IntegralDeadband = PosDeadband * maxOmega;
                        _posPID[i].Clegg = PosClegg;

                        _targetOmega[i] = _posPID[i].Update(_desired[i], _current[i]);
                    }
                    else
                    {
                        _posPID[i].Reset();
                        // v = - sqrt(2 * F * x / m) is target stopping velocity based on distance
                        _targetOmega[i] = soften * Sqrt(2 * _maxAlpha[i] * (Abs(_error[i]) - effLD)) * Sign(_error[i]);
                        _targetOmega[i] = Clamp(_targetOmega[i], -maxOmega, maxOmega);
                    }

                    if (UseControlRange && _distance * Mathf.Rad2Deg > RollControlRange)
                    {
                        _targetOmega[1] = 0;
                        _posPID[1].Reset();
                    }
                }

                _velPID[i].Kp = VelKp;
                _velPID[i].Ti = VelTi;
                _velPID[i].Td = VelTd;
                _velPID[i].N = VelN;
                _velPID[i].B = VelB;
                _velPID[i].C = VelC;
                _velPID[i].Ts = Ac.VesselState.DeltaT;
                _velPID[i].SmoothIn = Clamp01(VelSmoothIn);
                _velPID[i].SmoothOut = Clamp01(VelSmoothOut);
                _velPID[i].MinOutput = -_maxAlpha[i];
                _velPID[i].MaxOutput = _maxAlpha[i];
                _velPID[i].IntegralDeadband = VelDeadband * _maxAlpha[i];
                _velPID[i].Clegg = VelClegg;

                _targetAlpha[i] = _velPID[i].Update(_targetOmega[i], Ac.AngularVelocity[i]);

                _targetTorque[i] = Ac.MomentOfInertia[i] * _targetAlpha[i];

                // need the negative from the pid due to KSP's orientation of actuation
                _actuation[i] = -_targetTorque[i] / _controlTorque[i];

                if (Ac.ActuationControl[i] == 0 || _controlTorque[i] == 0 || Ac.AxisControl[i] == 0)
                {
                    _actuation[i] = 0;
                    Reset(i);
                }

                if (Abs(_actuation[i]) < EPS || double.IsNaN(_actuation[i]))
                    _actuation[i] = 0;
            }
        }

        public override void Reset()
        {
            Reset(0);
            Reset(1);
            Reset(2);
            _directionTracker.Reset();
        }

        public override void Reset(int i)
        {
            _velPID[i].Reset();
            _posPID[i].Reset();
            _directionTracker.Reset(i);
        }

    }
}
