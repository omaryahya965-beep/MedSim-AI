using System;
using System.IO;
using MedSimAI.Surgery.Shared;

namespace MedSimAI.Surgery.Tests
{
    /// <summary>Loads authored surgical scenario JSON in Unity (JsonUtility) and off-Unity (System.Text.Json).</summary>
    public static class TestScenarioLoader
    {
        public const string TkaPath = "Assets/MedSimAI/Surgery/Scenarios/Resources/SurgeryScenarios/OR_TKA_TRAINING_001.json";

        public static string RepoFile(string relative)
        {
            if (File.Exists(relative)) return Path.GetFullPath(relative);   // Unity: cwd = project root
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
            throw new FileNotFoundException(relative);
        }

        public static SurgicalScenarioData FromJson(string json)
        {
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<SurgicalScenarioData>(json);
#else
            return System.Text.Json.JsonSerializer.Deserialize<SurgicalScenarioData>(json, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
        }

        public static SurgicalScenarioData LoadTka() => FromJson(File.ReadAllText(RepoFile(TkaPath)));
    }
}
