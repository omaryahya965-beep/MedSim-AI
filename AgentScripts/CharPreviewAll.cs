using UnityEngine; using UnityEditor; using System.IO;
public static class Shot {
  public static string Take(string name, float px, float py, float pz, float tx, float ty, float tz, float fov) {
    var go = new GameObject("TempShotCam"); var cam = go.AddComponent<Camera>();
    cam.transform.position = new Vector3(px,py,pz); cam.transform.LookAt(new Vector3(tx,ty,tz)); cam.stereoTargetEye = StereoTargetEyeMask.None; cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
    var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
    var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4; cam.targetTexture = rt; cam.Render();
    RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0,0,1280,720),0,0); tex.Apply(); RenderTexture.active = null;
    File.WriteAllBytes("Screenshots/" + name + ".png", tex.EncodeToPNG());
    Object.DestroyImmediate(go); rt.Release();
    return "Screenshots/" + name + ".png";
  }
}
public static class CharPreview {
  public static string Run() {
    var root = new GameObject("TEMP_PREVIEW"); root.transform.position = new Vector3(20, 0, 0);
    var d = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Characters/DrSaraHassan/DrSaraHassan.prefab"), root.transform);
    d.transform.localPosition = new Vector3(-0.5f, 0, 0); d.transform.localRotation = Quaternion.Euler(0, 180, 0);
    var p = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MedSimAI/Characters/AdamReyes/AdamReyes.prefab"), root.transform);
    p.transform.localPosition = new Vector3(0.5f, 0, 0); p.transform.localRotation = Quaternion.Euler(0, 180, 0);
    var l = new GameObject("k").AddComponent<Light>(); l.transform.SetParent(root.transform); l.type = LightType.Directional; l.intensity = 1.2f; l.transform.rotation = Quaternion.Euler(30, 160, 0);
    var r1 = Shot.Take("P2_char_full", 20, 1.1f, -3.2f, 20, 0.95f, 0, 38);
    var r2 = Shot.Take("P2_char_faces", 19.5f, 1.55f, -1.0f, 19.5f, 1.5f, 0, 32);
    Object.DestroyImmediate(root);
    return r1 + " " + r2;
  }
}
