using System.Collections.Generic;
using NPC.Core.Navigation;
using NPC.Core.World;
using UnityEngine;

namespace YourBuddy
{
    // Storage follows active room-anchored nodes, not a fixed ship layout. docs/resources.md
    internal static class ResourceStorage
    {
        private static readonly Collider[] Overlaps = new Collider[96];
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly Vector3[] Offsets =
        [Vector3.zero, new(-.4f, 0, 0), new(.4f, 0, 0), new(0, 0, -.4f), new(0, 0, .4f),
            new(-.8f, 0, 0), new(.8f, 0, 0), new(0, 0, -.8f), new(0, 0, .8f),
            new(-.4f, 0, -.4f), new(-.4f, 0, .4f), new(.4f, 0, -.4f), new(.4f, 0, .4f)];
        private static readonly ResourceProbeBudget ProbeBudget = new(4);
        internal static bool MayProbe() => ProbeBudget.Take(Time.frameCount);

        internal static string LastBlocker { get; private set; } = "no supported floor";

        internal static Vector3 Clearance(Vector3 size)
        {
            float radius = Mathf.Max(.2f, new Vector2(size.x, size.z).magnitude);
            return new Vector3(radius + .04f, Mathf.Max(.2f, size.y) + .02f, radius + .04f);
        }

        internal static void Candidates(Vector3 near, List<Vector3> points)
        {
            points.Clear();
            for (int i = 0; i < NavGraph.NodeCount; i++)
            {
                if (!NavGraph.IsNodeActive(i) || NavGraph.GetNodeOwner(i) != NavGraph.ShipOwner ||
                    NavGraph.GetNodeType(i) != NodeType.Ground) continue;
                Vector3 node = NavGraph.GetNodeWorld(i);
                foreach (Vector3 offset in Offsets) points.Add(node + offset);
            }
            points.Sort((a, b) => (a - near).sqrMagnitude.CompareTo((b - near).sqrMagnitude));
        }

        internal static bool FindFloor(Vector3 point, out RaycastHit floor, bool shipOnly = true)
        {
            floor = default;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * .3f, Vector3.down, Hits, 1.8f,
                NavProbe.ProbeLayers, QueryTriggerInteraction.Ignore);
            if (count == Hits.Length) return false;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].distance >= nearest) continue;
                nearest = Hits[i].distance;
                floor = Hits[i];
            }
            return floor.collider != null && floor.normal.y > .98f &&
                floor.collider.GetComponentInParent<Grabbable>() == null &&
                (!shipOnly || NpcVessels.OwnerOfTransform(floor.collider.transform) == NavGraph.ShipOwner);
        }

        internal static bool Clear(Vector3 center, Vector3 half, Transform? item, bool shipOnly = true)
        {
            if (NpcDoors.ChamberAt(center, withShip: true) != null) return Blocked("airlock chamber");
            // Require support beneath the whole footprint, on the same deck.
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 sample = center + new Vector3(x * half.x, 0, z * half.z);
                    if (!FindFloor(sample, out RaycastHit floor, shipOnly) ||
                        Mathf.Abs(floor.point.y - (center.y - half.y - .03f)) > .04f) return Blocked("uneven or missing floor support");
                }
            }
            int count = Physics.OverlapBoxNonAlloc(center, half, Overlaps, Quaternion.identity,
                ~0, QueryTriggerInteraction.Collide);
            if (count == Overlaps.Length) return Blocked("overlap buffer full");
            int restricted = LayerMask.NameToLayer("PlaceRestriction");
            int solidMask = item != null ? NavProbe.CollisionMaskFor(item.gameObject.layer) : NavProbe.ProbeLayers;
            for (int i = 0; i < count; i++)
            {
                Collider hit = Overlaps[i];
                if (item != null && hit.transform.IsChildOf(item)) continue;
                if ((!hit.isTrigger && (solidMask & (1 << hit.gameObject.layer)) != 0) || hit.gameObject.layer == restricted ||
                    hit.GetComponentInParent<ItemDetector>() != null ||
                    hit.GetComponentInParent<ItemDestroyer>() != null)
                    return Blocked("blocked by " + hit.name + " (layer " + hit.gameObject.layer + ")");
            }
            return true;
        }

        private static bool Blocked(string why) { LastBlocker = why; return false; }
    }
}
