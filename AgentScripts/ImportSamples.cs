using UnityEditor.PackageManager.UI; using System.Linq;
var r = "";
foreach (var s in Sample.FindByPackage("com.unity.xr.interaction.toolkit", null)) {
  r += s.displayName + (s.isImported?"[imported]":"") + "; ";
  if (s.displayName == "Starter Assets" && !s.isImported) r += "import=" + s.Import(Sample.ImportOptions.HideImportWindow) + "; ";
}
return r;
