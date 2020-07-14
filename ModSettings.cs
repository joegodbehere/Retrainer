using Newtonsoft.Json;
using System;

namespace Retrainer
{
    internal class ModSettings
    {
        //original
        public int Cost = 500000;
        public bool OnceOnly = false;
        public bool TrainingModuleRequired = false;

        //new
        public bool RespecCausesInjury = true;
        public float MedtechPerExperience = 0.01f;
        public bool VariableCreditsCost = true;
        public float CreditsPerExperience = 10.0f;

        public bool Debug = true;
        public bool DebugVerbose = false;

        public static ModSettings ReadSettings(string json)
        {
            ModSettings settings;

            try
            {
                settings = JsonConvert.DeserializeObject<ModSettings>(json);
            }
            catch (Exception e)
            {
                settings = new ModSettings();
                Logger.Log($"Reading settings failed: {e.Message}");
            }

            return settings;
        }
    }
}
