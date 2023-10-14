﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CommandTerminal;
using DV.Utils;
using HarmonyLib;
using PersistentJobsMod.Utilities;
using UnityEngine;

namespace PersistentJobsMod.HarmonyPatches.Console {
    [HarmonyPatch]
    public sealed class Console_Patches {
        [HarmonyPatch(typeof(DV.Console), "Dev_TeleportTrainToTrack")]
        [HarmonyPrefix]
        public static bool Dev_TeleportTrainToTrack_Prefix(CommandArg[] args) {
            if (Terminal.IssuedError)
                return false;

            var trackId = args[0].String.ToLower();
            var destinationRailTrack = RailTrackRegistry.Instance.AllTracks.FirstOrDefault(rt => rt.logicTrack.ID.FullDisplayID.ToLower() == trackId);

            if (destinationRailTrack == null) {
                Debug.LogError("Couldn't find railtrack with id " + trackId);
                return false;
            }

            if (PlayerManager.Car == null) {
                Debug.LogError("Player is currently not on any train");
                return false;
            }

            var trainCarsToMove = PlayerManager.Car.trainset.cars.ToList();

            SingletonBehaviour<CoroutineManager>.Instance.Run(MoveCarsCoro(trainCarsToMove, destinationRailTrack));

            return false;
        }

        [HarmonyReversePatch]
        [HarmonyPatch(typeof(DV.Console), nameof(MoveCarsCoro))]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IEnumerator MoveCarsCoro(List<TrainCar> trainCarsToMove, RailTrack destinationRailTrack) {
            throw new NotImplementedException("It's a reverse patch");
        }
    }
}