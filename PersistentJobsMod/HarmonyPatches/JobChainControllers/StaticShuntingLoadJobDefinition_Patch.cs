using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Utilities;
using System.Collections.Generic;
using System.Reflection;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers
{

    [HarmonyPatch(typeof(StaticShuntingLoadJobDefinition), nameof(StaticShuntingLoadJobDefinition.GetRequiredTrackReservations))]
    public static class StaticShuntingLoadJobDefinition_Patch
    {
        private static readonly FieldInfo _TrackIDTrackType = ReflectionUtilities.CompatAccess.Field(typeof(TrackID), "trackType");

        public static void Postfix(ref List<TrackReservation> __result, Track ___destinationTrack)
        {
            if (__result != null && ((string)(_TrackIDTrackType.GetValue(___destinationTrack.ID)) == "L"))
            {
                Main._modEntry.Logger.Log("SL job prevented from reserving space on " + __result[0].track.ID.FullDisplayID);
                __result = new List<TrackReservation>();
            }
        }
    }
}
