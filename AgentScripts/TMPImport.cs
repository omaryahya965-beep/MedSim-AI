using UnityEditor;
if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return "TMP settings already present: " + AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:TMP_Settings")[0]);
var pkg = System.IO.Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
if (!System.IO.File.Exists(pkg)) return "missing " + pkg;
AssetDatabase.ImportPackage(pkg, false);
return "imported " + pkg;
