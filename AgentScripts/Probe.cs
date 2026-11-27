using UnityEditor;
var s = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
return "input=" + s.FindProperty("activeInputHandler").intValue
 + " android=" + BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)
 + " win=" + BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64)
 + " active=" + EditorUserBuildSettings.activeBuildTarget
 + " rp=" + UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline?.name;
