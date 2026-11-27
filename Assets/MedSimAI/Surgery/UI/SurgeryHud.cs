using System;
using System.Collections.Generic;
using MedSimAI.Surgery.Shared;
using MedSimAI.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace MedSimAI.Surgery.UI
{
    /// <summary>
    /// Operating-room HUD: validation banner, current objective, AI/mechanics status, conversation transcript (RTL-aware),
    /// pause menu (restart / language / mic / finish / return to menu) and the final debrief. Desktop: screen overlay;
    /// VR: world-space panels near the student. Esc (desktop) or the left menu button (VR) toggles the pause menu.
    /// </summary>
    public class SurgeryHud : MonoBehaviour
    {
        public event Action RestartRequested, MenuRequested, LanguageToggled, FinishRequested, MicModeToggled;
        public string Language = "en";

        Canvas canvas; RectTransform root, pause, debrief; TMP_Text banner, objTitle, objBody, progress, status, transcript, mic, debriefText;
        readonly List<string> lines = new List<string>();
        bool desktop, menuWasDown;
        string objT = "", objB = "", prog = "", stat = "", micText = "", bannerText = SurgeryContract.VALIDATION_BANNER; Color statColor = Color.white;

        void Start() { desktop = SurgeryCanvas.IsDesktop; Build(); }

        string L(string en, string ar) => Language == "ar" ? ar : en;
        TMP_Text Txt(Transform p, string s, float size, Color c, TextAlignmentOptions a = TextAlignmentOptions.TopLeft, FontStyles st = FontStyles.Normal) { var t = UIFactory.Text(p, "", size, c, a, st); Set(t, s); return t; }
        void Set(TMP_Text t, string s)
        {
            if (!t) return; ArabicShaper.Apply(t, s ?? "");
            bool rtl = ArabicShaper.ContainsArabic(s ?? "");
            if (t.alignment == TextAlignmentOptions.TopLeft || t.alignment == TextAlignmentOptions.TopRight) t.alignment = rtl ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        }

        public void Rebuild() { Build(); }

        void Build()
        {
            if (canvas) Destroy(canvas.gameObject);
            canvas = SurgeryCanvas.Create("Surgery HUD", new Vector2(1600, 900), desktop);
            root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>(); root.SetParent(canvas.transform, false); UIFactory.Stretch(root);

            var top = UIFactory.Panel(root, "Banner", new Color(0.35f, 0.22f, 0.02f, 0.85f)); UIFactory.Place(top, new Vector2(0.25f, 0.95f), new Vector2(0.75f, 1f));
            banner = Txt(top, "⚠ " + bannerText, 22, Brand.Warn, TextAlignmentOptions.Center, FontStyles.Bold); UIFactory.Stretch(banner.rectTransform, 4);

            var obj = UIFactory.Panel(root, "Objective", new Color(0.04f, 0.10f, 0.18f, 0.88f)); UIFactory.Place(obj, new Vector2(0.01f, 0.62f), new Vector2(0.30f, 0.93f));
            progress = Txt(obj, prog, 18, Brand.Muted); UIFactory.Place(progress.rectTransform, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.96f));
            objTitle = Txt(obj, objT, 26, Brand.TealLight, TextAlignmentOptions.TopLeft, FontStyles.Bold); UIFactory.Place(objTitle.rectTransform, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.83f));
            objBody = Txt(obj, objB, 19, Brand.White); UIFactory.Place(objBody.rectTransform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.62f));

            var conv = UIFactory.Panel(root, "Conversation", new Color(0.04f, 0.10f, 0.18f, 0.85f)); UIFactory.Place(conv, new Vector2(0.25f, 0.02f), new Vector2(0.75f, 0.22f));
            transcript = Txt(conv, "", 21, Brand.White); UIFactory.Stretch(transcript.rectTransform, 14); transcript.alignment = TextAlignmentOptions.BottomLeft;
            RenderTranscript();

            var st = UIFactory.Panel(root, "Status", new Color(0, 0, 0, 0.55f)); UIFactory.Place(st, new Vector2(0.70f, 0.95f), new Vector2(0.99f, 1f));
            status = Txt(st, stat, 17, statColor, TextAlignmentOptions.Center); UIFactory.Stretch(status.rectTransform, 4);
            mic = Txt(root, micText, 18, Brand.Muted, TextAlignmentOptions.Center); UIFactory.Place(mic.rectTransform, new Vector2(0.30f, 0.225f), new Vector2(0.70f, 0.26f));

            BuildPause(); BuildDebrief();
            if (!desktop) SurgeryCanvas.PlaceInFront(canvas, Camera.main, 1.5f, 0.15f);
        }

        void BuildPause()
        {
            pause = UIFactory.Panel(root, "Pause", new Color(0.03f, 0.08f, 0.15f, 0.96f)); UIFactory.Place(pause, new Vector2(0.35f, 0.25f), new Vector2(0.65f, 0.80f));
            var v = UIFactory.VStack(pause, "Items", 12, 24); UIFactory.Stretch(v);
            Txt(v, L("Paused", "إيقاف مؤقت"), 32, Brand.White, TextAlignmentOptions.Center, FontStyles.Bold);
            UIFactory.Button(v, L("Resume", "متابعة"), () => ShowPause(false), Brand.Teal, 24);
            UIFactory.Button(v, L("Restart scenario", "إعادة السيناريو"), () => { ShowPause(false); RestartRequested?.Invoke(); }, null, 24);
            UIFactory.Button(v, Language == "ar" ? "English" : "العربية", () => LanguageToggled?.Invoke(), null, 24);
            UIFactory.Button(v, L("Microphone: push-to-talk / hands-free (M)", "المايك: اضغط وتكلم / بدون إيدين (M)"), () => MicModeToggled?.Invoke(), null, 22);
            UIFactory.Button(v, L("Finish and see debrief", "إنهاء وعرض التقييم"), () => { ShowPause(false); FinishRequested?.Invoke(); }, null, 24);
            UIFactory.Button(v, L("Return to scenario menu", "الرجوع لقائمة السيناريوهات"), () => MenuRequested?.Invoke(), Brand.NavyLight, 24);
            pause.gameObject.SetActive(false);
        }

        void BuildDebrief()
        {
            debrief = UIFactory.Panel(root, "Debrief", new Color(0.03f, 0.08f, 0.15f, 0.97f)); UIFactory.Place(debrief, new Vector2(0.15f, 0.05f), new Vector2(0.85f, 0.93f));
            var scroll = UIFactory.ScrollArea(debrief, out var content); UIFactory.Place(scroll, new Vector2(0.03f, 0.14f), new Vector2(0.97f, 0.97f));
            debriefText = Txt(content, "", 20, Brand.White);
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup)).GetComponent<RectTransform>(); bar.SetParent(debrief, false);
            UIFactory.Place(bar, new Vector2(0.1f, 0.02f), new Vector2(0.9f, 0.12f));
            var h = bar.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>(); h.spacing = 16; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            UIFactory.Button(bar, L("Start a new session", "جلسة جديدة"), () => { debrief.gameObject.SetActive(false); RestartRequested?.Invoke(); }, Brand.Teal, 24);
            UIFactory.Button(bar, L("Scenario menu", "قائمة السيناريوهات"), () => MenuRequested?.Invoke(), Brand.NavyLight, 24);
            debrief.gameObject.SetActive(false);
        }

        void Update()
        {
            bool d = SurgeryCanvas.IsDesktop; if (d != desktop) { desktop = d; Build(); }
            bool toggle = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool menuDown = l.isValid && l.TryGetFeatureValue(CommonUsages.menuButton, out var m) && m;
            if (menuDown && !menuWasDown) toggle = true; menuWasDown = menuDown;
            if (toggle && debrief && !debrief.gameObject.activeSelf) ShowPause(!pause.gameObject.activeSelf);
        }

        public bool Paused => pause && pause.gameObject.activeSelf;
        public void ShowPause(bool on)
        {
            if (!pause) return; pause.gameObject.SetActive(on);
            if (desktop) { Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = on; }
            if (on && !desktop) SurgeryCanvas.PlaceInFront(canvas, Camera.main, 1.3f, 0.1f);
        }

        public void SetBanner(string text) { bannerText = text; if (banner) Set(banner, string.IsNullOrEmpty(text) ? "" : "⚠ " + text); if (banner) banner.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(text)); }
        public void SetObjective(string title, string body, int index, int total)
        {
            objT = title; objB = body; prog = total > 0 ? L($"Step {Mathf.Min(index + 1, total)} of {total}", $"الخطوة {Mathf.Min(index + 1, total)} من {total}") : "";
            Set(objTitle, objT); Set(objBody, objB); Set(progress, prog);
        }
        public void SetStatus(string text, Color c) { stat = text; statColor = c; if (status) { Set(status, text); status.color = c; } }
        public void SetMic(string text) { micText = text; if (mic) Set(mic, text); }
        public void AddLine(string speaker, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lines.Add($"<b>{speaker}:</b> {text}"); while (lines.Count > 4) lines.RemoveAt(0);
            RenderTranscript();
        }
        void RenderTranscript()
        {
            if (!transcript) return;
            // Shape each line separately so mixed Arabic/English lines stay readable.
            var shaped = new List<string>(); foreach (var x in lines) shaped.Add(ArabicShaper.ContainsArabic(x) ? ArabicShaper.PrepareRtl(x) : x);
            transcript.text = string.Join("\n", shaped);
        }
        public void ShowDebrief(string text)
        {
            if (!debrief) return; ShowPause(false); debrief.gameObject.SetActive(true); Set(debriefText, text);
            if (desktop) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else SurgeryCanvas.PlaceInFront(canvas, Camera.main, 1.4f, 0.05f);
        }
        public void HideDebrief() { if (debrief) debrief.gameObject.SetActive(false); }
    }
}
