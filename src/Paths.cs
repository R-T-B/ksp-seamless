using System;
using UnityEngine;

namespace Seamless
{
    /// <summary>
    /// Zooms between a far planet shot and a pose close to the anchor (the craft, the Space Center)
    /// when the planet stands between them - the sunlit far shot is often on the other side of the
    /// planet from the craft. Instead of a straight zoom through the planet (which the altitude
    /// clamp then pushed along the surface, with a kink where it let go), the camera swings around
    /// the planet: its direction from the planet's centre turns from the first camera's to the
    /// last one's while its altitude comes down (log scale), looking at the planet and turning to
    /// the anchor as it rises over the horizon. Once the anchor is in view the normal zoom takes
    /// over, blended in so there is no corner.
    /// </summary>
    internal static class Paths
    {
        internal delegate CamPose Zoom(CamPose from, CamPose to, double s);

        internal sealed class Plan
        {
            public double Sw;   // where (0..1) the anchor comes into view along the way around
        }

        /// <summary>The sphere that counts as "the planet in the way": 1 % above the surface, but never
        /// above half the anchor's own altitude (a craft on the launch pad or the Space Center sits
        /// within 1 % of the radius - with 1 % alone every surface zoom went "around").</summary>
        private static double BlockRadius(double r, Vector3d anchor, Vector3d centre)
        {
            double alt = Math.Max(0.0, (anchor - centre).magnitude - r);
            return r + Math.Min(0.01 * r, 0.5 * alt);
        }

        /// <summary>Does the straight line from a to b pass within radius of centre?</summary>
        public static bool Blocked(Vector3d a, Vector3d b, Vector3d centre, double radius)
        {
            Vector3d ab = b - a;
            double l2 = ab.sqrMagnitude;
            double u = l2 > 0 ? Math.Max(0.0, Math.Min(1.0, Vector3d.Dot(centre - a, ab) / l2)) : 0.0;
            return (a + ab * u - centre).magnitude < radius;
        }

        /// <summary>A plan for going around the planet, or null when the anchor can be seen from the
        /// first camera (the normal zoom is used).</summary>
        public static Plan Make(CamPose from, CamPose to, Vector3d anchor, Vector3d centre, double radius)
        {
            double rb = BlockRadius(radius, anchor, centre);
            if (!Blocked(anchor + from.Off, anchor, centre, rb)) return null;
            double sw = 0.85;
            for (int i = 1; i <= 68; i++)
            {
                double s = i * 0.0125;   // up to 0.85
                Vector3d cam = OrbitPos(from, to, anchor, centre, radius, s);
                if (!Blocked(cam, anchor, centre, rb)) { sw = s; break; }
            }
            return new Plan { Sw = Math.Max(0.45, sw) };
        }

        /// <summary>The pose at <paramref name="s"/> (already eased, 0..1).</summary>
        public static CamPose Around(CamPose from, CamPose to, Vector3d anchor, Vector3d centre, double radius,
                                     double s, Plan plan, Zoom direct)
        {
            CamPose orbit = OrbitPose(from, to, anchor, centre, radius, s, plan.Sw);
            if (s <= plan.Sw) return orbit;
            double u = Math.Min(1.0, (s - plan.Sw) / Math.Max(1e-6, 1.0 - plan.Sw));
            CamPose w0 = OrbitPose(from, to, anchor, centre, radius, plan.Sw, plan.Sw);
            CamPose d = direct(w0, to, u);
            double w = Smooth(u);
            return new CamPose(orbit.Off + (d.Off - orbit.Off) * w,
                               Quaternion.Slerp(orbit.Rot, d.Rot, (float)w),
                               Math.Exp(Math.Log(orbit.TanV) + (Math.Log(d.TanV) - Math.Log(orbit.TanV)) * w));
        }

        /// <summary>Camera position (world) on the way around: direction from the centre done by
        /// 80 % of the way, altitude coming down from 20 % on.</summary>
        private static Vector3d OrbitPos(CamPose from, CamPose to, Vector3d anchor, Vector3d centre, double radius, double s)
        {
            Vector3d a = anchor + from.Off - centre, b = anchor + to.Off - centre;
            double la = Math.Max(1.0, a.magnitude), lb = Math.Max(1.0, b.magnitude);
            double ha = Math.Max(1.0, la - radius), hb = Math.Max(1.0, lb - radius);
            double sd = Smooth(s / 0.8), sh = Smooth((s - 0.2) / 0.8);
            Vector3 dir = Vector3.Slerp((Vector3)(a / la), (Vector3)(b / lb), (float)sd).normalized;
            double h = Math.Exp(Math.Log(ha) + (Math.Log(hb) - Math.Log(ha)) * sh);
            return centre + (Vector3d)dir * (radius + h);
        }

        private static CamPose OrbitPose(CamPose from, CamPose to, Vector3d anchor, Vector3d centre, double radius,
                                         double s, double sw)
        {
            Vector3d cam = OrbitPos(from, to, anchor, centre, radius, s);
            // the view: the planet where the first shot had it, turning to the anchor where the last
            // pose has it, done when the anchor comes into view
            double k = Smooth(s / Math.Max(1e-6, sw));
            Vector3d camFrom = anchor + from.Off;
            Vector3 rayP = Quaternion.Inverse(from.Rot) * (Vector3)(centre - camFrom).normalized;
            Vector3 rayA = Quaternion.Inverse(to.Rot) * (Vector3)(-to.Off).normalized;
            if (rayP.z < 0.05f) rayP = Vector3.forward;
            if (rayA.z < 0.05f) rayA = Vector3.forward;
            Vector3 ray = Vector3.Slerp(rayP, rayA, (float)k).normalized;
            Vector3d subject = centre + (anchor - centre) * k;
            Vector3 up = Vector3.Slerp(from.Rot * Vector3.up, to.Rot * Vector3.up, (float)Smooth(s));
            Quaternion rot = ShotSolver.AimAnyRay(subject - cam, (Vector3d)ray, (Vector3d)up);
            double tanV = Math.Exp(Math.Log(from.TanV) + (Math.Log(to.TanV) - Math.Log(from.TanV)) * s);
            return new CamPose(cam - anchor, rot, tanV);
        }

        private static double Smooth(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return x * x * (3.0 - 2.0 * x);
        }
    }
}
