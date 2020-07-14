using System.Reflection;
using Harmony;

namespace Retrainer
{
    public static class Main
    {
        internal static HBS.Logging.ILog HBSLog;
        internal static ModSettings Settings;

        // ENTRY POINT
        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.BattleTech.Retrainer");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            HBSLog = HBS.Logging.Logger.GetLogger("Retrainer");
            Logger.Prefix = "[Retrainer] ";
            Settings = ModSettings.ReadSettings(modSettings);
        }
    }
}
