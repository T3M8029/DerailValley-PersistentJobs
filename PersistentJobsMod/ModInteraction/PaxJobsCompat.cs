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
        private static Type _ConsistManager;
        private static Type _RouteTrack;
        private static Type _PassConsistInfo;
        private static Type _PassengerJobGenerator;
        private static Type _IPassDestination;

        private static ConstructorInfo _RouteTrackCtor;
        private static ConstructorInfo _PassConsistInfoCtor;
        private static MethodInfo _TryGetInstance;
        private static MethodInfo _GenerateJob;
        private static MethodInfo _GetPassengerCars;
        private static MethodInfo _IsPassengerStation;
        private static MethodInfo _GetStationData;
        private static MethodInfo _GetRouteTrackById;
        private static MethodInfo _GetPlatforms;

        private static PropertyInfo _AllTracksProperty;
        private static PropertyInfo _RouteTrackLengthProp;
        private static FieldInfo _RouteTrackTrackField;

        private static Random _Random;

        private const JobType PassengerExpress = (JobType)101;
        private const JobType PassengerLocal = (JobType)102;

        public static bool Initialize()
        {
            try
            {
                asm = Main.PaxJobs.Assembly;

                _RouteManager = CompatAccess.Type("PassengerJobs.Generation.RouteManager");
                _ConsistManager = CompatAccess.Type("PassengerJobs.Generation.ConsistManager");
                _RouteTrack = CompatAccess.Type("PassengerJobs.Generation.RouteTrack");
                _PassConsistInfo = CompatAccess.Type("PassengerJobs.Generation.PassConsistInfo");
                _PassengerJobGenerator = CompatAccess.Type("PassengerJobs.Generation.PassengerJobGenerator");
                _IPassDestination = CompatAccess.Type("PassengerJobs.Generation.IPassDestination");

                _TryGetInstance = CompatAccess.Method(_PassengerJobGenerator, "TryGetInstance");
                _GenerateJob = CompatAccess.Method(_PassengerJobGenerator, "GenerateJob", new[] { typeof(JobType), _PassConsistInfo });
                _GetPassengerCars = CompatAccess.Method(_ConsistManager, "GetPassengerCars");
                _IsPassengerStation = CompatAccess.Method(_RouteManager, "IsPassengerStation");
                _GetStationData = CompatAccess.Method(_RouteManager, "GetStationData");
                _GetRouteTrackById = CompatAccess.Method(_RouteManager, "GetRouteTrackById");
                _GetPlatforms = CompatAccess.Method(_IPassDestination, "GetPlatforms", new[] { typeof(bool) });

                _AllTracksProperty = CompatAccess.Property(_IPassDestination, "AllTracks");
                _RouteTrackLengthProp = CompatAccess.Property(_RouteTrack, "Length");
                _RouteTrackTrackField = CompatAccess.Field(_RouteTrack, "Track");

                _RouteTrackCtor = CompatAccess.Ctor(_RouteTrack, new[] { _IPassDestination, typeof(Track) });
                _PassConsistInfoCtor = CompatAccess.Ctor(_PassConsistInfo, new[] { _RouteTrack, typeof(List<Car>) });

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
            public static MethodInfo Method(Type type, string name, Type[] args = null) => (args == null ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args)) ?? throw new MissingMethodException(type.FullName, name);
            public static ConstructorInfo Ctor(Type type, Type[] args) => AccessTools.Constructor(type, args) ?? throw new MissingMethodException(type.FullName, ".ctor");
            public static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name) ?? throw new MissingMemberException(type.FullName, name);
            public static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        }

        public static bool TryGetGenerator(string yardId, out object generator)
        {
            generator = null;
            var args = new object[] { yardId, null };
            if (!(bool)_TryGetInstance.Invoke(null, args))
            {
                Main._modEntry.Logger.Error($"Couldn´t get instance of PaxJobsGenerator for {yardId}");
                return false;
            }

            generator = args[1];
            return generator != null;
        }

        public static bool TryGenerateJob(string yardId, JobType jobType, object passConsistInfo, out JobChainController passengerChainController)
        {
            passengerChainController = null;
            if (!AStartGameData.carsAndJobsLoadingFinished) return false;
            Main._modEntry.Logger.Log($"[TryGenerateJob] Attempting to generate job of type {jobType} in {yardId}");

            if (!TryGetGenerator(yardId, out object generator))
            {
                Main._modEntry.Logger.Error($"PaxJobsGenerator for {yardId} was null, this shouldn´t happen!");
                return false;
            }

            passengerChainController = (JobChainController)_GenerateJob.Invoke(generator, new object[] { jobType, passConsistInfo });
            if (passengerChainController == null) Main._modEntry.Logger.Error("Couldn´t generate PaxJob - null from there");
            return passengerChainController != null;
        }

        public static object CreateRouteTrack(object IPassDestination, Track terminalTrack) => _RouteTrackCtor.Invoke(new object[] { IPassDestination, terminalTrack });

        public static object CreatePassConsistInfo(object routeTrack, List<Car> cars) => _PassConsistInfoCtor.Invoke(new object[] { routeTrack, cars });

        public static bool IsPaxCars(TrainCar car)
        {
            var carLiveries = (IEnumerable<TrainCarLivery>)_GetPassengerCars.Invoke(null, null);
            return carLiveries != null && car.carLivery != null && carLiveries.Contains(car.carLivery);
        }

        private static float GetConsistLength(List<TrainCar> trainCars)
        {
            return CarSpawner.Instance.GetTotalCarsLength(
                TrainCar.ExtractLogicCars(trainCars),
                true
            );
        }

        public static bool IsPassengerStation(string yardId) => (bool)(_IsPassengerStation?.Invoke(null, new object[] { yardId }));

        public static List<StationController> AllPaxStations() => StationController.allStations.Where(st => IsPassengerStation(st.stationInfo.YardID)).ToList();

        public static object GetStationData(string yardId) => _GetStationData?.Invoke(null, new object[] { yardId }); // output is IPassDestination : PassStationData

        public static object GetRouteTrackById(string trackId) => _GetRouteTrackById?.Invoke(null, new object[] { trackId });

        public static List<Track> AllPaxTracksForStationData(string yardId) => ((IEnumerable<Track>)_AllTracksProperty.GetValue(GetStationData(yardId))).ToList();

        public static IEnumerable<object> GetPlatforms(object stationData, bool onlyTerminusTracks = false)
        {
            var result = _GetPlatforms.Invoke(stationData, new object[] { onlyTerminusTracks });
            if (result is System.Collections.IEnumerable enumerable) foreach (var item in enumerable) yield return item;
        }
        private static IEnumerable<object> GetPlatformRouteTracks(object stationData)
        {
            foreach (var platform in GetPlatforms(stationData))
            {
                var track = GetRouteTractTrackField(platform);
                yield return CreateRouteTrack(stationData, track);
            }
        }

        public static Track GetRouteTractTrackField(object routeTrack) => (Track)_RouteTrackTrackField.GetValue(routeTrack);

        public static double GetRouteTrackLength(object routeTrack) => (double)_RouteTrackLengthProp.GetValue(routeTrack);

        public static bool CanFitInStation(object stationData, List<TrainCar> trainCars)
        {
            float consistLength = GetConsistLength(trainCars);

            return GetPlatformRouteTracks(stationData)
                .Any(rt => consistLength <= GetRouteTrackLength(rt));
        }

        public static List<object> GetFittingPlatforms(object stationData, List<TrainCar> trainCars)
        {
            float consistLength = GetConsistLength(trainCars);

            return GetPlatformRouteTracks(stationData)
                .Where(rt => consistLength <= GetRouteTrackLength(rt))
                .ToList();
        }

        private static JobType PickPassengerJobType(int carCount)
        {
            if (carCount <= 4)
                return (JobType)_Random.Next(101, 103); // Express or Local

            return PassengerExpress;
        }

        private static void TryGeneratePassengerJob(StationController station, List<TrainCar> trainCars, List<object> fittingPlatforms, JobType jobType)
        {
            foreach (var routeTrack in fittingPlatforms.OrderBy(_ => _Random.Next()))
            {
                var consistInfo = CreatePassConsistInfo(routeTrack, TrainCar.ExtractLogicCars(trainCars));

                if (TryGenerateJob(station.stationInfo.YardID, jobType, consistInfo, out JobChainController passangerChainController))
                {
                    Main._modEntry.Logger.Log($"Successfully reassigned pax consist starting with {trainCars.First().ID} to job {passangerChainController.currentJobInChain.ID}");
                    return;
                }
            }
            HandleSplitOrFail(trainCars, station);
        }

        private static void HandleSplitOrFail(List<TrainCar> trainCars, StationController station)
        {
            if (trainCars.Count < 2)
            {
                Main._modEntry.Logger.Error($"Single pax car {trainCars.First().ID} cannot be reassigned");
                return;
            }

            Main._modEntry.Logger.Log($"Spilitting consist starting with car {trainCars.First().ID}");
            var (first, second) = SplitInHalf(trainCars);
            HandleEmptyPaxCars(first, station);
            HandleEmptyPaxCars(second, station);
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

            foreach (var trainCar in FilterTrainCarGroups(paxConsecutiveTrainCarGroups).SelectMany(tcg => tcg))
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

        public static List<IReadOnlyList<TrainCar>> FilterTrainCarGroups(List<IReadOnlyList<TrainCar>> trainCarGroups)
        {
            trainCarGroups.RemoveAll(trainCars =>
            {
                if (trainCars == null || trainCars.Count < 1)
                {
                    Main._modEntry.Logger.Error("[FilterTrainCarGroups] Invalid trainCars input thrown out");
                    return true;
                }
                var startingTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
                return startingTrack == null;
            });
            return trainCarGroups;
        }

        public static void HandleLoadedPaxCars(List<TrainCar> trainCars, StationController station)
        {
            var startingTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
            if (startingTrack.ID.yardId != station.stationInfo.YardID)
            {
                Main._modEntry.Logger.Error($"[HandleEmptyPaxCars] Station Track mismatch,this should´t happen!");
                return;
            }
        }

        public static void HandleEmptyPaxCars(List<TrainCar> trainCars, StationController station)
        {
            var startingTrack = CarTrackAssignment.FindNearestNamedTrackOrNull(trainCars);
            if (startingTrack == null)
            {
                Main._modEntry.Logger.Error("[HandleEmptyPaxCars] No starting track found");
                return;
            }

            if (startingTrack.ID.yardId != station.stationInfo.YardID)
            {
                Main._modEntry.Logger.Error("[HandleEmptyPaxCars] Station mismatch");
                return;
            }

            if (IsPassengerStation(station.stationInfo.YardID))
            {
                var stationData = GetStationData(station.stationInfo.YardID);
                var fittingPlatforms = GetFittingPlatforms(stationData, trainCars);

                if (!fittingPlatforms.Any())
                {
                    HandleSplitOrFail(trainCars, station);
                    return;
                }

                JobType jobType = PickPassengerJobType(trainCars.Count);
                TryGeneratePassengerJob(station, trainCars, fittingPlatforms, jobType);
                return;
            }
            else
            {
                Main._modEntry.Logger.Log($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} on track {startingTrack.ID.FullID} needs to be transported to a pax jobs station to get reassigned");
                //generate LH job to random pax station: use already existing mod logic elswhere -- potentially chnge to use SP tracks which PaxJobs currently doesn´t - wait for update?
                StationController viableDestStation = AllPaxStations().Distinct().Where(st => st != station).Where(st => CanFitInStation(GetStationData(st.stationInfo.YardID), trainCars)).OrderBy(_ => _Random.Next()).FirstOrDefault();
                if (viableDestStation != null)
                {
                    var viableDestStationStationData = GetStationData(viableDestStation.stationInfo.YardID);
                    Track possibleDestinationTrack = (GetPlatforms(viableDestStationStationData).Select(GetRouteTractTrackField)).Where(t => GetRouteTrackLength(GetRouteTrackById(t.ID.FullID)) > CarSpawner.Instance.GetTotalCarsLength(TrainCar.ExtractLogicCars(trainCars), true)).DefaultIfEmpty(null).ToList().GetRandomElement();
                    var jobChainController = EmptyHaulJobGenerator.GenerateEmptyHaulJobWithExistingCarsOrNull(station, viableDestStation, startingTrack, trainCars, _Random, possibleDestinationTrack);
                    if (jobChainController != null)
                    {
                        FinalizeJobChainControllerAndGenerateFirstJob(jobChainController);
                        return;
                    }
                }
                else
                {
                    Main._modEntry.Logger.Error($"Empty consist of {trainCars.Count()} pax cars starting with {trainCars.First().ID} can´t be reassigned a LH to any pax station, attempting splitting");
                    HandleSplitOrFail(trainCars, station);
                    return;
                }
            }

            Main._modEntry.Logger.Error("[HandleEmptyPaxCars] End of function reached possibly without reassigning, this shouldn´t happen!");
        }
    }
}