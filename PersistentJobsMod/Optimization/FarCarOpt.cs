using DV.JObjectExtstensions;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.ModInteraction;
using PersistentJobsMod.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace PersistentJobsMod.Optimization
{
    public static class FarCarOpt
    {
        public static RailTrack[] AllTracks;
        public static int SuspendIteration;

        public static TrainCar CurrentTrainCarToSuspend;
        public static string CurrentCarIDToResume;
        public static bool SuspendCoroRunning;
        public static bool ResumeCoroRunning;
        public static Stopwatch ResumeStopwatch;

        private static (Coroutine, List<StationController>) SuspendCoroutine;
        private static (Coroutine, List<string>, string) ResumeCoroutine;

        private static readonly Queue<(List<string>, string)> PendingResumes = new();

        //key is carGUID (not car ID!), value is the save format for a car
        public static readonly Dictionary<string, JObject> SuspendedCarObjects = [];
        public static readonly Dictionary<string, string> SuspendedCarIDToCarGUID = [];
        public static readonly Dictionary<string, string> SuspendedCarGUIDToCarID = [];
        public static readonly Dictionary<string, JobChainController> SuspendedCarGUIDToJobChainController = [];
        public static readonly Dictionary<string, (DebtTrackerBase, CarDebtData)> SuspendedCarGUIDToDebtTracker = [];
        public static readonly Dictionary<string, List<string>> StationIDtoSuspendedCarGUID = [];

        public static event Action<string> ResumeCompleted;
        public static event Action SuspendCompleted;

        public static void SuspendCar(TrainCar trainCar, JObject carObj = null)
        {
            CurrentTrainCarToSuspend = null;
            try
            {
                if (trainCar is null) return;
                if (!trainCar.isEligibleForSleep) return;
                if (trainCar.logicCar is null) return;
                var st = Stopwatch.StartNew();
                Main.Pause = true;

                if (AllTracks == null || AllTracks.Length == 0) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
                var allTracks = AllTracks;

                PlayerSpawnedCarUtilities.ConvertPlayerSpawnedTrainCar(trainCar);

                CurrentTrainCarToSuspend = trainCar;
                Car logicCar = trainCar.logicCar;
                string carGUID = logicCar.carGuid;
                string carID = logicCar.ID;

                if (SuspendedCarObjects.ContainsKey(carGUID))
                {
                    UnityEngine.Debug.LogError($"[PersistentJobsMod] Car with GUID {carGUID} already suspended!");
                    return;
                }

                CarsSaveManager.SetBrakesOnSpawn(trainCar);

                carObj ??= CarsSaveManager.GetCarSaveData(trainCar, allTracks);

                if ((carObj.GetInt("bog1TrackChildInd").Value == -1) || (carObj.GetInt("bog2TrackChildInd").Value == -1))
                {
                    UnityEngine.Debug.LogError($"[PersistentJobsMod] Car with GUID {carGUID} has invalid track {trainCar.logicCar.CurrentTrack.ID} saved!");
                    return;
                }

                var carJccOrNull = CarTrackAssignment.GetControllerOfCarOrNull(logicCar);
                var yardID = (CarTrackAssignment.FindNearestNamedTrackOrNull([trainCar]))?.ID.yardId;
                DebtTrackerBase tracker = null;
                CarDebtData frozenCarDebtData = null;

                var debtController = trainCar.GetComponent<CarDebtController>();
                if (debtController != null)
                {
                    tracker = debtController.CarDebtTracker;
                    if (tracker != null)
                    {
                        tracker.UpdateDebtValues();
                        frozenCarDebtData = new(tracker.GetDebtData());
                    }
                }

                SuspendedCarObjects.Add(carGUID, carObj);
                SuspendedCarIDToCarGUID.Add(carID, carGUID);
                SuspendedCarGUIDToCarID.Add(carGUID, carID);
                SuspendedCarGUIDToJobChainController.Add(carGUID, carJccOrNull);
                SuspendedCarGUIDToDebtTracker.Add(carGUID, (tracker, CarDebtData.LoadCarDebtFromSaveData(frozenCarDebtData.GetCarDebtSaveData())));

                if (StationIDtoSuspendedCarGUID.TryGetValue(yardID ?? "#Y", out var carGuids)) carGuids.Add(carGUID);
                else StationIDtoSuspendedCarGUID.Add(yardID ?? "#Y", [carGUID]);

                SingletonBehaviour<IdGenerator>.Instance.carGuidToCar.Remove(carGUID);
                SingletonBehaviour<CarSpawner>.Instance.DeleteCar(trainCar);
                SingletonBehaviour<UnusedTrainCarDeleter>.Instance.ClearInvalidCarReferencesAfterManualDelete();

                st.Stop();
                Main._modEntry.Logger.Log($"Suspended car {carID} (carGUID: {carGUID}) in {st.Elapsed}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log($"Problem when suspending car {trainCar.ID}");
                UnityEngine.Debug.LogException(ex);
            }
            finally
            {
                SingletonBehaviour<CoroutineManager>.Instance.Run(AfterSuspend());
            }
        }

        public static bool ResumeCar(string carGUID, out JObject carObject)
        {
            carObject = null;
            CurrentCarIDToResume = null;
            try
            {
                if (carGUID == null) return false;
                SuspendedCarObjects.TryGetValue(carGUID, out var carObj);
                if (carObj is null) return false;
                Stopwatch st = Stopwatch.StartNew();
                Main.Pause = true;

                if (AllTracks == null || AllTracks.Length == 0) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
                var allTracks = AllTracks;

                string oldCarID = SuspendedCarGUIDToCarID[carGUID];
                CurrentCarIDToResume = oldCarID;
                TrainCar trainCar = CarsSaveManager.InstantiateCarFromSavegame(carObj, allTracks);
                Car logicCar = trainCar.logicCar;
                string newCarGUID = logicCar.carGuid;
                if (!(oldCarID == logicCar.ID && carGUID == newCarGUID)) throw new Exception("Restored car doesn´t match");

                CarsSaveManager.SetBrakesOnSpawn(trainCar);
                carObject = carObj;

                SuspendedCarGUIDToJobChainController.TryGetValue(carGUID, out var jcc);

                if (jcc is not null)
                {
                    ReplaceCarInJcc(jcc, oldCarID, logicCar);
                    trainCar.UpdateJobIdOnCarPlates(jcc.currentJobInChain.ID);
                }

                var newDebtController = trainCar.GetComponent<CarDebtController>();
                if (newDebtController != null && SuspendedCarGUIDToDebtTracker.TryGetValue(carGUID, out var tuple))
                {
                    newDebtController.ignoreCarDamageDebt = false;
                    var (oldTracker, frozenCarDebtData) = tuple;
                    var newTracker = newDebtController.CarDebtTracker;
                    if (newTracker != null && oldTracker != null)
                    {
                        TransferDebtValues(oldTracker, newTracker, frozenCarDebtData);
                        ReplaceTrackerInSystems(oldTracker, newTracker);
                    }
                }

                SuspendedCarObjects.Remove(carGUID);
                SuspendedCarIDToCarGUID.Remove(logicCar.ID);
                SuspendedCarGUIDToCarID.Remove(carGUID);
                SuspendedCarGUIDToJobChainController.Remove(carGUID);
                SuspendedCarGUIDToDebtTracker.Remove(carGUID);
                foreach (var cars in StationIDtoSuspendedCarGUID.Values) if (cars.Remove(carGUID)) break;

                CurrentCarIDToResume = null;

                Main.Pause = false;
                st.Stop();
                Main._modEntry.Logger.Log($"Resumed car {logicCar.ID} (carGUID: {newCarGUID}) in {st.Elapsed}");
                return true;
            }
            catch (Exception ex)
            {
                Main._modEntry.Logger.LogException($"Problem when resuming car {carGUID}", ex);
                return false;
            }
            finally
            {
                SingletonBehaviour<CoroutineManager>.Instance.Run(AfterRessume());
            }
        }

        private static void ReplaceCarInJcc(JobChainController jcc, string oldCarID, Car newLogicCar)
        {
            var oldCar = jcc.carsForJobChain.First(c => c.ID == oldCarID);
            jcc.carsForJobChain.Replace(oldCar, newLogicCar);

            foreach (var sjd in jcc.jobChain)
            {
                Job job = sjd.job;

                var jobToCarsDict = SingletonBehaviour<JobsManager>.Instance.jobToJobCars;
                if (jobToCarsDict.TryGetValue(job, out var cars)) jobToCarsDict[job] = (cars?.Replace(oldCar, newLogicCar).ToHashSet());

                //if (job.tasks[0] is not SequentialTasks sequence) continue;
                TaskUtilities.TaskDoLeafDfs(job.tasks[0], task =>
                {
                    var cars = Traverse.Create(task).Field("cars").GetValue<IList<Car>>();
                    if (cars == null) return;
                    cars.Replace(oldCar, newLogicCar);
                });

                switch (sjd)
                {
                    case StaticEmptyHaulJobDefinition ehjd:
                        ehjd.carsToTransport.Replace(oldCar, newLogicCar);
                        break;

                    case StaticTransportJobDefinition tjd:
                        tjd.carsToTransport.Replace(oldCar, newLogicCar);
                        break;

                    case StaticShuntingLoadJobDefinition sljd:
                        sljd.carsPerStartingTrack.ForEach(cpt => cpt.cars.Replace(oldCar, newLogicCar));
                        sljd.loadData.ForEach(ld => ld.cars.Replace(oldCar, newLogicCar));
                        break;

                    case StaticShuntingUnloadJobDefinition sljd:
                        sljd.carsPerDestinationTrack.ForEach(cpt => cpt.cars.Replace(oldCar, newLogicCar));
                        sljd.unloadData.ForEach(ld => ld.cars.Replace(oldCar, newLogicCar));
                        break;

                    default:
                        if (Main.PaxJobsPresent)
                        {
                            if (PaxJobsCompat.IsPaxJobDefinition(sjd, out var phjd))
                            {
                                var jobCars = PaxJobsCompat.GetCarsFromPaxJobDef(phjd);
                                jobCars.Replace(oldCar, newLogicCar);
                                PaxJobsCompat.SetCarsInPaxJobDef(phjd, jobCars);
                                break;
                            }
                        }
                        Main._modEntry.Logger.Warning("Unkown StaticJobDefiniton type encountered, won´t be updated!");
                        break;
                }
            }
        }

        private static void TransferDebtValues(DebtTrackerBase oldTracker, DebtTrackerBase newTracker, CarDebtData frozenCarDebtData)
        {
            CarDebtData oldData = oldTracker.GetDebtData();
            CarDebtData newData = newTracker.GetDebtData();

            if (oldData == null || newData == null || frozenCarDebtData == null) return;

            var oldCarDebtSer = oldData.GetCarDebtSaveData();
            var frozenCarDebtSer = frozenCarDebtData.GetCarDebtSaveData();
            bool changed = !(oldCarDebtSer.ToString() == frozenCarDebtSer.ToString());

            //Main._modEntry.Logger.Log($"old d: ({((oldTracker as SimulatedCarDebtTracker) != null ? "simTracker" : "carTracker")}) \n{oldCarDebtSer} \nfrozen d: {frozenCarDebtSer}");
            //Main._modEntry.Logger.Log($"eaqual: {!changed}");

            var oldComponents = oldData.GetTrackedDebts();
            var newComponents = newData.GetTrackedDebts();

            if (oldComponents != null && newComponents != null)
            {
                foreach (var oldComp in oldComponents)
                {
                    var oldType = oldComp.Type;

                    foreach (var newComp in newComponents)
                    {
                        if ((newComp.Type).Equals(oldType))
                        {
                            newComp.type = oldComp.type;
                            newComp.startValue = oldComp.startValue;
                            newComp.snapshotValue = oldComp.snapshotValue;
                            newComp.endValue = oldComp.endValue;
                            break;
                        }
                    }
                }
                //Main._modEntry.Logger.Log($"After replace d: ({((oldTracker as SimulatedCarDebtTracker) != null ? "simTracker" : "carTracker")}) \n{newTracker.GetDebtData().GetCarDebtSaveData()}");
            }
        }

        private static void ReplaceTrackerInSystems(DebtTrackerBase oldTracker, DebtTrackerBase newTracker)
        {
            var trackedJobs = JobDebtController.Instance.existingTrackedJobs;
            if (trackedJobs != null)
            {
                foreach (var existingJobDebt in trackedJobs)
                {
                    var carsDebtTrackers = existingJobDebt.jobDebtTracker.carsDebtTrackers;
                    if (carsDebtTrackers != null)
                    {
                        for (int i = 0; i < carsDebtTrackers.Length; i++)
                        {
                            if (carsDebtTrackers.GetValue(i) == oldTracker) carsDebtTrackers.SetValue(newTracker, i);
                        }
                    }
                }
            }

            var joblessTrackers = Traverse.Create(JobDebtController.Instance.existingJoblessCarDebts).Field("joblessCarsTrackers").GetValue<System.Collections.IList>();
            if (joblessTrackers != null)
            {
                for (int i = 0; i < joblessTrackers.Count; i++)
                {
                    if (joblessTrackers[i] == oldTracker)
                    {
                        joblessTrackers[i] = newTracker;
                    }
                }
            }

            var trackedLocos = LocoDebtController.Instance.trackedLocosDebts;
            if (trackedLocos != null)
            {
                foreach (var locoDebt in trackedLocos) if (locoDebt.locoDebtTracker == oldTracker) Traverse.Create(locoDebt).Field("locoDebtTracker").SetValue(newTracker as LocoDebtTrackerBase);
            }

            var ownedCarStates = OwnedCarsStateController.Instance.existingOwnedCarStates;
            if (ownedCarStates != null)
            {
                foreach (var ownedCarDebt in ownedCarStates) if (ownedCarDebt.carDebtTrackerBase == oldTracker) Traverse.Create(ownedCarDebt).Field("carDebtTrackerBase").SetValue(newTracker);
            }
        }

        public static IEnumerator SuspendCars(List<TrainCar> trainCars)
        {
            if (trainCars is null || !trainCars.Any()) yield break;
            if (!WorldStreamingInit.IsLoaded) yield break;

            var st = Stopwatch.StartNew();
            var fst = Stopwatch.StartNew();

            if (AllTracks == null || AllTracks.Length == 0) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
            var viableTrainCars = (trainCars.Where(tc => !(tc is null || tc.uniqueCar || tc.IsLoco || tc.IsCaboose || tc.preventDelete || tc.logicCar is null))).ToList();
            var trainCarObjects = viableTrainCars.Select(tc => CarsSaveManager.GetCarSaveData(tc, AllTracks)).ToList();

            for (int i = 0; i < viableTrainCars.Count; i++)
            {
                SuspendCar(viableTrainCars[i], trainCarObjects[i]);

                if (fst.ElapsedMilliseconds > 10)
                {
                    yield return null;
                    fst.Restart();
                }
            }

            SingletonBehaviour<UnusedTrainCarDeleter>.Instance.ClearInvalidCarReferencesAfterManualDelete();
            st.Stop();
            Main._modEntry.Logger.Log($"Suspending {viableTrainCars.Count} cars took {st.Elapsed}");
            yield break;
        }

        public static bool RunSuspendCars(bool immediately = false, List<StationController> where = null)
        {
            SuspendIteration++;
            if (!immediately && (SuspendIteration % 2 > 0)) return false;
            if (!Main.Settings.SuspendFarAwayCars) return false;

            if (AllTracks == null || AllTracks.Length == 0 || AllTracks.Any(rt => rt is null)) AllTracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
            where ??= StationController.allStations.Where(sc => !sc.stationRange.IsPlayerInJobGenerationZone(sc.stationRange.PlayerSqrDistanceFromStationCenter * 2)).ToList();
            if (where.Any(sc => sc?.gameObject == null)) return false;

            if (SuspendCoroRunning && SuspendCoroutine.Item1 != null)
            {
                if (where.All(sc => SuspendCoroutine.Item2.Contains(sc))) return false;
                Main._modEntry.Logger.Error($"SuspendCarsCoro already running on {string.Join(", ", SuspendCoroutine.Item2.Select(sc => sc.stationInfo.YardID))} stopped for another is launching");
                SingletonBehaviour<CoroutineManager>.Instance.Stop(SuspendCoroutine.Item1);
            }

            SuspendCoroRunning = true;
            SuspendCoroutine = (SingletonBehaviour<CoroutineManager>.Instance.Run(new ExceptionCatchingCoroutineIterator(SuspendCarsCoro(where), nameof(FarCarOpt) + "." + nameof(SuspendCarsCoro), new StackTrace(true))), where);

            return true;
        }

        private static IEnumerator<(string NextStageName, object Result)> SuspendCarsCoro(IEnumerable<StationController> viableSCs)
        {
            Main._modEntry.Logger.Log(nameof(SuspendCarsCoro));
            while (!WorldStreamingInit.IsLoaded) yield return ("waiting for WorldStreamingInit.IsLoaded", null);
            while (ResumeCoroRunning) yield return ("waiting for car resuming to finish", null);
            yield return ("safety wait", WaitFor.SecondsRealtime(0.5f));

            var st = Stopwatch.StartNew();
            var fst = Stopwatch.StartNew();
            var cars = new HashSet<TrainCar>();
            try
            {
                foreach (var sc in viableSCs)
                {
                    var scst = Stopwatch.StartNew();
                    Main._modEntry.Logger.Log("Suspending cars in " + sc.stationInfo.YardID);
                    var stationTracks = sc.ExtractLogicTracks(sc.AllStationTracks);
                    if (Main.PaxJobsPresent && PaxJobsCompat.IsPassengerStation(sc.stationInfo.YardID)) stationTracks.AddRange(PaxJobsCompat.AllPaxTracksForStationData(sc.stationInfo.YardID));

                    foreach (var track in stationTracks)
                    {
                        var fully = track.GetCarsFullyOnTrack();
                        var partially = track.GetCarsPartiallyOnTrack();
                        if (fully != null && fully.Any())
                        {
                            cars.UnionWith(fully.Select(c => c.TrainCar()));
                            cars.UnionWith(fully.FirstOrDefault()?.TrainCar().trainset.cars);
                            cars.UnionWith(fully.LastOrDefault()?.TrainCar().trainset.cars);
                        }
                        if (partially != null && partially.Any())
                        {
                            cars.UnionWith(partially.Select(c => c.TrainCar()));
                            cars.UnionWith(partially.FirstOrDefault()?.TrainCar().trainset.cars);
                            cars.UnionWith(partially.LastOrDefault()?.TrainCar().trainset.cars);
                        }
                    }
                    cars.RemoveWhere(c => c.logicCar?.ID is null);
                    scst.Stop();
                    Main._modEntry.Logger.Log($"Gather took {scst.Elapsed}");

                    if (fst.ElapsedMilliseconds > 3)
                    {
                        yield return ("frame time elapsed", null);
                        fst.Restart();
                    }

                    if (cars.Any()) yield return ("actually suspending cars", SuspendCars([.. cars]));
                    cars.Clear();
                    yield return ("done - resetting", null);
                }
            }
            finally
            {
                SuspendCoroRunning = false;
                SuspendCompleted?.Invoke();
                st.Stop();
                Main._modEntry.Logger.Log($"SuspendCarsCoro took {st.Elapsed} to run");
            }

            yield break;
        }

        public static bool ResumeCarsInStation(string stationID)
        {
            if (stationID is null || stationID == string.Empty) return false;
            if (!WorldStreamingInit.IsLoaded) return false;

            stationID = stationID.Trim().ToUpper();

            UnityEngine.Debug.Log("[PersistentJobsMod] Resuming cars in " + stationID);

            StationIDtoSuspendedCarGUID.TryGetValue(stationID, out var carGUIDS);
            if (stationID is "ALL" or "*") carGUIDS = StationIDtoSuspendedCarGUID.Values.SelectMany(x => x).ToList();
            if (carGUIDS is null || !carGUIDS.Any()) return false;

            //SingletonBehaviour<CoroutineManager>.Instance.Run(CarsSaveManager.IgnoreTrainStressForLoadedCarsUntilCouplingIsSettled());
            if (!RunResumeCars(carGUIDS.ToList(), stationID))
            {
                UnityEngine.Debug.LogWarning($"Car resuming in {stationID} failed");
                if (Debugger.IsAttached) Debugger.Break();
                return false;
            }
            return true;
        }

        public static bool RunResumeCars(List<string> guids, string location)
        {
            if (ResumeCoroRunning)
            {
                PendingResumes.Enqueue((guids, location));
                return false;
            }

            ResumeCoroRunning = true;
            ResumeStopwatch = Stopwatch.StartNew();
            ResumeCoroutine = (SingletonBehaviour<CoroutineManager>.Instance.Run(new ExceptionCatchingCoroutineIterator(ResumeCarsCoro(guids, location), nameof(FarCarOpt) + "." + nameof(ResumeCarsCoro), new StackTrace(true))), guids, location);

            return true;
        }

        private static IEnumerator<(string NextStageName, object Result)> ResumeCarsCoro(List<string> guids, string location)
        {
            List<JObject> succesfullCars = [];
            if (guids is not null && guids.Any())
            {
                var fst = Stopwatch.StartNew();
                TrainStress.globalIgnoreStressCalculation = true;
                try
                {
                    foreach (var guid in guids)
                    {
                        if (ResumeStopwatch.Elapsed.TotalMinutes > 4) throw new TimeoutException($"{nameof(ResumeCarsCoro)} has ran for too long!");

                        if (!ResumeCar(guid, out JObject carData))
                        {
                            TrainStress.globalIgnoreStressCalculation = false;
                            throw new Exception("Failed to resume car with guid " + guid);
                        }
                        else succesfullCars.Add(carData);

                        if (fst.ElapsedMilliseconds > 10)
                        {
                            yield return ("frame time elapsed", null);
                            fst.Restart();
                        }
                    }

                    foreach (var carData in succesfullCars) CarsSaveManager.RestoreCarConnections(carData);

                    UnityEngine.Debug.Log($"[PersistentJobsMod] Successfully resumed {succesfullCars.Count} cars {(location.Length > 0 ? ("in " + location) : "")} in {ResumeStopwatch.Elapsed}");
                    ResumeStopwatch.Reset();
                }
                finally
                {
                    TrainStress.globalIgnoreStressCalculation = false;
                    ResumeStopwatch.Stop();
                    ResumeCoroRunning = false;
                    ResumeCompleted?.Invoke(location);

                    if (PendingResumes.Count > 0)
                    {
                        var next = PendingResumes.Dequeue();
                        RunResumeCars(next.Item1, next.Item2);
                    }
                }
            }
            yield break;
        }

        public static IEnumerator AfterSuspend()
        {
            yield return WaitFor.SecondsRealtime(0.005f);
            CurrentTrainCarToSuspend = null;
            Main.Pause = false;
            yield break;
        }

        public static IEnumerator AfterRessume()
        {
            yield return WaitFor.SecondsRealtime(0.005f);
            CurrentCarIDToResume = null;
            Main.Pause = false;
            yield break;
        }

        public static void ClearRecords()
        {
            SuspendedCarObjects.Clear();
            SuspendedCarIDToCarGUID.Clear();
            SuspendedCarGUIDToCarID.Clear();
            SuspendedCarGUIDToJobChainController.Clear();
            SuspendedCarGUIDToDebtTracker.Clear();
            StationIDtoSuspendedCarGUID.Clear();
            SuspendIteration = 0;
            AllTracks = null;
        }
    }
}