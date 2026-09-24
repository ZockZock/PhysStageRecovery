// Adapted from MechJeb2 a295713498582847ef7f8f50f913d03f099e9494. GPL-3.0. See THIRD_PARTY.md.
using System;
using UnityEngine;
using static BoosterWatch.MechJebPort.PortMath;
namespace BoosterWatch.MechJebPort { internal static class MathExtensions {
        public static Vector3d EulerAngles(QuaternionD q)
        {
            double magnitude = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);

            if (magnitude < EPS)
                return Vector3d.zero;

            if (Math.Abs(magnitude - 1.0) > 1e-10)
            {
                q.x /= magnitude;
                q.y /= magnitude;
                q.z /= magnitude;
                q.w /= magnitude;
            }

            double sqw = q.w * q.w;
            double sqx = q.x * q.x;
            double sqy = q.y * q.y;
            double sqz = q.z * q.z;

            double unit = sqx + sqy + sqz + sqw;
            double test = q.x * q.w - q.y * q.z;

            if (test > 0.499999999 * unit) // North pole gimbal lock
            {
                double yaw = 2.0 * Math.Atan2(q.y, q.w);

                return new Vector3d(
                    90,
                    Rad2Deg(Clamp2Pi(yaw)),
                    0
                );
            }

            if (test < -0.499999999 * unit) // South pole gimbal lock
            {
                double yaw = -2.0 * Math.Atan2(q.y, q.w);

                return new Vector3d(
                    270,
                    Rad2Deg(Clamp2Pi(yaw)),
                    0
                );
            }
            else
            {
                double pitch = Math.Asin(2.0 * test / unit);
                double yaw = Math.Atan2(2.0 * (q.x * q.z + q.w * q.y), sqw - sqx - sqy + sqz);
                double roll = Math.Atan2(2.0 * (q.x * q.y + q.w * q.z), sqw - sqx + sqy - sqz);

                // Convert to degrees
                return new Vector3d(
                    Rad2Deg(Clamp2Pi(pitch)),
                    Rad2Deg(Clamp2Pi(yaw)),
                    Rad2Deg(Clamp2Pi(roll))
                );
            }
        }

        public static QuaternionD Euler(double x, double y, double z)
        {
            x = Deg2Rad(x);
            y = Deg2Rad(y);
            z = Deg2Rad(z);

            double cx = Math.Cos(x * 0.5);
            double sx = Math.Sin(x * 0.5);
            double cy = Math.Cos(y * 0.5);
            double sy = Math.Sin(y * 0.5);
            double cz = Math.Cos(z * 0.5);
            double sz = Math.Sin(z * 0.5);

            var q = new QuaternionD { w = cz * cx * cy + sz * sx * sy, x = cz * sx * cy - sz * cx * sy, y = cz * cx * sy + sz * sx * cy, z = sz * cx * cy - cz * sx * sy };

            return q;
        }

} }
