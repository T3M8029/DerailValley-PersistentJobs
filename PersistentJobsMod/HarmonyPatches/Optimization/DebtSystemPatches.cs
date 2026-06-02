using DV.ServicePenalty;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistentJobsMod.HarmonyPatches.Optimization
{
    [HarmonyPatch(typeof(JobDebtController), nameof(JobDebtController.StageJoblessCarDebtOnCarDestroy))]
    public static class JobDebtController_StageJoblessCarDebtOnCarDestroy_Patch
    {
        public static bool Prefix()
        {
            if (FarCarOpt.CurrentTrainCarToSuspend != null) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(LocoDebtController), nameof(LocoDebtController.StageLocoDebtOnLocoDestroy))]
    public static class LocoDebtController_StageLocoDebtOnLocoDestroy_Patch
    {
        public static bool Prefix()
        {
            if (FarCarOpt.CurrentTrainCarToSuspend != null) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(OwnedCarsStateController), nameof(OwnedCarsStateController.StageCarStateTrackerOnDestroy))]
    public static class OwnedCarsStateController_StageCarStateTrackerOnDestroy_Patch
    {
        public static bool Prefix()
        {
            if (FarCarOpt.CurrentTrainCarToSuspend != null) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(DebtTrackerCar), nameof(DebtTrackerCar.UpdateDebtValues))]
    public static class DebtTrackerCar_UpdateDebtValues_Patch
    {
        public static bool Prefix(DebtTrackerCar __instance)
        {
            if (FarCarOpt.SuspendedCarGUIDToDebtTracker.ContainsValue(__instance as DebtTrackerBase)) return false;
            else return true;
        }
    }
}
