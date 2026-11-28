using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using MedSimAI.UI;
using TMPro;
using UnityEngine;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Guided training only. Shows the student, at every step, WHAT to pick up and WHERE to use it (from the authored scenario):
    ///   1. tool cue: a pulsing halo, a bobbing arrow and a label ("Pick this up: skin marker") on the instrument (or implant)
    ///      the current step needs next — for two-tool steps (cutting guide, then saw) the next one not yet used;
    ///   2. target cue: once the student holds the right tool, the soft pulsing ring marks the anatomy target.
    /// Visual cues only: they never complete or validate anything. Hidden in independent assessment.
    /// </summary>
    public class ObjectiveHighlighter : MonoBehaviour
    {
        public Transform ring;            // built by the scene builder (thin torus, unlit translucent)
        public float pulseHz = 0.8f;
        SurgicalScenarioData scenario; SurgeryEventBus bus; bool on; Transform target; Vector3 baseScale = Vector3.one;
        SurgicalObjective objective; string held = ""; readonly HashSet<string> usedThisStep = new HashSet<string>();
        Transform cueRoot, cueArrow; LineRenderer halo; TextMeshPro cueLabel; Transform cueTool; string cueId = "";
        /// <summary>The instrument/implant currently highlighted for pick-up (tests, evidence).</summary>
        public string CueId => on && cueTool && cueRoot && cueRoot.gameObject.activeSelf ? cueId : "";
        public bool TargetCueVisible => ring && ring.gameObject.activeSelf;
        public static string Language = "en";

        void Awake()
        {
            var ta = Resources.Load<TextAsset>("SurgeryScenarios/" + SurgeryContract.SCENARIO_ID_TKA);
            if (ta) scenario = JsonUtility.FromJson<SurgicalScenarioData>(ta.text);
            if (ring) { baseScale = ring.localScale; ring.gameObject.SetActive(false); }
            BuildCue();
        }
        void OnEnable() => StartCoroutine(Hook());
        void OnDisable() { if (bus != null) bus.EventPublished -= OnEvent; bus = null; }
        IEnumerator Hook() { while (!SurgeryEventBusHost.Instance) yield return null; bus = SurgeryEventBusHost.Instance.Bus; bus.EventPublished += OnEvent; }

        public void SetEnabled(bool enabledNow) { on = enabledNow; Refresh(); }

        void BuildCue()
        {
            cueRoot = new GameObject("[Tool cue]").transform; cueRoot.SetParent(transform, false);
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(sh) { color = new Color(0.25f, 0.95f, 1f) };
            halo = new GameObject("halo").AddComponent<LineRenderer>(); halo.transform.SetParent(cueRoot, false);
            halo.loop = true; halo.useWorldSpace = false; halo.positionCount = 40; halo.widthMultiplier = 0.006f; halo.sharedMaterial = mat;
            for (int i = 0; i < 40; i++) { float a = i / 40f * Mathf.PI * 2f; halo.SetPosition(i, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))); }
            // arrow: a small downward chevron made of lines (no extra assets)
            cueArrow = new GameObject("arrow").transform; cueArrow.SetParent(cueRoot, false);
            var shaft = cueArrow.gameObject.AddComponent<LineRenderer>(); shaft.useWorldSpace = false; shaft.sharedMaterial = mat;
            shaft.positionCount = 5; shaft.widthMultiplier = 0.008f;
            shaft.SetPositions(new[] { new Vector3(0, 0.11f, 0), new Vector3(0, 0f, 0), new Vector3(-0.025f, 0.03f, 0), new Vector3(0, 0f, 0), new Vector3(0.025f, 0.03f, 0) });
            cueLabel = new GameObject("label").AddComponent<TextMeshPro>(); cueLabel.transform.SetParent(cueRoot, false);
            cueLabel.fontSize = 0.42f; cueLabel.alignment = TextAlignmentOptions.Center; cueLabel.color = new Color(0.8f, 1f, 1f); cueLabel.fontStyle = FontStyles.Bold;
            cueLabel.rectTransform.sizeDelta = new Vector2(0.6f, 0.14f); cueLabel.outlineWidth = 0.22f; cueLabel.outlineColor = new Color32(0, 0, 0, 220);
            cueRoot.gameObject.SetActive(false);
        }

        void OnEvent(SurgeryEvent e)
        {
            if (scenario == null) return;
            if (e.source == SurgeryEventSources.Mechanics && e.id == SurgeryEventIds.ObjectiveStarted)
            {
                objective = scenario.objectives.FirstOrDefault(x => x.id == e.objectiveId); usedThisStep.Clear();
                var id = objective != null && objective.targets != null && objective.targets.Length > 0 ? objective.targets[0] : null;
                target = id == null ? null : FindObjectsByType<SurgeryAnatomyTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.targetId == id)?.transform;
            }
            else if (e.id == SurgeryEventIds.SessionReset || e.id == SurgeryEventIds.SessionStarted) { usedThisStep.Clear(); held = ""; }
            else if (e.id == SurgeryEventIds.ToolGrabbed) held = e.toolId ?? "";
            else if (e.id == SurgeryEventIds.ToolReleased && e.toolId == held) held = "";
            else if (e.id == SurgeryEventIds.AnatomyInteracted || e.id == SurgeryEventIds.AnatomyStateChanged) { if (!string.IsNullOrEmpty(e.toolId)) usedThisStep.Add(e.toolId); }
            else if (e.id == SurgeryEventIds.ImplantPlacementCompleted && e.success && objective != null && !string.IsNullOrEmpty(objective.implantId)) usedThisStep.Add(objective.implantId);
            Refresh();
        }

        /// <summary>The instrument (or implant) the student should pick up now, or "" when they already hold the right one.</summary>
        string NextPickup()
        {
            if (objective == null) return "";
            var tools = objective.requiredTools ?? new string[0];
            // placement steps: the component first (seated), then the impaction tool
            if (!string.IsNullOrEmpty(objective.implantId) && !usedThisStep.Contains(objective.implantId)) return held == objective.implantId ? "" : objective.implantId;
            if (tools.Length == 0) return "";
            if (tools.Contains(held)) return "";
            return tools.FirstOrDefault(t => !usedThisStep.Contains(t)) ?? tools[0];   // e.g. the cutting guide before the saw
        }

        void Refresh()
        {
            cueId = on && !DemonstrationRunning ? NextPickup() : "";   // the pick-up cue is for the student, not for a demonstrator
            cueTool = string.IsNullOrEmpty(cueId) ? null : FindPickup(cueId);
            if (cueRoot) cueRoot.gameObject.SetActive(on && cueTool);
            if (cueTool && cueLabel && scenario != null)
            {
                string lang = Language == "ar" ? "ar" : "en";
                var name = scenario.Instrument(cueId)?.name.Get(lang) ?? scenario.implants.FirstOrDefault(i => i.id == cueId)?.name.Get(lang) ?? cueId;
                ArabicShaper.Apply(cueLabel, (lang == "ar" ? "خذ هذه الأداة: " : "Pick this up: ") + name);
            }
            // the target ring shows once the right tool is in hand (or for steps without a tool to pick up)
            if (ring) ring.gameObject.SetActive(on && target && !cueTool);
        }

        static MedSimAI.Surgery.Mechanics.SurgicalDesktopInteractionAdapter desk;
        static bool DemonstrationRunning => SurgicalDemonstrator.Instance && SurgicalDemonstrator.Instance.Running;
        bool demoWas;
        static bool IsInStudentsHand(Transform t)
        {
            if (!desk) desk = FindAnyObjectByType<MedSimAI.Surgery.Mechanics.SurgicalDesktopInteractionAdapter>(FindObjectsInactive.Include);
            var d = desk;
            if (d && d.isActiveAndEnabled && d.HeldObject == t) return true;
            var g = t.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            return g && g.isSelected && !(g.firstInteractorSelecting is UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor);
        }

        static Transform FindPickup(string id)
        {
            foreach (var t in FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None)) if (t.instrumentId == id) return t.transform;
            foreach (var t in FindObjectsByType<SurgeryImplantTag>(FindObjectsSortMode.None)) if (t.implantId == id) return t.transform;
            return null;
        }

        void Update()
        {
            if (!on) return;
            if (ring && target && ring.gameObject.activeSelf)
            {
                var b = target.GetComponent<Collider>();
                ring.position = b ? b.bounds.center : target.position;
                float s = 1f + 0.12f * Mathf.Sin(Time.time * Mathf.PI * 2f * pulseHz);
                ring.localScale = baseScale * s;
            }
            if (DemonstrationRunning != demoWas) { demoWas = DemonstrationRunning; Refresh(); }
            if (cueTool && IsInStudentsHand(cueTool)) { held = cueId; Refresh(); }   // also implants (no tool.grabbed event)
            if (cueTool && cueRoot && cueRoot.gameObject.activeSelf)
            {
                var bounds = new Bounds(cueTool.position, Vector3.zero); foreach (var r in cueTool.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                float rad = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.25f, 0.05f, 0.25f) * (1f + 0.1f * Mathf.Sin(Time.time * 6f));
                halo.transform.position = new Vector3(bounds.center.x, bounds.min.y + 0.004f, bounds.center.z); halo.transform.localScale = new Vector3(rad, 1, rad); halo.transform.rotation = Quaternion.identity;
                float bob = 0.02f * Mathf.Sin(Time.time * 4f);
                cueArrow.position = new Vector3(bounds.center.x, bounds.max.y + 0.03f + bob, bounds.center.z);
                cueLabel.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.2f, bounds.center.z);
                var cam = Camera.main;
                if (cam)
                {
                    var look = cueLabel.transform.position - cam.transform.position; cueLabel.transform.rotation = Quaternion.LookRotation(look);
                    var flat = Vector3.ProjectOnPlane(look, Vector3.up); if (flat.sqrMagnitude > 1e-4f) cueArrow.rotation = Quaternion.LookRotation(flat);
                }
            }
        }
    }
}
