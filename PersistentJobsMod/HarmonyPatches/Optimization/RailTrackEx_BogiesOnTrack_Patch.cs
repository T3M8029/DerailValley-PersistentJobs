using DV.Utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using VLB;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch]
    public static class RailTrackEx_BogiesOnTrack_Patch
    {
        [HarmonyPatch(typeof(RailTrackOnTrackBogiesExtensions), nameof(RailTrackOnTrackBogiesExtensions.BogiesOnTrack))]
        [HarmonyPostfix]
        public static void Postfix(RailTrack rt, HashSet<Bogie> __result)
        {
            if (Main.Settings.DummyBogiesForTracksOfSuspendedCars && WorldStreamingInit.IsLoaded)
            {
                if (PersistentJobsMod.Optimization.FarCarOpt.AllTracks == null || PersistentJobsMod.Optimization.FarCarOpt.AllTracks.Length == 0) PersistentJobsMod.Optimization.FarCarOpt.AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;

                int i = Array.IndexOf(PersistentJobsMod.Optimization.FarCarOpt.AllTracks, rt);
                if (i != -1 && PersistentJobsMod.Optimization.FarCarOpt.OccupiedRailTrackIndexes.Contains(i))
                {
                    GameObject tempGO = new($"PersistentJobs_PlaceholderBogieHolder_{rt.gameObject.name}");
                    tempGO.SetActive(false);
                    Bogie placeholder = tempGO.GetOrAddComponent<Bogie>();
                    __result.Add(placeholder);
                    MonoBehaviour.Destroy(tempGO, 5f);
                }
            }
        }
    }
}
