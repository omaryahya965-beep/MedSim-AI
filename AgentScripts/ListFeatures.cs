using UnityEditor; using UnityEngine.XR.OpenXR; using System.Linq;
var sb = new System.Text.StringBuilder();
foreach (var g in new[]{BuildTargetGroup.Standalone, BuildTargetGroup.Android}) {
  var s = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
  sb.Append(g + ": ");
  if (s == null) { sb.Append("null\n"); continue; }
  foreach (var f in s.GetFeatures()) sb.Append(f.GetType().Name + (f.enabled?"*":"") + ", ");
  sb.Append("\n");
}
return sb.ToString();
