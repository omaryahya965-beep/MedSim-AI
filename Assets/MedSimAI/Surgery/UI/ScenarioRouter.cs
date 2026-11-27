using MedSimAI.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace MedSimAI.Surgery.UI
{
    /// <summary>
    /// Scene routing between the scenario-selection menu and the two independent simulations. Each simulation is its own
    /// scene, so loading one destroys the other's session state (clinical ClinicalSession / surgical SurgerySessionCore).
    /// </summary>
    public static class ScenarioRouter
    {
        public const string MenuScene = "ScenarioSelect";
        public const string ClinicalScene = "MedSim_ER";
        public const string SurgicalScene = "OperatingRoom";
        public const string LanguagePref = "medsim.language";

        public static string Language
        {
            get => PlayerPrefs.GetString(LanguagePref, "en") == "ar" ? "ar" : "en";
            set { PlayerPrefs.SetString(LanguagePref, value == "ar" ? "ar" : "en"); PlayerPrefs.Save(); }
        }

        public static bool InBuild(string scene) => Application.CanStreamedLevelBeLoaded(scene);
        public static bool MenuAvailable => InBuild(MenuScene);

        public static void Load(string scene)
        {
            if (!InBuild(scene)) { Debug.LogError($"[MedSim] Scene '{scene}' is not in the build settings (run MedSim AI → Surgery → Add Scenes To Build)."); return; }
            Debug.Log("[MedSim] Loading scene " + scene);
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }
        public static void ToMenu() => Load(MenuScene);
        public static void StartClinical() => Load(ClinicalScene);
        public static void StartSurgical() => Load(SurgicalScene);
        public static void RestartCurrent() => Load(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Global "return to scenario menu" shortcut that works in both simulations without modifying the clinical code:
    /// F10 on desktop, or hold the left controller's menu button for 2 seconds in VR. Only active when the menu scene is
    /// in the build and we are not already in it.
    /// </summary>
    public class ReturnToMenuShortcut : MonoBehaviour
    {
        float held;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!ScenarioRouter.MenuAvailable || FindAnyObjectByType<ReturnToMenuShortcut>()) return;
            var go = new GameObject("[MedSim Return To Menu]"); DontDestroyOnLoad(go); go.AddComponent<ReturnToMenuShortcut>();
        }
        void Update()
        {
            if (SceneManager.GetActiveScene().name == ScenarioRouter.MenuScene) { held = 0; return; }
            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame) { ScenarioRouter.ToMenu(); return; }
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool down = l.isValid && l.TryGetFeatureValue(CommonUsages.menuButton, out var m) && m;
            held = down ? held + Time.unscaledDeltaTime : 0;
            if (held >= 2f) { held = 0; ScenarioRouter.ToMenu(); }
        }
    }

    /// <summary>Canvas helper: world-space panel in VR (via the existing UIFactory, XR raycaster included), screen overlay on desktop.</summary>
    public static class SurgeryCanvas
    {
        public static Canvas Create(string name, Vector2 sizeMm, bool desktop)
        {
            var c = UIFactory.WorldCanvas(name, null, sizeMm);
            if (desktop)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 50;
                var scaler = c.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
                if (!c.GetComponent<UnityEngine.UI.GraphicRaycaster>()) c.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            return c;
        }

        /// <summary>Places a world-space canvas in front of the camera at a comfortable reading distance.</summary>
        public static void PlaceInFront(Canvas c, Camera cam, float distance = 1.3f, float down = 0.1f)
        {
            if (!c || c.renderMode != RenderMode.WorldSpace || !cam) return;
            var f = cam.transform.forward; f.y = 0; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            c.transform.position = cam.transform.position + f * distance + Vector3.down * down;
            c.transform.rotation = Quaternion.LookRotation(f);
        }

        public static bool IsDesktop => MedSimAI.XR.DisplayModeManager.Mode != MedSimAI.XR.DisplayMode.VR;
    }
}
