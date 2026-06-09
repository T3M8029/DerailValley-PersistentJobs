using DV.Damage;
using DV.ServicePenalty;
using HarmonyLib;
using PersistentJobsMod.Optimization;
using System.Linq;

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
            if (FarCarOpt.SuspendedCarGUIDToDebtTracker.Values.Any(t => t.Item1 == __instance as DebtTrackerBase)) return false;
            else return true;
        }
    }

    [HarmonyPatch(typeof(CarDebtController), nameof(CarDebtController.SetDebtTracker))]
    public static class CarDebtController_SetDebtTracker_Patch
    {
        public static bool Prefix(CarDamageModel carDmg, CargoDamageModel cargoDmg, CarDebtController __instance)
        {
            Main._modEntry.Logger.Log("CarDebtController_SetDebtTracker_Patch");
            return Foo(carDmg, cargoDmg, __instance);
        }

        public static bool Foo(CarDamageModel carDmg, CargoDamageModel cargoDmg, CarDebtController __instance)
        {
            if (__instance.ignoreCarDamageDebt && !__instance.trainCar.IsLoco)
            {
                if (carDmg != null || cargoDmg != null)
                {
                    Main._modEntry.Logger.Log($"Setting functional debt tracker on {__instance.trainCar.ID}");
                    Traverse.Create(__instance).Property("CarDebtTracker").SetValue(new DebtTrackerCar(carDmg, cargoDmg, __instance.trainCar.ID, __instance.trainCar.carType));
                }
                else
                {
                    Main._modEntry.Logger.Error("Car is missing both damage components: CarDamageModel or CargoDamageModel. CarDebtTracker will be set to null!");
                    return true;
                }
                return false;
            }
            else return true;
        }
    }
}
