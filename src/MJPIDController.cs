// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
using System;
namespace BoosterWatch.MechJebPort {
    internal class PIDController : IConfigNode
    {
        private double _prevError;
        public double INTAccum, Kp, Ki, Kd;
        private readonly double _max;
        private readonly double _min;

        public PIDController(double kp = 0, double ki = 0, double kd = 0, double max = double.MaxValue, double min = double.MinValue)
        {
            Kp = kp;
            Ki = ki;
            Kd = kd;
            _max = max;
            _min = min;
            Reset();
        }

        public double Compute(double error)
        {
            INTAccum += error * TimeWarp.fixedDeltaTime;
            double action = Kp * error + Ki * INTAccum + Kd * (error - _prevError) / TimeWarp.fixedDeltaTime;
            double clamped = Math.Max(_min, Math.Min(_max, action));
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (clamped != action)
                INTAccum -= error * TimeWarp.fixedDeltaTime;

            _prevError = error;

            return action;
        }

        public void Reset() => _prevError = INTAccum = 0;

        public void Load(ConfigNode node)
        {
            if (node.HasValue("Kp"))
                Kp = Convert.ToDouble(node.GetValue("Kp"));

            if (node.HasValue("Ki"))
                Ki = Convert.ToDouble(node.GetValue("Ki"));

            if (node.HasValue("Kd"))
                Kd = Convert.ToDouble(node.GetValue("Kd"));
        }

        public void Save(ConfigNode node)
        {
            node.SetValue("Kp", Kp.ToString());
            node.SetValue("Ki", Ki.ToString());
            node.SetValue("Kd", Kd.ToString());
        }
    }

}
