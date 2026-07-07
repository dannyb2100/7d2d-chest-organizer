using HarmonyLib;

namespace CategorySorter
{
    /// <summary>
    /// Einstiegspunkt der Mod. 7DTD ruft InitMod beim Laden auf (Server und Client).
    /// Die eigentliche Sortier-Logik laeuft ausschliesslich serverseitig (siehe GameManager_Patch).
    /// </summary>
    public class ModApi : IModApi
    {
        public const string ModName = "CategorySorter";

        public void InitMod(Mod modInstance)
        {
            Config.Load();

            var harmony = new Harmony("category.sorter.harmony");
            var patched = ChestClosePatches.Apply(harmony);

            if (patched)
                Log.Out("[" + ModName + "] initialized");
            else
                Log.Error("[" + ModName + "] initialized without a supported chest-close hook; sorting is disabled.");
        }
    }
}
