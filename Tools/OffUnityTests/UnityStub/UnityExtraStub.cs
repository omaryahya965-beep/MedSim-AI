// COMPILE-CHECK STUB ONLY (see UnityEngineStub.cs). UI, TMP, InputSystem, XR, Networking, SceneManagement, plus the few
// existing MedSim types the Surgery scripts call. Signatures mirror the real APIs; bodies are empty.
using System;
using System.Collections;
using System.Collections.Generic;
namespace UnityEngine
{
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum LightType { Spot, Directional, Point }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public enum CursorLockMode { None, Locked, Confined }
    public static class Cursor { public static CursorLockMode lockState; public static bool visible; }
    public static class Screen { public static int width = 1920, height = 1080; }
    public static class GUI { public static void Label(Rect r, string s) { } }
    public static class PlayerPrefs { public static string GetString(string k, string d = "") => d; public static void SetString(string k, string v) { } public static int GetInt(string k, int d = 0) => d; public static void SetInt(string k, int v) { } public static void Save() { } public static bool HasKey(string k) => false; }
    public class Shader : Object { public static Shader Find(string n) => new Shader(); }
    public class Material : Object { public Material(Shader s) { } public Material(Material m) { } public bool HasProperty(string n) => true; public void SetColor(string n, Color c) { } public void SetFloat(string n, float f) { } }
    public class Renderer : Component { public Material[] materials = new Material[0], sharedMaterials = new Material[0]; public Material sharedMaterial, material; }
    public class Light : Behaviour { public LightType type; public float spotAngle, intensity, range; }
    public partial class GameObjectExt { }
    public static class GameObjectPrimitive { }
    public class AnimatorControllerParameter { public string name = ""; }
    public class RuntimeAnimatorController : Object { }
    public static class Microphone { public static string[] devices = new string[0]; public static AudioClip Start(string d, bool loop, int sec, int rate) => new AudioClip(); public static void End(string d) { } public static int GetPosition(string d) => 0; }
    [AttributeUsage(AttributeTargets.Class)] public class ExecuteAlways : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class ContextMenu : Attribute { public ContextMenu(string n) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
    public class RectTransform : Transform { public RectTransform() : base(new GameObject()) { } public Vector2 sizeDelta, anchorMin, anchorMax, offsetMin, offsetMax, pivot; public void SetParent(Transform p, bool w) { } }
}
namespace UnityEngine
{
    public static class AudioClipExt { }
    public partial class Canvas2 { }
}
namespace UnityEngine.UI
{
    public class Graphic : Behaviour { public Color color; public bool raycastTarget; }
    public class Image : Graphic { }
    public class Selectable : Behaviour { public bool interactable = true; }
    public class Button : Selectable { public class ButtonClickedEvent { public void AddListener(Action a) { } } public ButtonClickedEvent onClick = new ButtonClickedEvent(); }
    public class CanvasScaler : Behaviour { public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize } public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight, dynamicPixelsPerUnit; }
    public class GraphicRaycaster : Behaviour { }
    public class HorizontalLayoutGroup : Behaviour { public float spacing; public bool childForceExpandWidth, childControlWidth, childControlHeight; }
}
namespace TMPro
{
    public enum TextAlignmentOptions { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }
    [Flags] public enum FontStyles { Normal = 0, Bold = 1, Italic = 2 }
    public class TMP_Text : UnityEngine.UI.Graphic { public string text = ""; public TextAlignmentOptions alignment; public float fontSize; public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform(); }
}
namespace UnityEngine.InputSystem
{
    public static class CommonUsages { }   // exists in the real InputSystem: catches ambiguity with UnityEngine.XR.CommonUsages
    public class KeyControl { public bool isPressed, wasPressedThisFrame; }
    public class Keyboard { public static Keyboard current; public KeyControl tKey = new KeyControl(), mKey = new KeyControl(), f10Key = new KeyControl(), escapeKey = new KeyControl(), hKey = new KeyControl(), jKey = new KeyControl(), nKey = new KeyControl(), wKey = new KeyControl(), sKey = new KeyControl(), pKey = new KeyControl(), oKey = new KeyControl(), gKey = new KeyControl(); }
}
namespace UnityEngine.XR
{
    public enum XRNode { LeftHand, RightHand, Head }
    public struct InputFeatureUsage<T> { }
    public static class CommonUsages { public static InputFeatureUsage<bool> primaryButton, menuButton; }
    public struct InputDevice { public bool isValid; public bool TryGetFeatureValue(InputFeatureUsage<bool> u, out bool v) { v = false; return false; } }
    public static class InputDevices { public static InputDevice GetDeviceAtXRNode(XRNode n) => default; }
}
namespace UnityEngine.XR.Interaction.Toolkit.UI { public class TrackedDeviceGraphicRaycaster : UnityEngine.Behaviour { } }
namespace UnityEngine.Networking
{
    public class UploadHandler { } public class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] d) { } }
    public class DownloadHandler { public string text = ""; } public class DownloadHandlerBuffer : DownloadHandler { }
    public class DownloadHandlerScript : DownloadHandler { public DownloadHandlerScript(byte[] b) { } protected virtual bool ReceiveData(byte[] data, int len) => true; protected virtual void CompleteContent() { } }
    public enum AudioType { WAV, MPEG, OGGVORBIS }
    public class DownloadHandlerAudioClip : DownloadHandler { public DownloadHandlerAudioClip(string u, AudioType t) { } public UnityEngine.AudioClip audioClip; public static UnityEngine.AudioClip GetContent(UnityWebRequest w) => null; }
    public static class UnityWebRequestMultimedia { public static UnityWebRequest GetAudioClip(string u, AudioType t) => new UnityWebRequest(u, "GET"); }
    public class UnityWebRequestAsyncOperation { public bool isDone; }
    public class UnityWebRequest : IDisposable { public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public UnityWebRequest(string url, string method) { } public UploadHandler uploadHandler; public DownloadHandler downloadHandler; public int timeout; public Result result; public long responseCode; public bool isDone; public string error = "";
        public void SetRequestHeader(string k, string v) { } public UnityWebRequestAsyncOperation SendWebRequest() => new UnityWebRequestAsyncOperation(); public void Abort() { } public void Dispose() { } public static string EscapeURL(string s) => s; }
}
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene { public string name; }
    public static class SceneManager { public static void LoadScene(string n, LoadSceneMode m) { } public static Scene GetActiveScene() => default; }
}
