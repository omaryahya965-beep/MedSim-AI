using UnityEngine; using System.Linq;
var all = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.name.StartsWith("Zone ") && t.gameObject.scene.IsValid()).ToList();
return "count=" + all.Count + " " + string.Join(" | ", all.Take(4).Select(t => t.name + " comps=" + string.Join(",", t.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name))));
