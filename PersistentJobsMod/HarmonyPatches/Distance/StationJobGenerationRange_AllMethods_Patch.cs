﻿using System;
using System.Reflection;
using HarmonyLib;

namespace PersistentJobsMod.HarmonyPatches.Distance {
    /// <summary>expands the distance at which the job generation trigger is rearmed</summary>
    [HarmonyPatch(typeof(StationJobGenerationRange))]
    [HarmonyPatchAll]
    public static class StationJobGenerationRange_AllMethods_Patch {
        public static void Prefix(StationJobGenerationRange __instance, MethodBase __originalMethod) {
            try {
                // backup existing values before overwriting
                if (Main._initialDistanceRegular < 1f) {
                    Main._initialDistanceRegular = __instance.destroyGeneratedJobsSqrDistanceRegular;
                }
                if (Main._initialDistanceAnyJobTaken < 1f) {
                    Main._initialDistanceAnyJobTaken = __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
                }

                if (Main._modEntry.Active) {
                    if (__instance.destroyGeneratedJobsSqrDistanceAnyJobTaken < 4000000f) {
                        __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = 4000000f;
                    }
                    __instance.destroyGeneratedJobsSqrDistanceRegular = __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken;
                } else {
                    __instance.destroyGeneratedJobsSqrDistanceRegular = Main._initialDistanceRegular;
                    __instance.destroyGeneratedJobsSqrDistanceAnyJobTaken = Main._initialDistanceAnyJobTaken;
                }
            } catch (Exception e) {
                Main.HandleUnhandledException(e, nameof(StationJobGenerationRange_AllMethods_Patch) + "." + nameof(Prefix) + " of " + __originalMethod.Name);
            }
        }
    }
}