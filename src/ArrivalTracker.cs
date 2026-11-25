using System.Collections.Generic;

namespace Seamless
{
    /// <summary>
    /// Per-vessel memory: the bodies this vessel has already "arrived" at (saved with the vessel).
    /// A vessel seen for the first time is seeded with the body it is at when it is not still
    /// arriving there (sitting on the ground, or already on a closed orbit), so existing saves and
    /// fresh launches do not get a shot of the body they started from.
    /// </summary>
    public class ArrivalTracker : VesselModule
    {
        private readonly HashSet<string> visited = new HashSet<string>();
        private bool seeded;

        public bool HasVisited(CelestialBody b) { return b != null && visited.Contains(b.bodyName); }

        public void MarkVisited(CelestialBody b)
        {
            if (b != null) visited.Add(b.bodyName);
            seeded = true;
        }

        /// <summary>Called by the director before it looks at a vessel.</summary>
        public void EnsureSeeded()
        {
            if (seeded || vessel == null || vessel.mainBody == null) return;
            seeded = true;
            if (vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH || Arrival.IsCaptured(vessel.orbit, vessel.mainBody))
                visited.Add(vessel.mainBody.bodyName);
        }

        protected override void OnLoad(ConfigNode node)
        {
            visited.Clear();
            string v = node.GetValue("visited");
            if (!string.IsNullOrEmpty(v))
                foreach (string s in v.Split(';'))
                    if (s.Length > 0) visited.Add(s);
            string sd = node.GetValue("seeded");
            seeded = sd != null && bool.TryParse(sd, out bool r) && r;
        }

        protected override void OnSave(ConfigNode node)
        {
            node.SetValue("visited", string.Join(";", visited), true);
            node.SetValue("seeded", seeded.ToString(), true);
        }
    }
}
