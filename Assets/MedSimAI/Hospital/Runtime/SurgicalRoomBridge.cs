using System;
using System.Collections;
using System.Linq;
using MedSimAI.AI;
using MedSimAI.Core;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Characters;
using MedSimAI.Surgery.Integration;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.Surgery.UI;
using MedSimAI.Voice;
using MedSimAI.XR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Room 02 (operating room) inside the hospital. Starts a fresh surgical session when the student enters and tears it
    /// down cleanly when they leave: real mechanics (Person 3) on Person 2's patient, instruments and implants, the persistent
    /// doctor in the surgeon role, the assistant, Person 4's session controller (supervisor, hand-over, assessment, HUD,
    /// voice). Owns which voice pipeline and which keys are live while the student is in the room, keeps the language in
    /// sync with the rest of the hospital, and applies the training mode (guided vs. independent assessment).
    /// </summary>
    public class SurgicalRoomBridge : MonoBehaviour
    {
        public GameObject sessionRoot;                       // SurgerySessionController + HUD + voice input (inactive outside the OR)
        public SurgerySessionController controller;
        public SurgeryMechanicsRoot mechanics;
        public SurgicalCharacter doctorRole;                 // on the persistent doctor; enabled only in the OR
        public SurgicalCharacter assistant;
        public SurgeryCharacterActuator actuator;
        public SurgicalDesktopInteractionAdapter desktopAdapter;
        public RobotGuidanceAdapter robot;
        public ObjectiveHighlighter highlighter;

        public bool Active { get; private set; }
        /// <summary>The student has already been in the operating room during this visit: the welcome rule says a short line.</summary>
        public bool Returning { get; private set; }
        public bool GuidedMode { get; private set; } = true;
        public string SessionId { get; private set; } = "";
        public float StartedAt { get; private set; }
        public event Action<SurgicalAssessmentResult> Completed;
        public event Action<string> LanguageChanged;
        public event Action ReturnRequested;

        SupervisorRule[] allRules; bool hooked; string lastLang;

        void Awake() { if (sessionRoot && sessionRoot.activeSelf) sessionRoot.SetActive(false); }

        /// <summary>The student entered the operating room: start a fresh surgical session.</summary>
        public void Begin(string lang, bool guided, bool returning = false)
        {
            GuidedMode = guided; Returning = returning;
            ScenarioRouter.Language = lang; lastLang = lang;
            var er = VoiceInteractionManager.Instance; if (er) { er.Interrupt(); er.enabled = false; }   // one voice pipeline at a time
            DesktopPlayerController.SurgeryKeysActive = true;
            if (SessionManager.Instance) SessionManager.Instance.suppressPause = true;   // the surgical HUD has its own pause menu
            MedSimAI.Characters.CharacterActionRouter.Reset();
            if (mechanics && !mechanics.gameObject.activeSelf) mechanics.gameObject.SetActive(true);
            if (doctorRole) doctorRole.enabled = true;
            if (desktopAdapter) desktopAdapter.enabled = DisplayModeManager.Mode != DisplayMode.VR;
            SurgicalDesktopInteractionAdapter.ResetRequested = () => { if (Active && controller) controller.Restart(); };
            SurgicalDesktopInteractionAdapter.ResetPrompt = text => { if (controller && controller.hud) controller.hud.SetBanner(text); };
            HandPresence.ExtraHeld = () => Active && desktopAdapter && desktopAdapter.isActiveAndEnabled ? desktopAdapter.HeldObject : null;   // gloved hand around the held instrument
            // Physical tool–tissue contact is the VR way of acting; on desktop the student acts by clicking (no accidental touches).
            foreach (var t in FindObjectsByType<AnatomyContactTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None)) t.enabled = DisplayModeManager.Mode == DisplayMode.VR;
            var entry = FindAnyObjectByType<MedSimAI.Surgery.Environment.RoomEntryPublisher>();
            var rig = FindAnyObjectByType<XRPlayerController>();
            if (entry && rig) entry.BindPlayer(rig.transform);
            bool first = sessionRoot && !sessionRoot.activeSelf && !hooked;
            if (sessionRoot) sessionRoot.SetActive(true);
            Hook();
            ApplyMode();
            if (!first) controller.Restart();   // first visit: the controller's Start() runs the session itself
            Active = true; StartedAt = Time.time;
            ObjectiveHighlighter.Language = lang;
            if (highlighter) highlighter.SetEnabled(GuidedMode);
            EnsureMarkingGuide(lang);
            if (!demonstrator) { demonstrator = GetComponent<SurgicalDemonstrator>(); if (!demonstrator) demonstrator = gameObject.AddComponent<SurgicalDemonstrator>(); demonstrator.bridge = this; }
            demonstrator.enabled = true;
            StartCoroutine(TrackSessionId());
        }

        IEnumerator TrackSessionId()
        {
            float t0 = Time.time;
            while (Active && (controller.Core == null || !controller.Core.Active) && Time.time - t0 < 20f) yield return null;
            if (controller.Core != null) SessionId = controller.Core.Bus.SessionId;
        }

        void Hook()
        {
            if (hooked || !controller || controller.Core == null) return;
            hooked = true;
            controller.hostManaged = true;
            controller.Core.SessionCompleted += r => Completed?.Invoke(r);
            controller.HostReturnRequested += () => ReturnRequested?.Invoke();
            allRules = controller.Scenario.supervisorRules;
        }

        /// <summary>
        /// Independent assessment keeps the same simulation and scoring; only unsolicited coaching (hints after a rejection or
        /// an idle pause) is switched off. Safety interventions, repeated-mistake interventions and hints the student asks
        /// for remain, and every hint is counted in the assessment.
        /// </summary>
        void ApplyMode()
        {
            if (allRules == null) return;
            var rules = GuidedMode ? allRules
                : allRules.Where(r => !(r.kind == "hint" && (r.trigger.type == "rejected" || r.trigger.type == "idle"))).ToArray();
            if (Returning) rules = rules.Select(r => r.kind != "welcome" ? r : ShortWelcome(r)).ToArray();
            controller.Scenario.supervisorRules = rules;
        }

        static SupervisorRule ShortWelcome(SupervisorRule r)
        {
            var c = JsonUtility.FromJson<SupervisorRule>(JsonUtility.ToJson(r));   // a copy: the scenario's own rule is never changed
            c.line.en = HospitalLines.Get("room02_again", "en"); c.line.ar = HospitalLines.Get("room02_again", "ar");
            return c;
        }

        public void SetGuidedMode(bool guided) { GuidedMode = guided; ApplyMode(); if (highlighter) highlighter.SetEnabled(guided && Active); SiteMarkingGuide.ShowGuide = guided; }

        // ------------------------------------------------------------------ guided site marking (draw the mark on the knee)
        SiteMarkingGuide marking;
        /// <summary>Embodied demonstrations (explain → demonstrate → let the student try).</summary>
        public SurgicalDemonstrator demonstrator;
        void EnsureMarkingGuide(string lang)
        {
            if (!marking && mechanics) { marking = mechanics.GetComponent<SiteMarkingGuide>(); if (!marking) marking = mechanics.gameObject.AddComponent<SiteMarkingGuide>(); }
            SiteMarkingGuide.ShowGuide = GuidedMode; SiteMarkingGuide.Language = lang; SiteMarkingGuide.TextSetter = (t, s) => MedSimAI.UI.ArabicShaper.Apply(t, s);
            SiteMarkingGuide.Feedback -= OnMarkingFeedback; SiteMarkingGuide.Feedback += OnMarkingFeedback;
            if (marking) { marking.enabled = true; marking.ResetDrawing(); }
        }

        /// <summary>A marking mistake is explained by the doctor (approved bilingual line), not only shown.</summary>
        void OnMarkingFeedback(string what)
        {
            if (!Active || what != "off_guide") return;
            bool ar = lastLang == "ar";
            Say(ar ? "هذا خارج مكان التعليم. خلّي العلامة على الخط المنقّط فوق مقدّمة الركبة اليمنى." : "That's off the marking site. Keep the mark on the dotted line over the front of the right knee.");
        }

        /// <summary>The student left the operating room: stop the session and put the room back to its start state.</summary>
        public void End()
        {
            if (!Active) return;
            if (demonstrator) { demonstrator.Abort("left_room", false); demonstrator.enabled = false; }
            Active = false;
            if (controller) controller.Suspend();
            if (desktopAdapter) { desktopAdapter.DropHeldItem(); desktopAdapter.enabled = false; }
            var mgr = FindAnyObjectByType<XRInteractionManager>();
            if (mgr)
                foreach (var g in FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
                    if (g.isSelected && (g.GetComponent<SurgeryInstrumentTag>() || g.GetComponent<SurgeryImplantTag>())) mgr.CancelInteractableSelection((IXRSelectInteractable)g);
            if (actuator && assistant && assistant.Held) { var held = assistant.Held.GetComponent<SurgeryInstrumentTag>(); actuator.HandOverFailed(held ? held.instrumentId : ""); }
            if (mechanics) mechanics.ResetForNewSession();
            if (assistant) { assistant.Interrupt(); assistant.GoToAnchor(assistant.homeAnchor, _ => { }); }
            if (doctorRole) { doctorRole.Interrupt(); doctorRole.enabled = false; }
            if (robot) robot.Park();
            if (highlighter) highlighter.SetEnabled(false);
            if (marking) { marking.ResetDrawing(); marking.enabled = false; }
            SiteMarkingGuide.Feedback -= OnMarkingFeedback;
            if (sessionRoot) sessionRoot.SetActive(false);
            SurgicalDesktopInteractionAdapter.ResetRequested = null; SurgicalDesktopInteractionAdapter.ResetPrompt = null;
            var er = VoiceInteractionManager.Instance; if (er) er.enabled = true;
            DesktopPlayerController.SurgeryKeysActive = false;
            if (SessionManager.Instance) SessionManager.Instance.suppressPause = false;
        }

        /// <summary>A guide line spoken by the doctor inside the operating room (through the surgical speech queue).</summary>
        public void Say(string text) { if (Active && controller) controller.Say(SurgeryCharacterIds.Surgeon, text); }

        void Update()
        {
            if (!Active || !controller || controller.Core == null) return;
            var lang = controller.Core.Language;
            if (lang != lastLang) { lastLang = lang; LanguageChanged?.Invoke(lang); SiteMarkingGuide.Language = lang; ObjectiveHighlighter.Language = lang; if (highlighter) highlighter.SetEnabled(GuidedMode); }
        }
    }
}
