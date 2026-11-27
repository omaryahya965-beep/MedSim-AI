using UnityEngine; using System.Linq; using System.Threading.Tasks; using MedSimAI.Characters; using MedSimAI.Core;
public static class EyeTest {
  public static async Task<string> Run() {
    while (SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) await Task.Yield();
    var lifes = Object.FindObjectsByType<HumanoidLifeController>(FindObjectsSortMode.None);
    var p = lifes.First(l => l.lyingDown);
    var body = p.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == "Human");
    string W() => string.Join(",", new[]{"eyeBlinkLeft","eyeLookDownLeft","eyeLookUpLeft","eyeLookInLeft","eyeLookOutLeft","eyeSquintLeft","browDownLeft"}.Select(n => n + "=" + body.GetBlendShapeWeight(body.sharedMesh.GetBlendShapeIndex(n)).ToString("0")));
    var a = W();
    foreach (var l in lifes) l.useEyeLookShapes = false;
    float t = Time.realtimeSinceStartup + 1f; while (Time.realtimeSinceStartup < t) await Task.Yield();
    return "with look: " + a + " | without: " + W();
  }
}
