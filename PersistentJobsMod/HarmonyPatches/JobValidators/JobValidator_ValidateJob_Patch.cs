using DV.Logic.Job;
using DV.Printers;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Jobs;

namespace PersistentJobsMod.HarmonyPatches.JobValidators
{
    public static class JobValidator_ValidateJob_Patch
    {
        [HarmonyPatch(typeof(JobValidator), "ValidateJob")]
        public static bool Prefix(JobValidator __instance, JobBooklet jobBooklet, PrinterController ___bookletPrinter)
        {
            var jobChainController = UnityEngine.Object.FindObjectsOfType<StationController>().FirstOrDefault(st => st.logicStation.availableJobs.Contains(jobBooklet.job)).ProceduralJobsController.GetCurrentJobChains().FirstOrDefault(jcc => jcc.currentJobInChain == jobBooklet.job);
            if (FarCarOpt.SuspendedCarGUIDToJobChainController.ContainsValue(jobChainController ??= new JobChainController(new()))) //the new is just a fallthrough case instead of null
            {
                UnityEngine.Debug.LogWarning("[PersistentJobsMod] The cars for the job are still suspended!");
                if (FarCarOpt.ResumeCars(jobChainController?.carsForJobChain.Select(c => c.carGuid).ToList())) return true;
                __instance.StartCoroutine(JobValidator_ProcessJobOverview_Patch.HandleJobAcceptnceFaliure(___bookletPrinter, false));
                return false;
            }
            else return true;
        }
    }
}
