using System;
using UnityEngine;

namespace Seamless
{
    /// <summary>
    /// Shot helpers shared by the arrival shot and the loading-screen transitions. A Shot is
    /// solved once; per frame it keeps its offset from its subject's anchor and is re-aimed so
    /// the subject (planet centre, or the vessel) stays on the same screen ray.
    /// </summary>
    internal static class Shots
    {
        public static double VesselSize(Vessel v)
        {
            try
            {
                if (v.loaded && v.parts != null && v.parts.Count > 0)
                {
                    Vector3 s = ShipConstruction.CalculateCraftSize(v.parts, v.rootPart);
                    return Math.Max(2.0, Math.Max(s.x, Math.Max(s.y, s.z)));
                }
                if (v.protoVessel != null && v.protoVessel.protoPartSnapshots != null && v.protoVessel.protoPartSnapshots.Count > 0)
                {
                    Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
                    foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                    {
                        lo = Vector3d.Min(lo, p.position);
                        hi = Vector3d.Max(hi, p.position);
                    }
                    Vector3d d = hi - lo;
                    return Math.Max(2.0, Math.Max(d.x, Math.Max(d.y, d.z)) + 2.5);
                }
            }
            catch { }
            return 5.0;
        }

        public static Vector3d SunDir(CelestialBody body)
        {
            CelestialBody star = Arrival.StarOf(body);
            return star != null ? (star.position - body.position).normalized : Vector3d.zero;
        }

        /// <summary>Is <paramref name="b"/> the home body or one of its moons (any depth)?</summary>
        public static bool InSystemOf(CelestialBody b, CelestialBody home)
        {
            for (int i = 0; i < 16 && b != null; i++)
            {
                if (b == home) return true;
                if (b.referenceBody == null || b.referenceBody == b) break;
                b = b.referenceBody;
            }
            return false;
        }

        /// <summary>Arrival-style planet shot, re-aimed on the planet centre.</summary>
        public static CamPose Reaim(Shot shot, Vector3d anchor, CelestialBody body, double sx, double tanV)
        {
            Vector3d cam = anchor + shot.Offset;
            Vector3 toPlanet = (Vector3)(body.position - cam).normalized;
            Vector3 rp = (Vector3)ShotSolver.Ray(sx, 0, tanV, CameraRig.Aspect);
            return new CamPose(shot.Offset, Quaternion.FromToRotation(shot.Rot * rp, toPlanet) * shot.Rot, tanV);
        }

        /// <summary>A shot around a vessel, re-aimed so the vessel stays on <paramref name="ray"/>.</summary>
        public static CamPose ReaimOnVessel(Vector3d offset, Quaternion rot, Vector3 ray, double tanV)
        {
            Vector3 toVessel = (Vector3)(-offset).normalized;
            return new CamPose(offset, Quaternion.FromToRotation(rot * ray, toVessel) * rot, tanV);
        }

        public static bool SurfaceLike(Vessel v)
        {
            switch (v.situation)
            {
                case Vessel.Situations.LANDED:
                case Vessel.Situations.SPLASHED:
                case Vessel.Situations.PRELAUNCH:
                case Vessel.Situations.FLYING:
                    return true;
            }
            CelestialBody b = v.mainBody;
            return b.atmosphere && v.altitude < b.atmosphereDepth;
        }

        /// <summary>Altitude above which KSP switches the body's terrain off (and with it the
        /// colliders), seen from the camera; NaN when unknown.</summary>
        public static double TerrainOffAltitude(CelestialBody b)
        {
            try
            {
                if (b.pqsController == null) return double.NaN;
                var m = b.pqsController.GetComponentInChildren<PQSMod_CelestialBodyTransform>(true);
                if (m != null && m.deactivateAltitude > 0) return m.deactivateAltitude;
            }
            catch { }
            return double.NaN;
        }

        // ------------------------------------------------------------------ vessel shots

        /// <summary>
        /// High aerial view of a vessel on or near the ground: above it and back towards the sun,
        /// looking down past it at the horizon. Stays below half the terrain-off altitude so the
        /// ground (and the colliders under the vessel) never switch off.
        /// </summary>
        public static void Aerial(Settings s, Vector3d vesselPos, CelestialBody body, double vesselAlt,
                                  out Vector3d offset, out Quaternion rot, out Vector3 ray, out double tanV)
        {
            Vector3d up = (vesselPos - body.position).normalized;
            Vector3d sun = SunDir(body);
            Vector3d horiz = sun - up * Vector3d.Dot(sun, up);
            if (horiz.sqrMagnitude < 1e-6) horiz = ShotSolver.Perp(up);
            horiz = horiz.normalized;

            double off = TerrainOffAltitude(body);
            double h = double.IsNaN(off) ? 20000.0 : 0.5 * off - Math.Max(0.0, vesselAlt);
            h = Math.Max(1500.0, Math.Min(h, 40000.0));
            offset = up * h + horiz * (2.2 * h);
            tanV = Math.Tan(s.cameraFov * 0.5 * Math.PI / 180.0);
            ray = (Vector3)ShotSolver.Ray(0.0, -0.12, tanV, CameraRig.Aspect);
            rot = ShotSolver.AimAnyRay(-offset, (Vector3d)ray, up);
        }
    }
}
