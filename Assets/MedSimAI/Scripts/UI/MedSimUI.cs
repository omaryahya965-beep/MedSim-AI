using System.Collections.Generic;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Assessment;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MedSimAI.UI
{
    /// <summary>
    /// Builds and binds all world-space UI: menu/loading/pause/debrief board, bedside clinical tablet,
    /// lazy-follow subtitle bar with voice indicator. Information lives on physical surfaces where possible.
    /// </summary>
    public class MedSimUI : MonoBehaviour
    {
        public Transform menuAnchor, tabletAnchor, playerHead;
        public Texture2D logo;

        Canvas menuCanvas, tabletCanvas, subtitleCanvas;
        RectTransform loadingPanel, mainPanel, pausePanel, debriefPanel, settingsPanel, comfortPanel; RectTransform comfortReturn;
        TMP_Text heightText, gfxText, perfText;
        TMP_Text speakerText, langText, debriefText, debriefHeader, subtitleText, voiceText, stageText, progressText, findingsText, modeText, toastText;
        RectTransform tabPatient, tabSupervisor, tabInvestigations, tabFindings, tabProgress;
        Image voiceDot;
        readonly List<string> findings = new List<string>();
        float toastUntil;

        static readonly string[] PatientQuestions =
        {
            "Hello, I'm a medical student working with the team today.", "Can you confirm your full name for me?", "Is it okay if I ask some questions and examine you?",
            "When did the pain start?", "Can you describe what the pain feels like?", "Where exactly is the pain?", "Does the pain spread anywhere?",
            "How bad is the pain, out of ten?", "Any other symptoms, like sweating, nausea or breathlessness?", "Is the pain constant, or does it come and go?",
            "Have you had anything like this before?", "Do you have any medical conditions?", "Do you take any medications?", "Do you have any allergies?",
            "Do you smoke?", "Does heart disease run in your family?", "Do you drink alcohol?", "What do you do for work?"
        };
        static readonly (string label, string utterance)[] Investigations =
        {
            ("12-lead ECG", "I'd like a 12-lead ECG please."), ("Troponin", "Can we send a high-sensitivity troponin?"), ("Chest X-ray", "Can I get a portable chest x-ray?"),
            ("Blood glucose", "Can we check a capillary blood glucose?"), ("Echocardiogram", "Can we get an echocardiogram?")
        };
        static readonly string[] Summaries =
        {
            "He has ongoing central crushing chest pain radiating to the arm and jaw, with risk factors, and the ECG shows ST elevation in the inferior leads. I think this is an inferior STEMI and I want to escalate to the senior team and cardiology for urgent reperfusion now.",
            "His chest pain sounds musculoskeletal to me. I'd like to wait for the troponin result before deciding anything further."
        };

        SessionManager S => SessionManager.Instance;
        ClinicalSession Session => ClinicalScenarioManager.Instance.Session;

        void Start()
        {
            BuildMenu(); BuildTablet(); BuildSubtitles();
            S.StateChanged += OnAppState; S.DebriefReady += OnDebrief;
            CharacterSpeechController.SubtitleStarted += OnSubtitleStarted; // unsubscribed in OnDestroy (static event outlives the scene)
            ClinicalFeedbackBus.Finding += (title, text) => { findings.Insert(0, $"<b>{title}</b>\n{text}"); RefreshFindings(); Toast($"<b>{title}</b>  {text}"); };
            var v = VoiceInteractionManager.Instance;
            v.StateChanged += _ => RefreshVoice(); v.TargetChanged += _ => RefreshVoice();
            v.TranscriptReady += ShowTranscript;
            AIProviderManager.Instance.LanguageChanged += _ => { voiceHint = true; RefreshVoice(); RefreshLang(); if (languageButtonText) languageButtonText.text = LanguageButtonLabel(); };
            v.Error += e => Toast("<color=#FFB74D>" + e + "</color>");
            v.TurnReady += OnTurn;
            v.MicModeChanged += _ => { if (micButtonText) micButtonText.text = MicLabel(); RefreshVoice(); };
            if (!string.IsNullOrEmpty(MedSimAI.XR.DisplayModeManager.RecoveryNotice)) { Toast("<color=#FFB74D>" + MedSimAI.XR.DisplayModeManager.RecoveryNotice + "</color>"); toastUntil = Time.unscaledTime + 25f; }
            AIProviderManager.Instance.ModeChanged += _ => RefreshMode();
            Session.Stages.StageChanged += (_, __) => RefreshProgress();
            Session.Tracker.Recorded += _ => RefreshProgress();
            ClinicalScenarioManager.Instance.SessionReset += () => { findings.Clear(); RefreshFindings(); RefreshProgress(); subtitleText.text = ""; };
            foreach (var ch in FindObjectsByType<MedSimAI.Characters.SimCharacterBase>(FindObjectsSortMode.None))
                if (ch.speech) ConversationIndicator.Create(ch.speech, ch.Focus, playerHead, null);
            RefreshVoice(); RefreshMode(); RefreshLang(); RefreshProgress(); OnAppState(S.State);
            AIProviderManager.Instance.ModeChanged += _ => RefreshLang();
        }

        // ---------------- Menu board ----------------
        void BuildMenu()
        {
            menuCanvas = UIFactory.WorldCanvas("Menu Board", menuAnchor, new Vector2(1400, 860), 0.00085f);
            var root = UIFactory.Panel(menuCanvas.transform, "Board", Brand.Navy);
            var banner = UIFactory.Panel(root, "Disclaimer", new Color(1f, 0.72f, 0.3f, 0.95f), null);
            UIFactory.Place(banner, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 52));
            var bt = UIFactory.Text(banner, ClinicalScenarioManager.Instance.Scenario.ValidationBanner, 24, Brand.Navy, TextAlignmentOptions.Center, FontStyles.Bold); UIFactory.Stretch(bt.rectTransform, 6);

            loadingPanel = Section(root, "Loading");
            var lt = UIFactory.Text(loadingPanel, "<size=140%><b>MedSim <color=#64FFDA>AI</color></b></size>\n<color=#8892B0>Immersive clinical training</color>\n\n<size=70%>Preparing emergency department…</size>", 64, Brand.White, TextAlignmentOptions.Center);
            UIFactory.Stretch(lt.rectTransform, 40);
            AddLogo(loadingPanel, new Vector2(0.5f, 0.82f), 200);

            mainPanel = Section(root, "Main");
            AddLogo(mainPanel, new Vector2(0.12f, 0.84f), 150);
            var title = UIFactory.Text(mainPanel, "<b>MedSim <color=#64FFDA>AI</color></b>\n<size=50%><color=#8892B0>Emergency Room — Chest Pain Assessment · ER_CHEST_PAIN_001 v" + ClinicalScenarioManager.Instance.Scenario.version + "</color></size>", 64, Brand.White);
            UIFactory.Place(title.rectTransform, new Vector2(0.22f, 0.72f), new Vector2(0.98f, 0.97f));
            var col = UIFactory.VStack(mainPanel, "Buttons", 14, 0); UIFactory.Place(col, new Vector2(0.06f, 0.12f), new Vector2(0.46f, 0.68f));
            UIFactory.Button(col, "Start Clinical Session", () => S.StartClinicalSession(false), Brand.Teal, 30, 86);
            UIFactory.Button(col, "Training Mode (guided hints)", () => S.StartClinicalSession(true), Brand.NavyLight, 26, 76);
            UIFactory.Button(col, "Voice & AI Settings", () => Show(settingsPanel), Brand.NavyLight, 26, 76);
            UIFactory.Button(col, "Comfort & Graphics", () => { comfortReturn = mainPanel; Show(comfortPanel); }, Brand.NavyLight, 26, 76);
            UIFactory.Button(col, "Exit Simulation", () => S.ExitSimulation(), Brand.NavyLight, 26, 76);
            var controls = UIFactory.Text(mainPanel, "<b><color=#64FFDA>Controls</color></b>\n• <b>Grip</b> — pick up instruments\n• <b>Trigger</b> — press buttons / menus\n• <b>A / X (hold)</b> — push-to-talk\n• <b>Thumbstick</b> — teleport (forward) · snap turn (left/right)\n• <b>Menu</b> — pause\n\nLook at the person you want to talk to. Use the bedside tablet for questions, investigations, hints and escalation.\n\n<size=80%><color=#8892B0>Fictional patient. No real patient data is collected. Microphone audio is processed in memory only.</color></size>", 25, Brand.White);
            UIFactory.Place(controls.rectTransform, new Vector2(0.52f, 0.1f), new Vector2(0.96f, 0.68f));

            BuildComfortPanel(root);
            settingsPanel = Section(root, "Settings");
            var sc = UIFactory.VStack(settingsPanel, "Settings", 14, 40);
            UIFactory.Stretch(sc, 20);
            UIFactory.Text(sc, "<b>Voice & AI Settings</b>", 44, Brand.White);
            modeText = UIFactory.Text(sc, "", 26, Brand.TealLight);
            UIFactory.Button(sc, "Offline scripted demo (no network)", () => AIProviderManager.Instance.SetMode(ConversationMode.OfflineScripted), Brand.NavyLight, 24, 66);
            UIFactory.Button(sc, "Live AI via secure proxy", () => { AIProviderManager.Instance.SetMode(ConversationMode.OnlineAI); if (!AIProviderManager.Instance.OnlineAvailable) Toast("No proxy configured — see AI_SETUP.md. Staying offline."); }, Brand.NavyLight, 24, 66);
            UIFactory.Button(sc, "Talk target: automatic (gaze)", () => VoiceInteractionManager.Instance.autoTargetByGaze = true, Brand.NavyLight, 24, 66);
            langText = UIFactory.Text(sc, "", 22, Brand.Muted);
            UIFactory.Button(sc, "Language: English", () => { AIProviderManager.Instance.SetLanguage("en"); RefreshMode(); }, Brand.NavyLight, 24, 60);
            UIFactory.Button(sc, "Language: Arabic (draft translation)", () => { AIProviderManager.Instance.SetLanguage("ar"); RefreshMode(); }, Brand.NavyLight, 24, 60);
            UIFactory.Button(sc, "Back", () => Show(mainPanel), Brand.Teal, 26, 66);

            pausePanel = Section(root, "Pause");
            var pTitle = UIFactory.Text(pausePanel, "<b>Paused</b>", 44, Brand.White, TextAlignmentOptions.Center); UIFactory.Place(pTitle.rectTransform, new Vector2(0.2f, 0.89f), new Vector2(0.8f, 0.99f));
            var pc = UIFactory.VStack(pausePanel, "Pause", 7, 0); UIFactory.Place(pc, new Vector2(0.22f, 0.03f), new Vector2(0.78f, 0.89f));
            pauseStatusText = UIFactory.Text(pc, "", 22, Brand.TealLight, TextAlignmentOptions.Center);
            UIFactory.Button(pc, "Resume", () => S.Pause(false), Brand.Teal, 26, 58);
            UIFactory.Button(pc, "Show / hide clinical tablet", () => { TabletEnabled = !TabletEnabled; OnAppState(S.State); }, Brand.NavyLight, 22, 50);
            UIFactory.Button(pc, "Subtitles on / off", () => { SubtitlesEnabled = !SubtitlesEnabled; }, Brand.NavyLight, 22, 50);
            languageButtonText = UIFactory.Button(pc, LanguageButtonLabel(), () => { var ai = AIProviderManager.Instance; ai.SetLanguageMode((LanguageMode)(((int)ai.LangMode + 1) % 3)); if (languageButtonText) languageButtonText.text = LanguageButtonLabel(); RefreshMode(); RefreshPause(); }, Brand.NavyLight, 20, 50).GetComponentInChildren<TMP_Text>();
            {
                // VR height directly in the pause menu (saved; applies immediately; Desktop Mode height is unchanged)
                var pl = FindAnyObjectByType<MedSimAI.XR.XRPlayerController>();
                var hrow = new GameObject("HeightRow", typeof(RectTransform)).GetComponent<RectTransform>(); hrow.SetParent(pc, false);
                var hl = hrow.gameObject.AddComponent<HorizontalLayoutGroup>(); hl.spacing = 8; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = true;
                hrow.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
                UIFactory.Button(hrow, "Lower −5", () => pl?.AdjustHeight(-0.05f), Brand.NavyLight, 20, 48);
                pauseHeightText = UIFactory.Button(hrow, pl ? $"VR height +{pl.extraHeight * 100:0} cm" : "VR height", () => pl?.ResetHeight(), Brand.NavyLight, 20, 48).GetComponentInChildren<TMP_Text>();
                UIFactory.Button(hrow, "Higher +5", () => pl?.AdjustHeight(0.05f), Brand.NavyLight, 20, 48);
            }
            UIFactory.Button(pc, "Comfort, movement & graphics", () => { comfortReturn = pausePanel; Show(comfortPanel); }, Brand.NavyLight, 22, 50);
            micButtonText = UIFactory.Button(pc, MicLabel(), () => { VoiceInteractionManager.Instance.CycleMicMode(); if (micButtonText) micButtonText.text = MicLabel(); RefreshVoice(); }, Brand.NavyLight, 20, 48).GetComponentInChildren<TMP_Text>();
            micDeviceButtonText = UIFactory.Button(pc, MicDeviceLabel(), () => { CycleMicDevice(); if (micDeviceButtonText) micDeviceButtonText.text = MicDeviceLabel(); }, Brand.NavyLight, 18, 46).GetComponentInChildren<TMP_Text>();
            displayModeButtonText = UIFactory.Button(pc, DisplayModeLabel(), () => { MedSimAI.XR.DisplayModeManager.CyclePreference(); if (displayModeButtonText) displayModeButtonText.text = DisplayModeLabel(); }, Brand.NavyLight, 20, 48).GetComponentInChildren<TMP_Text>();
            UIFactory.Button(pc, "Restart Scenario", () => S.Restart(), Brand.NavyLight, 22, 50);
            UIFactory.Button(pc, "Exit Simulation", () => S.ExitSimulation(), Brand.NavyLight, 22, 50);

            debriefPanel = Section(root, "Debrief");
            debriefHeader = UIFactory.Text(debriefPanel, "", 40, Brand.White); UIFactory.Place(debriefHeader.rectTransform, new Vector2(0.03f, 0.86f), new Vector2(0.97f, 0.98f));
            var scroll = UIFactory.ScrollArea(debriefPanel, out var content); UIFactory.Place(scroll, new Vector2(0.03f, 0.16f), new Vector2(0.97f, 0.85f));
            debriefText = UIFactory.Text(content, "", 23, Brand.White);
            debriefText.gameObject.AddComponent<LayoutElement>();
            var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>(); row.SetParent(debriefPanel, false);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 20; h.childForceExpandWidth = true; h.childControlWidth = true; h.childControlHeight = true;
            UIFactory.Place(row, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.14f));
            UIFactory.Button(row, "Restart & Compare", () => S.Restart(), Brand.Teal, 26, 70);
            UIFactory.Button(row, "Main Menu", () => S.ReturnToMenu(), Brand.NavyLight, 26, 70);
        }

        // ---------------- Comfort & Graphics ----------------
        void BuildComfortPanel(RectTransform root)
        {
            comfortPanel = Section(root, "Comfort");
            var player = FindAnyObjectByType<MedSimAI.XR.XRPlayerController>();
            var left = UIFactory.VStack(comfortPanel, "Height", 12, 0); UIFactory.Place(left, new Vector2(0.04f, 0.14f), new Vector2(0.48f, 0.95f));
            UIFactory.Text(left, "<b>Player height</b>", 40, Brand.White);
            heightText = UIFactory.Text(left, "", 26, Brand.TealLight);
            var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>(); row.SetParent(left, false);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 12; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 76;
            UIFactory.Button(row, "Lower  -5 cm", () => player?.AdjustHeight(-0.05f), Brand.NavyLight, 24, 76);
            UIFactory.Button(row, "Higher  +5 cm", () => player?.AdjustHeight(0.05f), Brand.NavyLight, 24, 76);
            UIFactory.Button(left, "Standing mode", () => player?.SetSeated(false), Brand.NavyLight, 24, 68);
            UIFactory.Button(left, "Seated mode (+45 cm eye lift)", () => player?.SetSeated(true), Brand.NavyLight, 24, 68);
            UIFactory.Button(left, "Reset to default (+55 cm)", () => player?.ResetHeight(), Brand.NavyLight, 24, 68);
            var loco = player ? player.GetComponent<MedSimAI.XR.VRLocomotion>() : null;
            if (loco)
            {
                UIFactory.Text(left, "<b>Movement (VR)</b>", 30, Brand.White);
                moveText = UIFactory.Button(left, loco.MoveLabel, () => loco.CycleSpeed(), Brand.NavyLight, 22, 56).GetComponentInChildren<TMP_Text>();
                UIFactory.Button(left, "Smooth move on / off (teleport only)", () => loco.ToggleSmoothMove(), Brand.NavyLight, 22, 56);
                turnText = UIFactory.Button(left, loco.TurnLabel, () => loco.ToggleSmoothTurn(), Brand.NavyLight, 22, 56).GetComponentInChildren<TMP_Text>();
                UIFactory.Button(left, "Snap angle / turn speed", () => { if (loco.SmoothTurn) loco.CycleTurnSpeed(); else loco.CycleSnapAngle(); }, Brand.NavyLight, 22, 56);
                deadText = UIFactory.Button(left, loco.DeadZoneLabel, () => loco.CycleDeadZone(), Brand.NavyLight, 22, 56).GetComponentInChildren<TMP_Text>();
                loco.Changed += () => { if (moveText) moveText.text = loco.MoveLabel; if (turnText) turnText.text = loco.TurnLabel; if (deadText) deadText.text = loco.DeadZoneLabel; };
            }
            UIFactory.Text(left, "<color=#8892B0>Raises the headset and both controllers together. Saved on this device. The hospital is never rescaled.</color>", 20, Brand.Muted);

            var right = UIFactory.VStack(comfortPanel, "Graphics", 12, 0); UIFactory.Place(right, new Vector2(0.52f, 0.14f), new Vector2(0.96f, 0.95f));
            UIFactory.Text(right, "<b>Graphics</b>", 40, Brand.White);
            gfxText = UIFactory.Text(right, "", 26, Brand.TealLight);
            foreach (var p in new[] { MedSimAI.Core.GraphicsProfile.Performance, MedSimAI.Core.GraphicsProfile.Balanced, MedSimAI.Core.GraphicsProfile.HighQuality })
            {
                var pp = p; string label = p == MedSimAI.Core.GraphicsProfile.HighQuality ? "High Quality" : p.ToString();
                UIFactory.Button(right, label, () => MedSimAI.Core.PerformanceManager.Instance?.Apply(pp), Brand.NavyLight, 24, 68);
            }
            perfText = UIFactory.Text(right, "", 20, Brand.Muted);
            UIFactory.Button(comfortPanel, "Back", () => Show(comfortReturn ? comfortReturn : mainPanel), Brand.Teal, 26, 70).GetComponent<RectTransform>().Also(r => UIFactory.Place(r, new Vector2(0.38f, 0.02f), new Vector2(0.62f, 0.11f)));
            if (player) player.HeightChanged += (_, __) => RefreshComfort();
            if (MedSimAI.Core.PerformanceManager.Instance) MedSimAI.Core.PerformanceManager.Instance.ProfileChanged += _ => RefreshComfort();
            RefreshComfort();
        }

        TMP_Text moveText, turnText, deadText, pauseHeightText;
        void RefreshComfort()
        {
            { var pl = FindAnyObjectByType<MedSimAI.XR.XRPlayerController>(); if (pauseHeightText && pl) pauseHeightText.text = $"VR height +{pl.extraHeight * 100:0} cm"; }
            var player = FindAnyObjectByType<MedSimAI.XR.XRPlayerController>();
            if (heightText && player) heightText.text = $"Extra height: <b>+{player.extraHeight * 100:0} cm</b> · {(player.Seated ? "Seated" : "Standing")}";
            var pm = MedSimAI.Core.PerformanceManager.Instance;
            if (gfxText && pm) { var s = pm.ActiveSettings; gfxText.text = $"Profile: <b>{s.label}</b>\n<size=80%>Render scale {s.renderScale:0.0}× · MSAA {s.msaa}× · shadows {(s.softShadows ? "soft" : "hard")}</size>"; }
        }

        RectTransform Section(RectTransform root, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(root, false);
            var rt = (RectTransform)go.transform; UIFactory.Place(rt, Vector2.zero, Vector2.one, new Vector2(0, 52), Vector2.zero);
            go.SetActive(false); return rt;
        }

        void AddLogo(RectTransform parent, Vector2 anchor, float size)
        {
            if (!logo) return;
            var go = new GameObject("Logo", typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(parent, false);
            go.GetComponent<RawImage>().texture = logo;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = anchor; rt.sizeDelta = Vector2.one * size;
        }

        /// <summary>
        /// Desktop Mode: the menu/pause/debrief board and the subtitles become screen-space overlays (crisp at native
        /// resolution, clickable with the mouse). VR keeps them as world-space panels.
        /// </summary>
        public void SetDesktopMode()
        {
            foreach (var c in new[] { menuCanvas, subtitleCanvas })
            {
                if (!c) continue;
                var size = ((RectTransform)c.transform).sizeDelta;
                c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = c == menuCanvas ? 20 : 10;
                var scaler = c.GetComponent<CanvasScaler>() ?? c.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
                if (!c.GetComponent<GraphicRaycaster>()) c.gameObject.AddComponent<GraphicRaycaster>();
                foreach (Transform ch in c.transform)
                {
                    if (!(ch is RectTransform rt)) continue;
                    if (c == subtitleCanvas && rt.name != "Bar") continue;
                    rt.anchorMin = rt.anchorMax = c == subtitleCanvas ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
                    rt.pivot = c == subtitleCanvas ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = c == subtitleCanvas ? new Vector2(1060, PanelH) : size; rt.anchoredPosition = c == subtitleCanvas ? new Vector2(0, 40) : Vector2.zero;
                    rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
                }
            }
            desktopUI = true;
        }
        bool desktopUI;
        public bool MenuVisible => menuCanvas && menuCanvas.gameObject.activeInHierarchy;

        /// <summary>The clinical tablet is an optional secondary interface (voice and physical actions are primary).</summary>
        public static bool TabletEnabled;
        public static bool SubtitlesEnabled = true;
        TMP_Text pauseStatusText, displayModeButtonText, micButtonText, micDeviceButtonText;
        static string MicDeviceLabel() { var d = PlayerPrefs.GetString(VoiceInteractionManager.MicDevicePref, ""); return "Mic device: " + (string.IsNullOrEmpty(d) ? "automatic (headset in VR, system default on desktop)" : d); }
        static void CycleMicDevice()
        {
            var devs = Microphone.devices; var cur = PlayerPrefs.GetString(VoiceInteractionManager.MicDevicePref, "");
            int i = string.IsNullOrEmpty(cur) ? -1 : System.Array.IndexOf(devs, cur);
            PlayerPrefs.SetString(VoiceInteractionManager.MicDevicePref, i + 1 < devs.Length ? devs[i + 1] : ""); PlayerPrefs.Save();
            VoiceInteractionManager.Instance.ReopenMic();
        }
        static string MicLabel() { var m = VoiceInteractionManager.Instance ? VoiceInteractionManager.Instance.Mic : MicMode.PushToTalk; return "Microphone: " + (m == MicMode.HandsFree ? "hands-free (just speak)" : m == MicMode.Muted ? "muted" : "push-to-talk"); }
        static string DisplayModeLabel() => $"Display: now {MedSimAI.XR.DisplayModeManager.Mode} · next launch {MedSimAI.XR.DisplayModeManager.SavedPreference}";
        void RefreshPause()
        {
            if (!pauseStatusText) return;
            if (micButtonText) micButtonText.text = MicLabel();
            if (micDeviceButtonText) micDeviceButtonText.text = MicDeviceLabel();
            var ai = AIProviderManager.Instance;
            pauseStatusText.text = $"{ai.ModeLabel}  ·  {(ai.Config.language == "ar" ? "العربية" : "English")}  ·  {(S.trainingMode ? "Training" : "Assessment")}\n<size=80%><color=#FFB74D>{ClinicalScenarioManager.Instance.Scenario.ValidationBanner}</color></size>";
        }

        void Show(RectTransform panel)
        {
            foreach (var p in new[] { loadingPanel, mainPanel, pausePanel, debriefPanel, settingsPanel, comfortPanel }) p.gameObject.SetActive(p == panel);
            menuCanvas.gameObject.SetActive(panel != null);
        }

        void OnAppState(AppState s)
        {
            switch (s)
            {
                case AppState.Loading: Show(loadingPanel); break;
                case AppState.MainMenu: Show(mainPanel); break;
                case AppState.Paused: Show(pausePanel); RefreshPause(); break;
                case AppState.Debrief: Show(debriefPanel); break;
                default: Show(null); break;
            }
            tabletCanvas.gameObject.SetActive(TabletEnabled && (s == AppState.Running || s == AppState.Tutorial || s == AppState.Discussion));
            RefreshProgress();
        }

        void OnDebrief(AssessmentResult r, string text)
        {
            debriefHeader.text = $"<b>Debrief</b>  <size=60%><color=#8892B0>{r.scenarioId} v{r.scenarioVersion} · {r.reviewStatus}</color></size>";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b><color=#64FFDA>Objectives</color></b>");
            foreach (var o in r.objectives) sb.AppendLine($"{(o.complete ? "<color=#64FFDA>•</color>" : o.met > 0 ? "<color=#FFB74D>•</color>" : "<color=#FF8A80>•</color>")} {o.text}  <color=#8892B0>({o.met}/{o.total})</color>");
            sb.AppendLine();
            sb.AppendLine("<b><color=#64FFDA>Checklist</color></b>");
            foreach (var c in r.criteria) sb.AppendLine($"{(c.met ? "<color=#64FFDA>•</color>" : "<color=#FF8A80>•</color>")} {c.description}{(c.critical ? " <color=#FFB74D>[safety-critical]</color>" : "")}{(c.met ? $" <color=#8892B0>@ {c.time:0}s</color>" : "")}{(c.viaAIIntent ? " <color=#42A5F5>[AI-interpreted — for instructor review]</color>" : "")}  <size=80%><color=#8892B0>{c.category}</color></size>");
            sb.AppendLine();
            sb.Append(text);
            debriefText.text = sb.ToString();
        }

        // ---------------- Bedside tablet ----------------
        void BuildTablet()
        {
            tabletCanvas = UIFactory.WorldCanvas("Clinical Tablet", tabletAnchor, new Vector2(620, 420), 0.00075f);
            var root = UIFactory.Panel(tabletCanvas.transform, "Tablet", Brand.Navy);
            var header = UIFactory.Panel(root, "Header", Brand.NavyLight, null, false); UIFactory.Place(header, new Vector2(0, 1), Vector2.one, new Vector2(0, -56), Vector2.zero);
            stageText = UIFactory.Text(header, "", 20, Brand.White, TextAlignmentOptions.MidlineLeft); UIFactory.Stretch(stageText.rectTransform, 10);
            var tabs = new GameObject("Tabs", typeof(RectTransform)).GetComponent<RectTransform>(); tabs.SetParent(root, false);
            var hl = tabs.gameObject.AddComponent<HorizontalLayoutGroup>(); hl.spacing = 6; hl.padding = new RectOffset(8, 8, 4, 4); hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = true;
            UIFactory.Place(tabs, new Vector2(0, 1), Vector2.one, new Vector2(0, -112), new Vector2(0, -58));
            var body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>(); body.SetParent(root, false);
            UIFactory.Place(body, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -116));

            tabPatient = TabBody(body); tabSupervisor = TabBody(body); tabInvestigations = TabBody(body); tabFindings = TabBody(body); tabProgress = TabBody(body);
            void Tab(string n, RectTransform t) => UIFactory.Button(tabs, n, () => ShowTab(t), Brand.NavyLight, 17, 48);
            Tab("Patient", tabPatient); Tab("Supervisor", tabSupervisor); Tab("Tests", tabInvestigations); Tab("Findings", tabFindings); Tab("Progress", tabProgress);

            UIFactory.ScrollArea(tabPatient, out var pq);
            foreach (var q in PatientQuestions) { var qq = q; UIFactory.Button(pq, qq, () => Ask(Persona.Patient, qq), Brand.NavyLight, 17, 50); }

            var sv = UIFactory.VStack(tabSupervisor, "Sup", 8, 4); UIFactory.Stretch(sv);
            UIFactory.Button(sv, "Ask for a hint", () => S.RequestHint(), Brand.NavyLight, 18, 52);
            UIFactory.Button(sv, "Escalate to senior / cardiology now", () => S.EscalateToSenior(), new Color32(150, 40, 40, 255), 18, 52);
            UIFactory.Button(sv, "I'm ready to summarise my findings", () => S.BeginReasoning(), Brand.NavyLight, 18, 52);
            foreach (var s in Summaries) { var ss = s; UIFactory.Button(sv, "Summary: \"" + ss.Substring(0, 52) + "…\"", () => Ask(Persona.Instructor, ss), Brand.NavyLight, 15, 52); }
            UIFactory.Button(sv, "Finish case & debrief", () => S.FinishCase(), Brand.Teal, 18, 52);

            var ig = UIFactory.VStack(tabInvestigations, "Inv", 8, 4); UIFactory.Stretch(ig);
            foreach (var (label, utt) in Investigations) { var u = utt; UIFactory.Button(ig, "Request: " + label, () => Ask(Persona.Instructor, u), Brand.NavyLight, 18, 52); }

            UIFactory.ScrollArea(tabFindings, out var fc);
            findingsText = UIFactory.Text(fc, "", 17, Brand.White); findingsText.gameObject.AddComponent<LayoutElement>();
            UIFactory.ScrollArea(tabProgress, out var pc);
            progressText = UIFactory.Text(pc, "", 17, Brand.White); progressText.gameObject.AddComponent<LayoutElement>();
            ShowTab(tabPatient);
        }

        RectTransform TabBody(RectTransform body)
        {
            var go = new GameObject("TabBody", typeof(RectTransform)); go.transform.SetParent(body, false);
            var rt = (RectTransform)go.transform; UIFactory.Stretch(rt); return rt;
        }

        void ShowTab(RectTransform t) { foreach (var x in new[] { tabPatient, tabSupervisor, tabInvestigations, tabFindings, tabProgress }) x.gameObject.SetActive(x == t); }

        void Ask(Persona p, string text)
        {
            voiceText.text = $"<color=#8892B0>You → {(p == Persona.Patient ? "patient" : "supervisor")}:</color> {text}";
            _ = VoiceInteractionManager.Instance.Submit(p, text);
        }

        void OnTurn(ConversationTurn t)
        {
            if (t.investigation != null)
            {
                if (t.investigation.id == "INV_ECG" && S.ecg && t.investigation.available)
                    S.ecg.ShowResult(new InvestigationResult { success = true, available = true, investigationId = t.investigation.id, name = t.investigation.name, result = t.investigation.result, imageKey = t.investigation.imageKey });
                else ClinicalFeedbackBus.Publish(t.investigation.name, t.investigation.result);
            }
            if (t.source == ResponseSource.SafetyGuard || t.source == ResponseSource.AIRejectedFallback || t.source == ResponseSource.ProviderFailureFallback)
                Debug.Log($"[MedSim] Turn source {t.source}: {t.note}");
        }

        void RefreshFindings() { if (findingsText) findingsText.text = findings.Count == 0 ? "<color=#8892B0>No findings yet. Use the instruments or request tests.</color>" : string.Join("\n\n", findings); }

        void RefreshProgress()
        {
            if (!stageText) return;
            var s = Session; var mgr = ClinicalScenarioManager.Instance;
            stageText.text = $"<b>{Pretty(s.Stages.Current)}</b>   <color=#8892B0>{System.TimeSpan.FromSeconds(mgr.Elapsed):mm\\:ss} · {(S.trainingMode ? "Training" : "Assessment")} · </color><color=#FFB74D>DRAFT</color>";
            var sb = new System.Text.StringBuilder("<b>Objectives</b>\n");
            foreach (var o in s.Scenario.objectives)
            {
                var items = s.Scenario.rubric.Where(r => r.objectiveId == o.id).ToList(); int met = items.Count(r => s.Tracker.Has(r.eventId));
                sb.AppendLine($"{(met == items.Count ? "<color=#64FFDA>•</color>" : met > 0 ? "<color=#FFB74D>•</color>" : "<color=#8892B0>•</color>")} {o.text}" + (S.trainingMode ? $" <color=#8892B0>({met}/{items.Count})</color>" : ""));
            }
            sb.AppendLine($"\n<color=#8892B0>Hints used: {s.HintsRequested}</color>");
            progressText.text = sb.ToString();
        }

        static string Pretty(ClinicalStage s) => System.Text.RegularExpressions.Regex.Replace(s.ToString(), "(\\B[A-Z])", " $1");

        // ---------------- Subtitles / voice indicator ----------------
        void BuildSubtitles()
        {
            // Conversation panel (v0.5). Fixed size (never resizes while speech streams in), with measured rows:
            //   header (speaker name + status) · character response (max 3 lines, paged in step with the speech) ·
            //   divider · student transcript (max 2 lines, paged). Text never overlaps and never leaves the panel.
            subtitleCanvas = UIFactory.WorldCanvas("Subtitles", transform, new Vector2(900, PanelH));
            var root = UIFactory.Panel(subtitleCanvas.transform, "Bar", new Color(0.035f, 0.07f, 0.12f, 0.78f));
            RectTransform Row(string n, float top, float height, float left = 24, float right = 24)
            {
                var r = new GameObject(n, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(root, false);
                r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
                r.offsetMin = new Vector2(left, -top - height); r.offsetMax = new Vector2(-right, -top); return r;
            }
            TMP_Text Fit(TMP_Text t, RectTransform into, float size, Color c, TextOverflowModes mode, float lineSpacing = 0)
            {
                t.rectTransform.SetParent(into, false); t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
                t.fontSize = size; t.color = c; t.enableWordWrapping = true; t.overflowMode = mode; t.lineSpacing = lineSpacing; t.enableAutoSizing = false; t.margin = Vector4.zero; return t;
            }
            var header = Row("Header", 12, 30);
            speakerText = Fit(UIFactory.Text(header, "", 20, Brand.TealLight, TextAlignmentOptions.MidlineLeft), header, 20, Brand.TealLight, TextOverflowModes.Ellipsis); speakerText.fontStyle = FontStyles.Bold;
            var statusBox = Row("Status", 12, 30, 24, 24);
            statusText = Fit(UIFactory.Text(statusBox, "", 16, Brand.Muted, TextAlignmentOptions.MidlineRight), statusBox, 16, new Color(0.6f, 0.66f, 0.74f), TextOverflowModes.Ellipsis);
            statusText.alignment = TextAlignmentOptions.MidlineRight;
            var dot = UIFactory.Panel(statusBox, "Dot", Brand.Muted, new Vector2(12, 12)); dot.anchorMin = dot.anchorMax = new Vector2(1, 0.5f); dot.anchoredPosition = new Vector2(16, 0); voiceDot = dot.GetComponent<Image>();
            var body = Row("Response", 48, ResponseH);
            subtitleText = Fit(UIFactory.Text(body, "", 25, Brand.White, TextAlignmentOptions.TopLeft), body, 25, new Color(0.96f, 0.97f, 0.98f), TextOverflowModes.Page, 4);
            var pageBox = Row("Page", 48 + ResponseH - 4, 18);
            pageText = Fit(UIFactory.Text(pageBox, "", 14, Brand.Muted, TextAlignmentOptions.BottomRight), pageBox, 14, new Color(0.55f, 0.6f, 0.68f), TextOverflowModes.Ellipsis);
            pageText.alignment = TextAlignmentOptions.BottomRight;
            var div = UIFactory.Panel(Row("Divider", 48 + ResponseH + 16, 1), "Line", new Color(1, 1, 1, 0.09f)); div.anchorMin = Vector2.zero; div.anchorMax = Vector2.one; div.offsetMin = div.offsetMax = Vector2.zero;
            var student = Row("Student", 48 + ResponseH + 24, StudentH);
            voiceText = Fit(UIFactory.Text(student, "", 18, Brand.Muted, TextAlignmentOptions.TopLeft), student, 18, new Color(0.72f, 0.77f, 0.85f), TextOverflowModes.Page, 2);
            toastText = UIFactory.Text(subtitleCanvas.transform, "", 22, Brand.TealLight, TextAlignmentOptions.Bottom); UIFactory.Place(toastText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 8), new Vector2(0, 120));
        }

        void OnSubtitleStarted(CharacterSpeechController c, string t)
        {
            if (!this || !subtitleText || !speakerText) return;
            bool show = SubtitlesEnabled || AIProviderManager.Instance.Mode != ConversationMode.OnlineAI || AIProviderManager.Instance.Live.Status == LiveStatus.Offline; currentSpeaker = c; bool ar = ArabicShaper.ContainsArabic(t); ArabicShaper.Apply(speakerText, show ? LocalName(c, ar) : ""); subtitleText.pageToDisplay = 1; ArabicShaper.Apply(subtitleText, show ? t : ""); subtitleText.ForceMeshUpdate(); 
        }
        void OnDestroy() { CharacterSpeechController.SubtitleStarted -= OnSubtitleStarted; }

        void Toast(string msg) { if (!toastText) return; toastText.text = msg; toastUntil = Time.unscaledTime + 6f; }

        // ---- conversation panel geometry (canvas units) and paging
        const float ResponseH = 114f, StudentH = 48f, PanelH = 48 + ResponseH + 24 + StudentH + 12; // 234
        TMP_Text statusText, pageText; CharacterSpeechController currentSpeaker; float studentPageAt;
        static string LocalName(CharacterSpeechController c, bool ar) => !ar ? c.displayName : c.persona == Persona.Patient ? "السيد آدم رييس (المريض)" : "د. سارة حسن (المشرفة)";

        /// <summary>Response paging follows the speech: page = progress × pages. Transcript pages rotate every 4 s.</summary>
        void UpdateConversationPaging()
        {
            if (!subtitleText) return;
            int pages = Mathf.Max(1, subtitleText.textInfo.pageCount);
            if (pages > 1)
            {
                float prog = currentSpeaker && currentSpeaker.IsSpeaking ? currentSpeaker.Progress : 1f;
                int page = Mathf.Clamp(1 + (int)(prog * pages * 1.08f), 1, pages); // slightly ahead so reading keeps up with listening
                if (subtitleText.pageToDisplay != page) subtitleText.pageToDisplay = page;
                if (pageText) pageText.text = $"{page}/{pages}";
            }
            else if (pageText && pageText.text.Length > 0) pageText.text = "";
            int sp = Mathf.Max(1, voiceText ? voiceText.textInfo.pageCount : 1);
            if (voiceText && sp > 1 && Time.unscaledTime > studentPageAt) { voiceText.pageToDisplay = voiceText.pageToDisplay % sp + 1; studentPageAt = Time.unscaledTime + 4f; }
        }

        TMP_Text languageButtonText; bool voiceHint = true;
        static string LanguageButtonLabel() => "Language: " + AIProviderManager.Instance.LanguageModeLabel;
        bool ArabicUi => AIProviderManager.Instance && AIProviderManager.Instance.ConversationLanguage == "ar";

        /// <summary>The student's own words (what the Realtime session heard): Arabic is shaped and shown right-to-left.</summary>
        void ShowTranscript(string t)
        {
            voiceHint = false; voiceText.pageToDisplay = 1; studentPageAt = Time.unscaledTime + 4f;
            if (ArabicShaper.ContainsArabic(t)) ArabicShaper.Apply(voiceText, "أنت: " + t);
            else ArabicShaper.Apply(voiceText, $"<color=#8892B0>You:</color> {t}");
        }
        void SetVoiceLine(string en, string ar) { if (ArabicUi && ar != null) ArabicShaper.Apply(voiceText, ar); else ArabicShaper.Apply(voiceText, en); }

        void RefreshVoice()
        {
            var v = VoiceInteractionManager.Instance; if (!voiceDot || v == null) return;
            string who = v.Target == Persona.Patient ? "Patient" : "Supervisor";
            if (statusText)
            {
                bool arS = ArabicUi;
                string st = v.State == VoiceState.Listening ? (arS ? "أستمع إليك…" : "Listening…") : v.State == VoiceState.Thinking || v.State == VoiceState.Transcribing ? (arS ? "يفكّر…" : "Thinking…") : v.State == VoiceState.Speaking ? (arS ? "يتحدّث" : "Speaking") : "";
                ArabicShaper.Apply(statusText, st); statusText.horizontalAlignment = arS ? HorizontalAlignmentOptions.Left : HorizontalAlignmentOptions.Right; // opposite side to the speaker name
            }
            switch (v.State)
            {
                case VoiceState.Listening: voiceDot.color = Brand.Danger; voiceHint = true; SetVoiceLine($"Talking to the {who} — {(v.Mic == MicMode.HandsFree ? "pause to send" : "release to send")}", (v.Target == Persona.Patient ? "تتحدث مع المريض — " : "تتحدث مع الطبيبة — ") + (v.Mic == MicMode.HandsFree ? "توقّف لحظة للإرسال" : "اترك الزر للإرسال")); break;
                case VoiceState.Transcribing: voiceDot.color = Brand.Warn; voiceHint = true; SetVoiceLine("Transcribing…", "…"); break;
                case VoiceState.Thinking: voiceDot.color = Brand.Blue; break;
                case VoiceState.Speaking: voiceDot.color = Brand.Teal; break;
                default: voiceDot.color = Brand.Muted; if (voiceHint || string.IsNullOrEmpty(voiceText.text)) SetVoiceLine((v.LiveOn && v.Mic == MicMode.HandsFree ? $"Just speak to the <b>{who}</b> (or hold {(MedSimAI.XR.DesktopPlayerController.Active ? "T" : "A/X")})" : v.LiveOn && v.Mic == MicMode.Muted ? $"Microphone muted · type, or unmute in the pause menu" : $"Hold {(MedSimAI.XR.DesktopPlayerController.Active ? "T" : "A/X")} to talk to the <b>{who}</b>") + $" · {AIProviderManager.Instance.ModeLabel}",
                    (v.LiveOn && v.Mic == MicMode.HandsFree ? "تحدّث مباشرةً مع " : "اضغط مطوّلاً على " + (MedSimAI.XR.DesktopPlayerController.Active ? "T" : "A/X") + " للتحدث مع ") + (v.Target == Persona.Patient ? "المريض" : "الطبيبة")); break;
            }
        }

        void RefreshMode() { if (modeText) modeText.text = "Current mode: " + AIProviderManager.Instance.ModeLabel + (AIProviderManager.Instance.OnlineAvailable ? "" : "\n<color=#8892B0>(No AI proxy configured)</color>"); RefreshVoice(); }
        void RefreshLang() { var ai = AIProviderManager.Instance; var lp = ai.Language; if (langText) langText.text = (lp == null || lp.Language == "en" ? "Language: English (source text)" : $"Language: Arabic — <color=#FFB74D>{lp.Status}</color>. Offline voices are English-only; Arabic speech needs Live AI.") + $"\n<size=85%>Mode: {ai.LangMode} — say «احكي عربي» or \"speak English\" to switch both characters.</size>"; }

        void LateUpdate()
        {
            if (toastText && toastText.text.Length > 0 && Time.unscaledTime > toastUntil) toastText.text = "";
            UpdateConversationPaging();
            if (Time.frameCount % 30 == 0) RefreshProgress();
            if (perfText && Time.frameCount % 45 == 0 && MedSimAI.Core.PerformanceManager.Instance) { var pm = MedSimAI.Core.PerformanceManager.Instance; perfText.text = $"<color=#8892B0>Measured now: {pm.AverageFrameMs:0.0} ms/frame (≈{1000f / Mathf.Max(1, pm.AverageFrameMs):0} fps), GPU {pm.GpuFrameMs:0.0} ms. Headset target: 11.1 ms (90 Hz).</color>"; }
            // Lazy-follow subtitle bar: ~1.4 m ahead, 0.45 m below eye line; only re-centres when far off-axis (comfort).
            if (playerHead && subtitleCanvas && !desktopUI)
            {
                var fwd = Vector3.ProjectOnPlane(playerHead.forward, Vector3.up).normalized; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                var target = playerHead.position + fwd * 1.4f + Vector3.down * 0.42f;
                var t = subtitleCanvas.transform;
                float angle = Vector3.Angle(Vector3.ProjectOnPlane(t.position - playerHead.position, Vector3.up), fwd);
                float speed = angle > 25f ? 4f : 0.6f;
                t.position = Vector3.Lerp(t.position, target, Time.unscaledDeltaTime * speed);
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(t.position - playerHead.position, Vector3.up), Time.unscaledDeltaTime * 6f);
            }
        }
    }
}
