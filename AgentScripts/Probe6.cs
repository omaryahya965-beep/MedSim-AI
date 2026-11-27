using UnityEngine; using UnityEditor; using System.Linq;
var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Characters/AdamReyes/AdamReyes.prefab");
var r = go.GetComponentsInChildren<SkinnedMeshRenderer>().First(x => x.name == "Human");
var names = Enumerable.Range(0, r.sharedMesh.blendShapeCount).Select(i => r.sharedMesh.GetBlendShapeName(i));
var lash = go.GetComponentsInChildren<SkinnedMeshRenderer>().First(x => x.name.Contains("eyelash"));
return string.Join(",", names) + "\nLASH: " + string.Join(",", Enumerable.Range(0, lash.sharedMesh.blendShapeCount).Select(i => lash.sharedMesh.GetBlendShapeName(i)));
