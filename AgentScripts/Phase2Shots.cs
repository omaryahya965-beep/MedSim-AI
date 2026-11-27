using UnityEngine; using System.IO; using System.Linq; using System.Threading.Tasks;
using MedSimAI.Core; using MedSimAI.AI; using MedSimAI.Voice; using MedSimAI.Instruments;
public static class Phase2Shots {
  static string Take(string name, Vector3 pos, Vector3 look, float fov) {
    var go = new GameObject("TempShotCam"); var cam = go.AddComponent<Camera>(); cam.stereoTargetEye = StereoTargetEyeMask.None;
    cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
    var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); data.renderPostProcessing = true; data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
    var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render();
    RenderTexture.active = rt; var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
    var p = "Screenshots/Phase2/" + name + ".png"; File.WriteAllBytes(p, tex.EncodeToPNG()); Object.DestroyImmediate(go); rt.Release(); return p;
  }
  static async Task Wait(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }
  public static async Task<string> Run() {
    while (SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) await Task.Yield();
    var eye = new Vector3(1.2f, 1.62f, 0.65f);
    SessionManager.Instance.StartClinicalSession(true);
    await Wait(1.2f);
    Take("01_room_student_viewpoint", eye, new Vector3(-0.3f, 1.05f, 0.9f), 82);
    Take("02_doctor_closeup", new Vector3(0.05f, 1.6f, 0.05f), new Vector3(-0.85f, 1.52f, -0.5f), 34);
    VoiceInteractionManager.Instance.Interrupt();
    var p = await Task.Run(() => 0); // keep context
    _ = VoiceInteractionManager.Instance.Submit(Persona.Patient, "Can you describe what the pain feels like?");
    await Wait(1.6f);
    Take("03_patient_face_reclining", new Vector3(0.55f, 1.3f, 0.35f), new Vector3(0.3f, 1.12f, 1.25f), 38);
    Take("07_conversation", eye, new Vector3(0.3f, 1.25f, 1.25f), 75);
    var cuff = Object.FindAnyObjectByType<BloodPressureController>(); cuff.PlaceInZone(Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "ARM_UPPER_L"));
    var ox = Object.FindAnyObjectByType<PulseOximeterController>(); ox.PlaceInZone(Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "FINGER_L"));
    var st = Object.FindAnyObjectByType<StethoscopeController>(); var mz = Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None).First(z => z.zoneId == "CHEST_MITRAL");
    st.transform.rotation = Quaternion.FromToRotation(Vector3.down, -mz.transform.forward); st.transform.position += mz.transform.position - st.probePoint.position; mz.SetHighlight(true, true);
    Object.FindAnyObjectByType<ECGMachineController>().Record();
    await Wait(8f);
    Take("04_bed_area", new Vector3(2.2f, 1.9f, -1.6f), new Vector3(0.3f, 0.8f, 1.0f), 60);
    Take("05_instruments_on_patient", new Vector3(0.95f, 1.35f, 0.55f), new Vector3(0.3f, 0.95f, 1.05f), 52);
    Take("05b_ecg_cart", new Vector3(1.25f, 1.5f, 0.4f), new Vector3(1.45f, 1.0f, -0.35f), 55);
    Take("06_patient_monitor", new Vector3(0.1f, 1.5f, 1.4f), new Vector3(-0.55f, 1.62f, 2.15f), 42);
    mz.SetHighlight(false, false);
    SessionManager.Instance.BeginReasoning();
    await VoiceInteractionManager.Instance.Submit(Persona.Instructor, "He has crushing chest pain with ST elevation in the inferior leads, I think this is a STEMI and I want to escalate to the senior and cardiology now.");
    VoiceInteractionManager.Instance.Interrupt();
    SessionManager.Instance.FinishCase();
    await Wait(2f);
    Take("08_debrief", eye, new Vector3(-0.35f, 1.72f, 0.62f), 62);
    return "done";
  }
}
