// Points UnityEngine.Resources.Load<TextAsset> (stub) at Assets/MedSimAI/Resources so ScenarioData.LoadFromResources works.
using System.IO;
using System.Runtime.CompilerServices;
static class ResourcesHook
{
    [ModuleInitializer] internal static void Init()
    {
        string root = null;
        for (var d = new DirectoryInfo(System.AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "Assets/MedSimAI/Resources"))) { root = Path.Combine(d.FullName, "Assets/MedSimAI/Resources"); break; }
        UnityEngine.Resources.TextLoader = p => { if (root == null) return null; var f = Path.Combine(root, p + ".json"); return File.Exists(f) ? File.ReadAllText(f) : null; };
    }
}
