using UnityModManagerNet;

namespace PersistentJobsMod {
    public sealed class Settings : UnityModManager.ModSettings, IDrawable {
        [Draw("Prevent accepting shunting (un)load jobs if cars are already on a loading track (L)")]
        public bool PreventStartingShuntingJobForCarsOnWarehouseTrack = true;

        public bool PreventStartingShuntingJobForCarsOnWarehouseTrackMessageWasShown = false;

        [Draw("Make shunting unload jobs end on loading tracks (L) (not recommended)")]
        public bool ShuntingUnloadEndsOnLTracks = false;

        [Draw("Replace destination tracks if there is no space")]
        public bool DestinationTrackChange = true;

        [Draw("Show track signs for all named tracks")]
        public bool GenerateTrackSigns = false;

        [Draw("Intercompatibility with Passenger Jobs mod (toggle mod off/on for setting change to take effect)")]
        public bool PaxJobsCompatibility = true;

        [Draw("Suspend cars in far away stations in order to improve performance")]
        public bool SuspendFarAwayCars = true;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }

        void IDrawable.OnChange() { }
    }
}