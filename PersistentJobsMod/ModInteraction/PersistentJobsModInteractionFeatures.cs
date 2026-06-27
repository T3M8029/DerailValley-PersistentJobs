using DV.Logic.Job;
using System;

namespace PersistentJobsMod.ModInteraction
{
    public static class PersistentJobsModInteractionFeatures
    {
        public static event Action<Job> JobTracksChanged;

        public static void RegisterJobTracksChangedListener(Action<Job> callback)
        {
            JobTracksChanged += job => callback(job);
        }

        public static void InvokeJobTrackChanged(Job job)
        {
            JobTracksChanged?.Invoke(job);
        }

        public static event Action<(Job, Car)> JobCarsChanged;

        public static void RegisterJobCarsChangedListener(Action<(Job, Car)> callback)
        {
            JobCarsChanged += jct => callback(jct);
        }

        public static void InvokeJobCarsChanged(Job job, Car car)
        {
            JobCarsChanged?.Invoke((job, car));
        }
    }
}