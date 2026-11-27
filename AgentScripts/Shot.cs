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
