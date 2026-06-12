using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using PersistentJobsMod.Optimization;

namespace PersistentJobsMod.HarmonyPatches.Pausing
{
    [HarmonyPatch(typeof(LoadingScreenManager))]
    public static class LoadingScreenManager_Patches
    {
        [HarmonyPatch(nameof(LoadingScreenManager.StartLoading))]
        [HarmonyPrefix]
        public static void StartLoadingPatch()
        {
            Main.Pause = true;
        }

        [HarmonyPatch(nameof(LoadingScreenManager.FinishLoading))]
        [HarmonyPrefix]
        public static void FinishLoadingPatch()
        {
            Main.Pause = false;
            FarCarOpt.SuspendIteration = 0;
            FarCarOpt.SuspendCarsCoro();
        }
    }
}
