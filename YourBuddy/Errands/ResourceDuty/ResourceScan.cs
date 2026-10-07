using System.Collections.Generic;
using NPC.Core;
using UnityEngine;

namespace YourBuddy
{
    // Shops and cells in switched-off rooms too; Items.Loadable decides, LoadRoomOf switches them on.
    // docs/resources.md#scans-and-storage
    internal static class ResourceScan
    {
        private const float Rescan = 5f;
        private static Shop[] shops = [];
        private static float shopsAt;
        private static ResourceContainer[] cells = [];
        private static float cellsAt;
        private static readonly List<ResourceContainer> Merged = [];

        internal static Shop[] Shops() => Cached(ref shops, ref shopsAt);

        // Active cells from this frame's sweep, so a cell just bought counts; switched-off ones from the cache.
        internal static List<ResourceContainer> Cells()
        {
            Merged.Clear();
            Merged.AddRange(SceneScan.ThisFrame<ResourceContainer>());
            foreach (ResourceContainer cell in Cached(ref cells, ref cellsAt))
            {
                if (cell != null && !cell.gameObject.activeInHierarchy) Merged.Add(cell);
            }
            return Merged;
        }

        private static T[] Cached<T>(ref T[] items, ref float at) where T : Object
        {
            if ((at > 0f && Time.time < at) || !SceneScan.MayRescan(at <= 0f)) return items;
            at = Time.time + Rescan;
            items = Object.FindObjectsOfType<T>(true);
            return items;
        }
    }
}
