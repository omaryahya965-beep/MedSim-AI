using UnityEngine; using UnityEditor; using System.Linq;
string Dump(string p) {
  var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
  var rs = go.GetComponentsInChildren<Renderer>(true).Select(r => r.name + ":" + r.GetType().Name + (r is SkinnedMeshRenderer s ? "/bones" + s.bones.Length : ""));
  var b = go.GetComponentsInChildren<Transform>(true).Take(14).Select(t => t.name);
  var mi = (ModelImporter)AssetImporter.GetAtPath(p);
  var hd = mi.humanDescription; var mapped = hd.human?.Length ?? 0;
  var bounds = go.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
  return p + "\n renderers: " + string.Join(", ", rs) + "\n first transforms: " + string.Join(",", b) + "\n humanMapped=" + mapped + " height=" + bounds.size.y.ToString("0.00") + "\n";
}
return Dump("Assets/MedSimAI/Characters/DrSaraHassan/DrSaraHassan.fbx") + Dump("Assets/MedSimAI/Characters/AdamReyes/AdamReyes.fbx");
