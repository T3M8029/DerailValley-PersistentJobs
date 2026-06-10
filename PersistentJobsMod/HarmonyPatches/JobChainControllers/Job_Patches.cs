using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System.Linq;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers
{
    [HarmonyPatch]
    public static class Job_Patches
    {
        [HarmonyPatch(typeof(Job), nameof(Job.ExpireJob))]
        [HarmonyPrefix]
        public static bool Prefix(Job __instance)
        {
            var controllerOfJob = UnityEngine.Object.FindObjectsOfType<StationController>()?.FirstOrDefault(st => st.logicStation.availableJobs.Contains(__instance))?.ProceduralJobsController.GetCurrentJobChains().FirstOrDefault(jcc => jcc.currentJobInChain == __instance);
            if (__instance != null && controllerOfJob != null)
            {
                if (FarCarOpt.SuspendedCarGUIDToJobChainController.Values.Any(jcc => jcc == controllerOfJob))
                {
                    Main._modEntry.Logger.Error("Can´ t expire a job whose cars are suspended");
                    return false;
                }
            }
            return true;
        }
    }
}
