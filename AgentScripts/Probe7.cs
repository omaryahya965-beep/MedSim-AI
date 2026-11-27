using UnityEngine; using UnityEditor; using System.Linq;
var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Characters/AdamReyes/AdamReyes.prefab");
var s = "";
foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(x => x.name.Contains("low-poly") || x.name.Contains("eyelash") || x.name.Contains("teeth"))) {
  var bw = r.sharedMesh.boneWeights; var counts = new System.Collections.Generic.Dictionary<string, float>();
  foreach (var w in bw) { void A(int i, float v) { if (v <= 0) return; var n = r.bones[i].name; counts[n] = (counts.ContainsKey(n) ? counts[n] : 0) + v; } A(w.boneIndex0, w.weight0); A(w.boneIndex1, w.weight1); A(w.boneIndex2, w.weight2); A(w.boneIndex3, w.weight3); }
  s += r.name + ": " + string.Join(", ", counts.OrderByDescending(k => k.Value).Take(5).Select(k => k.Key + "=" + (k.Value / bw.Length).ToString("0.00"))) + "\n";
}
return s;
