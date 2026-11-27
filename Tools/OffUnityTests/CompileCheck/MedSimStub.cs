// COMPILE-CHECK ONLY: stand-ins for existing MedSim runtime types used by Surgery scripts (CompileCheck project only).
using System;
namespace MedSimAI.AI
{
    public enum Persona { Patient, Instructor }
    [Serializable] public class AIRuntimeConfig { public string proxyEndpoint = "", clientSessionToken = "", language = "en"; public bool preferOnline = true; public int timeoutMs = 25000; }
    public class AIProviderManager : UnityEngine.MonoBehaviour { public static bool ForceOffline; public static AIRuntimeConfig ConfigOverride; public static string ConfigPath => ""; }
}
namespace MedSimAI.Voice
{
    public class StreamingVoice { public StreamingVoice(string name = "") { } public float FirstAudioTime = -1; public bool Cancelled, Completed; public void Complete() { } public void Cancel() { } }
    public class VoiceStreamHandler : UnityEngine.Networking.DownloadHandlerScript { public VoiceStreamHandler(StreamingVoice v, Action<string> m) : base(new byte[1]) { } }
}
namespace MedSimAI.Characters
{
    public class CharacterSpeechController : UnityEngine.MonoBehaviour { public MedSimAI.AI.Persona persona; public string displayName = ""; public bool IsSpeaking { get; } public void Speak(string l, UnityEngine.AudioClip c = null) { } public void SpeakStreaming(string l, MedSimAI.Voice.StreamingVoice s) { } public void Stop() { } }
}
namespace MedSimAI.UI
{
    public static class Brand { public static UnityEngine.Color Navy, NavyLight, Teal, TealLight, Blue, White, Muted, Warn, Danger; }
    public static class UIFactory
    {
        public static Func<UnityEngine.Canvas, UnityEngine.Component> RaycasterFactory;
        public static UnityEngine.Canvas WorldCanvas(string n, UnityEngine.Transform p, UnityEngine.Vector2 s, float scale = 0.001f) => new UnityEngine.Canvas();
        public static UnityEngine.RectTransform Panel(UnityEngine.Transform p, string n, UnityEngine.Color c, UnityEngine.Vector2? s = null, bool r = true) => new UnityEngine.RectTransform();
        public static void Stretch(UnityEngine.RectTransform rt, float pad = 0) { }
        public static TMPro.TMP_Text Text(UnityEngine.Transform p, string t, float s, UnityEngine.Color c, TMPro.TextAlignmentOptions a = TMPro.TextAlignmentOptions.TopLeft, TMPro.FontStyles st = TMPro.FontStyles.Normal) => new TMPro.TMP_Text();
        public static UnityEngine.UI.Button Button(UnityEngine.Transform p, string l, Action a, UnityEngine.Color? bg = null, float fs = 22, float h = 64) => new UnityEngine.UI.Button();
        public static UnityEngine.RectTransform VStack(UnityEngine.Transform p, string n, float sp = 10, int pad = 16) => new UnityEngine.RectTransform();
        public static UnityEngine.RectTransform ScrollArea(UnityEngine.Transform p, out UnityEngine.RectTransform content) { content = new UnityEngine.RectTransform(); return new UnityEngine.RectTransform(); }
        public static void Place(UnityEngine.RectTransform rt, UnityEngine.Vector2 a, UnityEngine.Vector2 b, UnityEngine.Vector2 c = default, UnityEngine.Vector2 d = default) { }
    }
    public static class ArabicShaper { public static bool ContainsArabic(string s) => false; public static void Apply(TMPro.TMP_Text t, string s) { } public static string PrepareRtl(string s) => s; }
}
namespace MedSimAI.XR { public enum DisplayMode { Detecting, VR, Desktop } public class DisplayModeManager { public static DisplayMode Mode; } }
