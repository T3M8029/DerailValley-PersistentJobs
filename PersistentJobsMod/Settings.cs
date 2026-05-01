using UnityModManagerNet;

namespace PersistentJobsMod {
    public sealed class Settings : UnityModManager.ModSettings, IDrawable {
        [Draw("Prevent accepting shunting (un)load jobs if cars are already on loading track (L)")]
        public bool PreventStartingShuntingJobForCarsOnWarehouseTrack = true;

        public bool PreventStartingShuntingJobForCarsOnWarehouseTrackMessageWasShown = false;

        [Draw("Replace destination tracks if there is no space")]
        public bool DestinationTrackChange = true;

        [Draw("Show track signs for all named tracks")]
        public bool GenerateTrackSigns = false;

        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }

        void IDrawable.OnChange() { }
    }
}