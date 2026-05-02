using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.JobChainControllers
{
    [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.AddJobDefinitionToChain))]
    public static class JobChainController_AddJobDefinitionToChain_Patch
    {
        //the dict will get populated only when the reservation actually gets made, for now remove this "promise"
        public static void Postfix(StaticJobDefinition jobInChain, JobChainController __instance)
        {
            __instance.jobDefToCurrentlyReservedTracks[jobInChain] = new();
        }
    }
}
