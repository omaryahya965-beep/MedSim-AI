using UnityEditor; using UnityEngine; using UnityEngine.XR.OpenXR; using UnityEditor.XR.Management; using UnityEditor.XR.Management.Metadata; using UnityEngine.XR.Management; using System.Linq;
var sb = new System.Text.StringBuilder();
EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget per);
if (per == null) { per = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(); if(!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets","XR"); AssetDatabase.CreateAsset(per,"Assets/XR/XRGeneralSettingsPerBuildTarget.asset"); EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, per, true); }
var want = new System.Collections.Generic.Dictionary<BuildTargetGroup,string[]>{
 {BuildTargetGroup.Standalone, new[]{"VIVEFocus3Profile","HTCViveControllerProfile","OculusTouchControllerProfile","ValveIndexControllerProfile","KHRSimpleControllerProfile"}},
 {BuildTargetGroup.Android, new[]{"VIVEFocus3Feature","VIVEFocus3Profile","ViveDisplayRefreshRate","KHRSimpleControllerProfile"}}};
foreach (var kv in want) {
  var g = kv.Key;
  if (!per.HasSettingsForBuildTarget(g)) per.CreateDefaultSettingsForBuildTarget(g);
  if (!per.HasManagerSettingsForBuildTarget(g)) per.CreateDefaultManagerSettingsForBuildTarget(g);
  var gs = per.SettingsForBuildTarget(g);
  var ok = XRPackageMetadataStore.AssignLoader(gs.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", g);
  gs.InitManagerOnStart = true;
  var s = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
  foreach (var f in s.GetFeatures()) if (kv.Value.Contains(f.GetType().Name)) { f.enabled = true; EditorUtility.SetDirty(f); }
  if (g == BuildTargetGroup.Standalone) s.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
  EditorUtility.SetDirty(s); EditorUtility.SetDirty(gs);
  sb.Append(g + " assign=" + ok + " loaders=" + string.Join(",", gs.Manager.activeLoaders.Select(l=>l.name)) + " enabled=" + string.Join(",", s.GetFeatures().Where(f=>f.enabled).Select(f=>f.GetType().Name)) + "\n");
}
EditorUtility.SetDirty(per); AssetDatabase.SaveAssets();
return sb.ToString();
