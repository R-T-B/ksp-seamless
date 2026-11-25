using System;
using UnityEngine;

namespace Seamless
{
    /// <summary>A camera the departure flight can drive (flight / Space Center camera, or the
    /// Tracking Station's planetarium camera). Poses are relative to a fixed anchor point.</summary>
    internal interface ICamDriver
    {
        Action TakenAway { get; set; }
        void Take();
        CamPose Current(Vector3d anchor);
        void Apply(Vector3d cam, Quaternion rot, double tanV, double skyWeight);
        /// <summary>Restores the camera for the scene change (the frozen frame covers the screen).</summary>
        void Leave();
    }

    /// <summary>No camera move (the editor, the main menu: the card comes in over the view, then it freezes).</summary>
    internal sealed class NullDriver : ICamDriver
    {
        public Action TakenAway { get; set; }
        public void Take() { }
        public CamPose Current(Vector3d anchor) { return new CamPose(Vector3d.zero, Quaternion.identity, 1.0); }
        public void Apply(Vector3d cam, Quaternion rot, double tanV, double skyWeight) { }
        public void Leave() { }
    }

    /// <summary>
    /// Drives the Tracking Station's planetarium camera (the scaled-space camera; the whole
    /// Tracking Station is drawn in scaled space). Positions are given in local (world) space and
    /// converted from doubles, so deep-space flights stay precise.
    /// </summary>
    internal sealed class PlanetariumRig : ICamDriver
    {
        private readonly Settings S;
        private readonly PlanetariumCamera pc;
        private readonly Camera cam;
        private readonly float origFov;
        private readonly GalaxyCubeControl galaxy;
        private readonly Color galaxyMax;
        private Transform prevScaledTarget;
        private float prevGalaxyFov = -1f;
        private bool taken, left;
        private OrbitLines lines;   // orbit lines, map icons and CommNet links, hidden during the flight

        public Action TakenAway { get; set; }

        public PlanetariumRig(Settings s, PlanetariumCamera pc)
        {
            S = s;
            this.pc = pc;
            cam = pc.GetComponent<Camera>();
            origFov = cam != null ? cam.fieldOfView : 60f;
            galaxy = GalaxyCubeControl.Instance;
            if (galaxy != null) galaxyMax = galaxy.maxGalaxyColor;
        }

        public void Take()
        {
            if (taken) return;
            taken = true;
            pc.enabled = false;   // its LateUpdate would put the camera back on its orbit target
            lines = OrbitLines.Hide();
            ScaledCamera sc = ScaledCamera.Instance;
            if (sc != null)
            {
                prevScaledTarget = sc.tgtRef;
                sc.tgtRef = null;
                if (sc.galaxyCamera != null) prevGalaxyFov = sc.galaxyCamera.fieldOfView;
            }
        }

        public CamPose Current(Vector3d anchor)
        {
            Vector3d pos = ScaledSpace.ScaledToLocalSpace(pc.transform.position);
            float fov = cam != null ? cam.fieldOfView : origFov;
            return new CamPose(pos - anchor, pc.transform.rotation, Math.Tan(fov * 0.5 * Math.PI / 180.0));
        }

        public void Apply(Vector3d camPos, Quaternion rot, double tanV, double skyWeight)
        {
            try { if (lines != null) lines.Keep(); } catch { }
            pc.transform.position = (Vector3)ScaledSpace.LocalToScaledSpace(camPos);
            pc.transform.rotation = rot;
            float fov = (float)(2.0 * Math.Atan(tanV) * 180.0 / Math.PI);
            if (cam != null) cam.fieldOfView = fov;
            ScaledCamera sc = ScaledCamera.Instance;
            if (sc != null && sc.galaxyCamera != null)
                sc.galaxyCamera.fieldOfView = Mathf.Max(fov, Mathf.Min(S.skyboxMinFov, origFov));
            if (galaxy != null)
            {
                float k = Mathf.Lerp(1f, S.skyboxBrightness, Mathf.Clamp01((float)skyWeight));
                galaxy.maxGalaxyColor = new Color(galaxyMax.r * k, galaxyMax.g * k, galaxyMax.b * k, galaxyMax.a);
            }
        }

        public void Leave()
        {
            if (left) return;
            left = true;
            try
            {
                if (cam != null) cam.fieldOfView = origFov;
                ScaledCamera sc = ScaledCamera.Instance;
                if (sc != null)
                {
                    if (sc.galaxyCamera != null && prevGalaxyFov > 0f) sc.galaxyCamera.fieldOfView = prevGalaxyFov;
                    if (taken && sc.tgtRef == null) sc.tgtRef = prevScaledTarget;
                }
                if (galaxy != null) galaxy.maxGalaxyColor = galaxyMax;
                if (pc != null) pc.enabled = true;
                if (lines != null) { lines.Restore(); lines = null; }
            }
            catch (Exception e) { Log.Warn("planetarium camera restore: " + e.Message); }
        }
    }
}
