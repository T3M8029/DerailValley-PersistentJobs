using DV.Logic.Job;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.Optimization;
using System.Linq;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers
{
    [HarmonyPatch]
    public static class JobsManager_Patches
    {
        [HarmonyPatch(typeof(JobsManager), nameof(JobsManager.AbandonJob))]
        [HarmonyPrefix]
        public static bool Prefix(Job job)
        {
            if (FarCarOpt.SuspendedCarGUIDToJobChainController.Values.WhereNotNull().Any(jcc => jcc.jobChain.Any(sjd => sjd.job == job)))
            {
                Main._modEntry.Logger.Error("Can´ t abandon a job whose cars are suspended");
                return false;
            }
            else return true;
        }
    }
}
