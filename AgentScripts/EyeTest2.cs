using UnityEngine; using System.Linq; using System.Threading.Tasks; using MedSimAI.Core;
public static class EyeTest2 {
  public static async Task<string> Run() {
    while (SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) await Task.Yield();
    var eyes = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r => r.name.Contains("low-poly")).ToList();
    var info = string.Join(" | ", eyes.Select(r => r.transform.root.name + " vis=" + r.isVisible + " enabled=" + r.enabled + " bounds=" + r.bounds.center.ToString("F2") + " root=" + (r.rootBone ? r.rootBone.name : "null") + " mat=" + r.sharedMaterial.name + " tex=" + (r.sharedMaterial.mainTexture ? r.sharedMaterial.mainTexture.name : "none")));
    return info;
  }
}
