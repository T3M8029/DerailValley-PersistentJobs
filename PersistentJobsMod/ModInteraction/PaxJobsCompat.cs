using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using PersistentJobsMod.Extensions;
using PersistentJobsMod.HarmonyPatches.JobGeneration;
using PersistentJobsMod.JobGenerators;
using PersistentJobsMod.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static PersistentJobsMod.HarmonyPatches.JobGeneration.UnusedTrainCarDeleter_Patches;
using Random = System.Random;

namespace PersistentJobsMod.ModInteraction
{
    public static class PaxJobsCompat
    {
        private static Assembly asm;

        private static Type _RouteManager;
        private static Type _ConsistManger;
        private static Type _RouteTrack;
        private static Type _PassConsistInfo;
        private static Type _PassengerJobGenerator;
        private static Type _IPassDestination;

        private static ConstructorInfo _RouteTrackCtor;
        private static ConstructorInfo _PassConsistInfoCtor;

        private static MethodInfo TryGetInstance;
        private static MethodInfo GenerateJob;

        private static Random _Random;

        public static bool Initialize()
        {
            try
            {
                asm = Main.PaxJobs.Assembly;

                _RouteManager = CompatAccess.Type("PassengerJobs.Generation.RouteManager");
                _ConsistManger = CompatAccess.Type("PassengerJobs.Generation.ConsistManager");
                _RouteTrack = CompatAccess.Type("PassengerJobs.Generation.RouteTrack");
                _PassConsistInfo = CompatAccess.Type("PassengerJobs.Generation.PassConsistInfo");
                _PassengerJobGenerator = CompatAccess.Type("PassengerJobs.Generation.PassengerJobGenerator");
                _IPassDestination = CompatAccess.Type("PassengerJobs.Generation.IPassDestination");

                TryGetInstance = CompatAccess.Method(_PassengerJobGenerator, "TryGetInstance");
                GenerateJob = CompatAccess.Method(_PassengerJobGenerator, "GenerateJob", new[] { typeof(JobType), _PassConsistInfo });

                _Random = new Random();
            }
            catch (Exception e)
            {
                Main._modEntry.Logger.LogException("Failed to initilize PaxJobsCompat when resolving types and methods", e);
                return false;
            }
            return true;
        }


        internal static class CompatAccess
        {
            public static Type Type(string fullName) => AccessTools.TypeByName(fullName) ?? throw new TypeLoadException($"Type not found: {fullName}");

            public static MethodInfo Method(Type type, string name, Type[] args = null)
            {
                var mi = args == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args);
                return mi ?? throw new MissingMethodException(type.FullName, name);
            }

            public static ConstructorInfo Ctor(Type type, Type[] args) => AccessTools.Constructor(type, args) ?? throw new MissingMethodException(type.FullName, ".ctor");

            public static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name);

            public static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        }

        public static bool TryGetGenerator(string yardId, out object generator)
        {
            generator = null;
            var args = new object[] { yardId, null };
            if (!(bool)TryGetInstance.Invoke(null, args))
            {
                Main._modEntry.Logger.Error($"Couldn´t get instance of PaxJobsGenerator for {yardId}");
                return false;
            }

            generator = args[1];
            return generator != null;
        }

        public static bool TryGenerateJob(string yardId, JobType jobType, object passConsistInfo, out object passengerChainController)
        {
            Main._modEntry.Logger.Log($"[TryGenerateJob] Attempting to generate job of type {jobType} in {yardId}");
            passengerChainController = null;
            if (!TryGetGenerator(yardId, out object generator))
            {
                Main._modEntry.Logger.Error($"PaxJobsGenerator for {yardId} was null, this shouldn´t happen!");
                return false;
            }

            passengerChainController = GenerateJob.Invoke(generator, new object[] { jobType, passConsistInfo });
            if (passengerChainController == null) Main._modEntry.Logger.Error("Couldn´t generate PaxJob - null from there");
            return passengerChainController != null;
        }

        public static object CreateRouteTrack(object IPassDestination, Track terminalTrack)
        {
            _RouteTrackCtor = CompatAccess.Ctor(_RouteTrack, new[] { _IPassDestination, typeof(Track) });
            return _RouteTrackCtor.Invoke(new object[] { IPassDestination, terminalTrack });
        }

        public static object CreatePassConsistInfo(object routeTrack, List<Car> cars)
        {
            _PassConsistInfoCtor = CompatAccess.Ctor(_PassConsistInfo, new[] { _RouteTrack, typeof(List<Car>) });
            return _PassConsistInfoCtor.Invoke(new object[] { routeTrack, cars });
        }

        public static bool IsPaxCars(TrainCar car)
        {
            var GetPassengerCars = AccessTools.Method(_ConsistManger, "GetPassengerCars");
            IEnumerable<TrainCarLivery> carLiveries = (IEnumerable<TrainCarLivery>)(GetPassengerCars?.Invoke(null, null));
            return (carLiveries != null && car.carLivery != null) && carLiveries.Contains(car.carLivery);
        }

        public static bool IsPassengerStation(string yardId)
        {
            var IsPassengerStation = AccessTools.Method(_RouteManager, "IsPassengerStation");
            return (bool)(IsPassengerStation?.Invoke(null, new object[] { yardId }));
        }

        public static List<StationController> AllPaxStations() => StationController.allStations.Where(st => IsPassengerStation(st.stationInfo.YardID)).ToList();

        public static object GetStationData(string yardId)
        {
            var GetStationData = AccessTools.Method(_RouteManager, "GetStationData");
            return /*IPassDestination : PassStationData*/GetStationData?.Invoke(null, new object[] { yardId });
        }

        public static List<Track> AllPaxTracksForStationData(string yardId)
        {
            PropertyInfo _allTracksProperty = CompatAccess.Property(_IPassDestination, "AllTracks");
            return ((IEnumerable<Track>)_allTracksProperty.GetValue(GetStationData(yardId))).ToList();
        }

        public static IEnumerable<object> GetPlatforms(object stationData, bool onlyTerminusTracks = false)
        {
            var _getPlatformsMethod = AccessTools.Method(_IPassDestination, "GetPlatforms", new[] { typeof(bool) });
            var result = _getPlatformsMethod.Invoke(stationData, new object[] { onlyTerminusTracks });

            // "Cast to non-generic IEnumerable, which works for both struct and class collections" <-- AI´s fix of one runtime crash, I don´t understand this fully...
            if (result is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    yield return item;
                }
            }
        }

        public static Track GetRouteTractTrackField(object routeTrack)
        {
            FieldInfo RouteTrackTrackField = _RouteTrack.GetField("Track", BindingFlags.Public | BindingFlags.Instance);
            return (Track)RouteTrackTrackField.GetValue(routeTrack);
        }

        public static double GetRouteTrackLength(object routeTrack)
        {
            PropertyInfo lengthProp = CompatAccess.Property(_RouteTrack, "Length");
            return (double)lengthProp.GetValue(routeTrack);
        }

        public static bool CanFitInPaxStation(StationController paxStation, List<TrainCar> trainCars)
        {
            var stationData = GetStationData(paxStation.stationInfo.YardID);
            var platforms = GetPlatforms(stationData).ToList();
            if (platforms == null || !platforms.Any()) return false;
            float carsLength = CarSpawner.Instance.GetTotalCarsLength(TrainCar.ExtractLogicCars(trainCars), true);
            return platforms.Any(p => carsLength < GetRouteTrackLength(p));
        }

        public static (List<IReadOnlyList<TrainCar>>, List<IReadOnlyList<TrainCar>>) SortCGIntoEmptyAndLoaded(List<IReadOnlyList<TrainCar>> paxConsecutiveTrainCarGroups)
        {
            var statusTrainCarGroups = paxConsecutiveTrainCarGroups.SelectMany(cars => cars.GroupConsecutiveBy(tc => UnusedTrainCarDeleter_Patches.GetTrainCarReassignStatus(tc))).ToList();
            var emptyConsecutiveTrainCarGroups = statusTrainCarGroups.Where(s => s.Key == TrainCarReassignStatus.Empty).Select(s => s.Items).ToList();
            var loadedConsecutiveTrainCarGroups = statusTrainCarGroups.Where(s => s.Key == TrainCarReassignStatus.Loaded).Select(s => s.Items).ToList();

            Main._modEntry.Logger.Log($"Found {emptyConsecutiveTrainCarGroups.Count} empty pax train car groups with a total of {emptyConsecutiveTrainCarGroups.SelectMany(g => g).Count()} cars");
            Main._modEntry.Logger.Log($"Found {loadedConsecutiveTrainCarGroups.Count} loaded pax train car groups with a total of {loadedConsecutiveTrainCarGroups.SelectMany(g => g).Count()} cars");
            return (emptyConsecutiveTrainCarGroups, loadedConsecutiveTrainCarGroups);
        }

        public static void DecideForPaxCarGroups(List<IReadOnlyList<TrainCar>> paxConsecutiveTrainCarGroups, StationController station)
        {
            Main._modEntry.Logger.Log($"Reassigning passanger cars to jobs in station {station.logicStation.ID}");

            foreach (var trainCar in paxConsecutiveTrainCarGroups.SelectMany(tcg => tcg))
            {
                PlayerSpawnedCarUtilities.ConvertPlayerSpawnedTrainCar(trainCar);
            }

            var (emptyConsecutiveTrainCarGroups, loadedConsecutiveTrainCarGroups) = SortCGIntoEmptyAndLoaded(paxConsecutiveTrainCarGroups);
            foreach (List<TrainCar> tcs in emptyConsecutiveTrainCarGroups.Cast<List<TrainCar>>())
            {
                HandleEmptyPaxCars(tcs, station);
            }
            foreach (List<TrainCar> tcs in loadedConsecutiveTrainCarGroups.Cast<List<TrainCar>>())
            {
                Main._modEntry.Logger.Log($"Loaded consist of {tcs.Count()} pax cars starting with {tcs.First().ID} is in station {station.stationInfo.Name}");
                //handling complicated - no inbuilt methods for job generation from already loaded cars
            }
        }

        public static (List<T> first, List<T> second) SplitInHalf<T>(IList<T> source)
        {
            if (source.Count() < 2) return (null, null);
            int mid = (source.Count + 1) / 2;
            var first = source.Take(mid).ToList();
            var second = source.Skip(mid).ToList();
            return (first, second);
        }

        public static void HandleEmptyPaxCars(List<TrainCar> trainCars, StationController station)
        {
            if (trainCars == null || trainCars.Count() < 1)
            {
                Main._modEntry.Logger.Error("[HandleEmptyPaxCars] Invalid trainCars input");
                return;
            }

            var startingTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
            if (startingTrack == null) return;

            if (IsPassengerStation(station.stationInfo.YardID))
            {
                //generate PaxJob: need to create PassConsistInfo from RouteTrack
                Main._modEntry.Logger.Log($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on track {startingTrack.ID.FullID} ready for pax job reassignment");

                var stationData = GetStationData(station.stationInfo.YardID);
                List<Track> platformTracks = GetPlatforms(stationData).Select(GetRouteTractTrackField).ToList();
                if (platformTracks == null || !platformTracks.Any())
                {
                    Main._modEntry.Logger.Error($"[HandleEmptyPaxCars] Station {station.stationInfo.YardID} is marked as passanger but has no accesable platforms, this should not happen!");
                    return;
                }

                bool canFitSomewhere = CanFitInPaxStation(station, trainCars);
                JobType jobType = 0;
                if (!canFitSomewhere && trainCars.Count >= 2)
                {
                    Main._modEntry.Logger.Log($"Consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} can´t fit into any platform in {station.stationInfo.YardID}, attempting to split");
                    var (first, second) = SplitInHalf(trainCars);
                    HandleEmptyPaxCars(first, station);
                    HandleEmptyPaxCars(second, station);
                    return;
                }
                else if (!canFitSomewhere && trainCars.Count < 2)
                {
                    Main._modEntry.Logger.Error($"Consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} can´t be assigned to any platform in {station.stationInfo.YardID}, this shouldn´t happen! (You should check station data");
                    return;
                }
                else if (canFitSomewhere && trainCars.Count <= 4)
                {
                    jobType = (JobType)_Random.Next(101, 103);
                }
                else if (canFitSomewhere && trainCars.Count() > 4)
                {
                    jobType = (JobType)101;
                }
                Main._modEntry.Logger.Log($"Picked JobType is {jobType}");

                if (jobType != 0 && platformTracks.Contains(startingTrack) && (CarSpawner.Instance.GetTotalCarsLength(TrainCar.ExtractLogicCars(trainCars), true) < startingTrack.length))
                {
                    var consistInfo = CreatePassConsistInfo(CreateRouteTrack(stationData, startingTrack), TrainCar.ExtractLogicCars(trainCars));
                    var haveChainController = TryGenerateJob(station.stationInfo.YardID, jobType, consistInfo, out object passengerChainController);
                    if (haveChainController)
                    {
                        Main._modEntry.Logger.Log($"Succesfully reassigned consist of pax cars starting with {trainCars.First().ID} to a job {null /*need to get job info from generated chain controller*/}");
                    }
                    else Main._modEntry.Logger.Log($"Could not reassign consist of pax cars starting with {trainCars.First().ID} to a job");
                    return;
                }
                else
                {
                    if ((int)jobType != 101 && (int)jobType != 102)
                    {
                        Main._modEntry.Logger.Error($"Picked JobType ({jobType}) is currently not valid, this should not happen!");
                        return;
                    }
                    else
                    {
                        Main._modEntry.Logger.Log($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on (non-platform ? ) track {startingTrack.ID.FullID} need handling");
                        //not an ideal solution, setting starting track to one of the platforms regardless of trainCars positions
                        var alternateStartingTrack = (GetPlatforms(stationData).Select(GetRouteTractTrackField).ToList()).Where(t => t.length < CarSpawner.Instance.GetTotalCarsLength(TrainCar.ExtractLogicCars(trainCars), true)).ToList().GetRandomElement();
                        var consistInfo = CreatePassConsistInfo(CreateRouteTrack(stationData, alternateStartingTrack), TrainCar.ExtractLogicCars(trainCars));
                        var haveChainController = TryGenerateJob(station.stationInfo.YardID, jobType, consistInfo, out object passengerChainController);
                        if (haveChainController)
                        {
                            Main._modEntry.Logger.Log($"Succesfully reassigned consist of pax cars starting with {trainCars.First().ID} to a job {null /*need to get job info from generated chain controller*/}");
                        }
                        else Main._modEntry.Logger.Log($"Could not reassign consist of pax cars starting with {trainCars.First().ID} to a job");
                        return;
                    }
                }
            }
            else
            {
                Main._modEntry.Logger.Log($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on track {startingTrack.ID.FullID} needs to be transported to a pax jobs station to get reassigned");
                //generate LH job to random pax station: use already existing mod logic elswhere -- potentially chnge to use SP tracks which PaxJobs currently doesn´t - waiting for update
                StationController viableDestStation = AllPaxStations().Distinct().Where(st => st != station).Where(st => CanFitInPaxStation(st, trainCars)).OrderBy(_ => _Random.Next()).FirstOrDefault();
                if (viableDestStation != null)
                {
                    var jobChainController = EmptyHaulJobGenerator.GenerateEmptyHaulJobWithExistingCarsOrNull(station, viableDestStation, startingTrack, trainCars, _Random);
                    if (jobChainController != null)
                    {
                        FinalizeJobChainControllerAndGenerateFirstJob(jobChainController);
                    }
                }
                else
                {
                    Main._modEntry.Logger.Error($"Empty onsist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} can´t be reassigned a LH to any pax station, attempting splitting");
                    var (first, second) = SplitInHalf(trainCars);
                    HandleEmptyPaxCars(first, station);
                    HandleEmptyPaxCars(second, station);
                    return;
                }
            }

            Main._modEntry.Logger.Error("[HandleEmptyPaxCars] End of function reached possibly without reassigning, this shouldn´t happen!");
        }
    }
}