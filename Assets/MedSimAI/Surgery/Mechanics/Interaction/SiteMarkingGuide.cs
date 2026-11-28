using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Guided site marking (objective obj.site_confirmation, skin marker). Instead of a single "touch = done" contact, the
    /// student physically draws the mark on the skin of the RIGHT knee:
    ///   • a dotted guide is projected on the knee skin along the authored marking site, with a short instruction label;
    ///   • VR: the marker's tip must touch the skin (pen down) and move along the guide; desktop: hold the left mouse button
    ///     and move the aim along the guide (the pen tip follows the skin);
    ///   • the student's ink stays on the skin; guide dots turn green as they are covered; when (almost) all are covered the
    ///     marking is validated through the normal mechanics path (AnatomyInteractionController → objective completed);
    ///   • ink drawn well away from the guide turns red, wipes off and the supervisor explains why (Feedback event);
    ///     the left knee is still a wrong-side attempt (handled by the normal contact path, never consumed here).
    /// Gameplay geometry only (DRAFT); it is not a clinical marking protocol.
    /// </summary>
    public class SiteMarkingGuide : MonoBehaviour
    {
        public string objectiveId = "obj.site_confirmation", toolId = InstrumentIds.SkinMarker, targetId = "knee_r.site_marking";
        public int guidePoints = 7;
        public float guideLength = 0.12f, coverRadius = 0.013f, offGuideDistance = 0.035f, penDownDistance = 0.012f;
        [Range(0.5f, 1f)] public float requiredCoverage = 0.85f;

        /// <summary>Host: show the projected guide (guided training). Drawing always works.</summary>
        public static bool ShowGuide = true;
        /// <summary>Host: UI language for the label ("en"/"ar").</summary>
        public static string Language = "en";
        /// <summary>Raised for the host to explain a mistake or confirm progress: "off_guide", "completed".</summary>
        public static event Action<string> Feedback;
        public static SiteMarkingGuide Instance { get; private set; }
        /// <summary>Host: sets label text (the hospital passes its Arabic shaper so Arabic renders right-to-left).</summary>
        public static Action<TMP_Text, string> TextSetter;

        public bool Active { get; private set; }
        public int Covered => covered.Count(c => c);
        public int Total => guide.Count;
        public IReadOnlyList<Vector3> GuideWorldPoints => guide;
        public MeshCollider Surface => surface;

        SurgeryScenarioEngine engine; AnatomyInteractionController anatomy; SurgicalDesktopInteractionAdapter desktop;
        MeshCollider surface; Transform root; readonly List<Vector3> guide = new List<Vector3>(); bool[] covered = new bool[0];
        readonly List<Transform> dots = new List<Transform>(); LineRenderer guideLine; TextMeshPro label; Material dotMat, inkMat, badInkMat;
        LineRenderer stroke; readonly List<LineRenderer> strokes = new List<LineRenderer>(); bool penWasDown; float offGuideInk; Vector3 lastInk; float lastFeedback = -10f;
        SurgeryInstrumentTag pen; bool built;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; if (AnatomyInteractionController.ContactInterceptor == Intercept) AnatomyInteractionController.ContactInterceptor = null; }
        void OnEnable() { AnatomyInteractionController.ContactInterceptor = Intercept; }
        void OnDisable() { if (AnatomyInteractionController.ContactInterceptor == Intercept) AnatomyInteractionController.ContactInterceptor = null; SetVisible(false); if (bus != null) bus.EventPublished -= OnBus; bus = null; }
        SurgeryEventBus bus;
        void OnBus(SurgeryEvent e) { if (e.id == SurgeryEventIds.SessionReset || (e.id == SurgeryEventIds.ObjectiveStarted && e.objectiveId == objectiveId)) ResetDrawing(); }

        /// <summary>While marking is the current step, the marker's plain contacts on the right knee belong to the drawing.</summary>
        bool Intercept(string tool, string target) => Active && tool == toolId && AnatomyInteractionController.IsOperativeSide(target) && target != null && target.StartsWith("knee_r.");

        bool Build()
        {
            if (built) return surface;
            built = true;
            engine = FindFirstObjectByType<SurgeryScenarioEngine>(); anatomy = FindFirstObjectByType<AnatomyInteractionController>();
            desktop = FindFirstObjectByType<SurgicalDesktopInteractionAdapter>(FindObjectsInactive.Include);
            var site = FindObjectsByType<SurgeryAnatomyTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.targetId == targetId);
            if (!site) return false;
            var knee = site.transform; while (knee && !knee.name.StartsWith("KneeAnatomy")) knee = knee.parent;
            var skin = knee ? knee.Find("Skin/KneeSkin") : null; var markVis = knee ? knee.Find("Skin/SiteMarking") : null;
            var mf = skin ? skin.GetComponent<MeshFilter>() : null; if (!mf || !mf.sharedMesh) return false;
            // Drawing surface: the knee skin mesh (raycast-only layer; also lets a VR marker rest on the skin instead of sinking in).
            var go = new GameObject("[Marking surface]"); go.layer = 2; go.transform.SetParent(skin, false);
            surface = go.AddComponent<MeshCollider>(); surface.sharedMesh = mf.sharedMesh;
            root = new GameObject("[Site marking guide]").transform; root.SetParent(transform, false);
            // Guide path: along the authored marking visual (anterior knee, along the leg), projected onto the skin.
            Bounds b = markVis && markVis.GetComponent<Renderer>() ? markVis.GetComponent<Renderer>().bounds : site.GetComponent<Collider>().bounds;
            var axis = b.size.z >= b.size.x ? Vector3.forward : Vector3.right;
            Physics.SyncTransforms();
            for (int i = 0; i < guidePoints; i++)
            {
                float t = guidePoints == 1 ? 0 : i / (float)(guidePoints - 1) - 0.5f;
                var p = b.center + axis * (t * guideLength);
                if (surface.Raycast(new Ray(p + Vector3.up * 0.2f, Vector3.down), out var hit, 0.5f)) guide.Add(hit.point + hit.normal * 0.0015f);
            }
            covered = new bool[guide.Count];
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            dotMat = new Material(sh); dotMat.color = new Color(0.2f, 0.95f, 1f); inkMat = new Material(sh); inkMat.color = new Color(0.22f, 0.05f, 0.45f); badInkMat = new Material(sh); badInkMat.color = new Color(0.9f, 0.15f, 0.1f);
            foreach (var p in guide)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(d.GetComponent<Collider>()); d.name = "guide dot";
                d.transform.SetParent(root, false); d.transform.position = p; d.transform.localScale = Vector3.one * 0.007f; d.GetComponent<Renderer>().sharedMaterial = new Material(dotMat); dots.Add(d.transform);
            }
            guideLine = new GameObject("guide line").AddComponent<LineRenderer>(); guideLine.transform.SetParent(root, false);
            guideLine.useWorldSpace = true; guideLine.positionCount = guide.Count; guideLine.SetPositions(guide.ToArray()); guideLine.widthMultiplier = 0.0018f;
            guideLine.sharedMaterial = new Material(sh) { color = new Color(0.2f, 0.95f, 1f, 0.8f) }; guideLine.numCapVertices = 2;
            label = new GameObject("guide label").AddComponent<TextMeshPro>(); label.transform.SetParent(root, false);
            label.fontSize = 0.32f; label.alignment = TextAlignmentOptions.Center; label.color = new Color(0.75f, 1f, 1f); label.rectTransform.sizeDelta = new Vector2(0.42f, 0.12f);
            label.transform.position = (guide.Count > 0 ? guide[guide.Count / 2] : b.center) + Vector3.up * 0.13f;
            label.outlineWidth = 0.2f; label.outlineColor = new Color32(0, 0, 0, 200);
            SetVisible(false);
            return true;
        }

        void SetVisible(bool on) { if (root) root.gameObject.SetActive(on); }

        void Update()
        {
            if (bus == null && SurgeryEventBusHost.Instance) { bus = SurgeryEventBusHost.Instance.Bus; bus.EventPublished += OnBus; }
            if (!Build() || engine == null) { Active = false; return; }
            bool active = engine.CurrentObjectiveId == objectiveId && !engine.IsFinished;
            if (active && !Active) ResetDrawing();
            Active = active;
            if (!active) { SetVisible(false); return; }
            if (!pen) pen = FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None).FirstOrDefault(t => t.instrumentId == toolId);
            SetVisible(true);
            guideLine.enabled = ShowGuide; foreach (var d in dots) d.gameObject.SetActive(ShowGuide);
            for (int i = 0; i < dots.Count; i++) { var r = dots[i].GetComponent<Renderer>(); r.sharedMaterial.color = covered[i] ? new Color(0.2f, 1f, 0.35f) : new Color(0.2f, 0.95f, 1f, 0.6f + 0.4f * Mathf.Sin(Time.time * 5f)); }
            var cam = Camera.main; if (cam && label) label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);

            bool penDown = PenDown(out var at);
            if (penDown) Ink(at);
            else if (penWasDown) { stroke = null; }
            penWasDown = penDown;
            UpdateLabel();
        }

        bool Held => pen && ((desktop && desktop.isActiveAndEnabled && desktop.HeldToolId == toolId) || (pen.GetComponentInParent<XRGrabInteractable>()?.isSelected ?? false) || DemoPen || TestHeld);
        /// <summary>Test hook: treat the marker as held in a VR hand.</summary>
        public static bool TestHeld;
        /// <summary>Demonstration: a character draws with the pen (tip position driven by the demonstrator).</summary>
        public static bool DemoPen; public static Vector3? DemoTip;

        bool PenDown(out Vector3 at)
        {
            at = default;
            if (!Held) return false;
            if (DemoPen) { if (DemoTip.HasValue && surface.Raycast(new Ray(DemoTip.Value + Vector3.up * 0.05f, Vector3.down), out var dh, 0.08f)) { at = dh.point; return true; } return false; }
            if (desktop && desktop.isActiveAndEnabled && desktop.HeldToolId == toolId)
            {   // desktop: hold the left button and move the aim along the guide
                var m = Mouse.current; if (m == null || !m.leftButton.isPressed) { desktop.TipLock = null; return false; }
                if (surface.Raycast(desktop.AimRay, out var hit, 3f)) { at = hit.point; desktop.TipLock = hit.point + hit.normal * 0.001f; return true; }
                desktop.TipLock = null; return false;
            }
            var tip = pen.tip ? pen.tip.position : pen.transform.position;   // VR: the tip must touch the skin
            if (surface.Raycast(new Ray(tip + Vector3.up * 0.05f, Vector3.down), out var h, 0.05f + penDownDistance)) { at = h.point; return true; }
            return false;
        }

        void Ink(Vector3 p)
        {
            p += Vector3.up * 0.0012f;
            if (stroke == null)
            {
                stroke = new GameObject("ink stroke").AddComponent<LineRenderer>(); stroke.transform.SetParent(transform, false);
                stroke.useWorldSpace = true; stroke.widthMultiplier = 0.0024f; stroke.numCapVertices = 2; stroke.positionCount = 0; stroke.sharedMaterial = inkMat; strokes.Add(stroke);
                lastInk = p + Vector3.one;
            }
            if ((p - lastInk).sqrMagnitude < 0.003f * 0.003f) return;
            stroke.positionCount++; stroke.SetPosition(stroke.positionCount - 1, p);
            float step = stroke.positionCount > 1 ? Vector3.Distance(p, lastInk) : 0f; lastInk = p;
            float dGuide = DistanceToGuide(p);
            for (int i = 0; i < guide.Count; i++) if (!covered[i] && Vector3.Distance(guide[i], p) <= coverRadius) covered[i] = true;
            if (dGuide > offGuideDistance)
            {   // ink off the planned site: shown red, wiped after a moment, explained once in a while
                stroke.sharedMaterial = badInkMat; offGuideInk += step;
                if (offGuideInk > 0.03f && Time.time - lastFeedback > 6f) { lastFeedback = Time.time; offGuideInk = 0; Feedback?.Invoke("off_guide"); var s = stroke; stroke = null; Destroy(s.gameObject, 1.5f); strokes.Remove(s); }
            }
            if (guide.Count > 0 && Covered >= Mathf.CeilToInt(guide.Count * requiredCoverage)) Complete();
        }

        float DistanceToGuide(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < guide.Count; i++) { var a = guide[i]; var ab = guide[i + 1] - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); best = Mathf.Min(best, Vector3.Distance(p, a + ab * t)); }
            return guide.Count == 1 ? Vector3.Distance(p, guide[0]) : best;
        }

        void Complete()
        {
            if (DemoPen) { Feedback?.Invoke("demo_completed"); return; }   // a demonstration draws but never completes the student's step
            if (desktop) desktop.TipLock = null;
            Active = false; SetVisible(false);
            Feedback?.Invoke("completed");
            if (anatomy) anatomy.HandleGuidedContact(toolId, targetId);   // validated by the normal mechanics path
        }

        /// <summary>Test/automation hook: draws one pen-down stroke through these skin points (same path as a real stroke).</summary>
        public void TestStroke(IEnumerable<Vector3> points)
        {
            if (!Build()) return;
            if (engine != null && engine.CurrentObjectiveId == objectiveId && !engine.IsFinished) Active = true;
            if (!Active) return;
            stroke = null; foreach (var p in points) { if (!Active) break; Ink(p); } stroke = null;
        }
        /// <summary>Test hook: a point on the knee skin under (x, z), or null.</summary>
        public Vector3? SkinPoint(Vector3 above) { if (!Build()) return null; return surface.Raycast(new Ray(above + Vector3.up * 0.2f, Vector3.down), out var h, 0.5f) ? h.point : (Vector3?)null; }

        /// <summary>Starts the step over (new objective activation, session reset, or after a demonstration).</summary>
        public void ResetDrawing()
        {
            foreach (var s in strokes) if (s) Destroy(s.gameObject);
            strokes.Clear(); stroke = null; penWasDown = false; offGuideInk = 0;
            for (int i = 0; i < covered.Length; i++) covered[i] = false;
            if (desktop) desktop.TipLock = null;
        }

        void UpdateLabel()
        {
            if (!label) return;
            bool ar = Language == "ar"; bool desk = desktop && desktop.isActiveAndEnabled;
            string text;
            if (DemoPen) text = ar ? "تابع: هيك بنعلّم مكان العملية" : "Watch: this is how the site is marked";
            else if (!Held) text = ar ? "خذ قلم التعليم الجراحي" : "Pick up the skin marker";
            else if (Covered == 0) text = desk ? (ar ? "اضغط زر الفأرة الأيسر مع الاستمرار وارسم على الخط المنقّط" : "Hold the left mouse button and draw along the dotted line")
                                              : (ar ? "المس الجلد بطرف القلم وارسم على الخط المنقّط" : "Touch the skin with the pen tip and draw along the dotted line");
            else text = (ar ? "تابع الرسم على الخط: " : "Keep drawing along the line: ") + Covered + "/" + Total;
            if (Time.time - lastFeedback < 4f) text = ar ? "هذا خارج مكان التعليم. ارسم على الخط المنقّط فوق الركبة اليمنى" : "That is off the marking site. Draw on the dotted line over the right knee";
            if (TextSetter != null) TextSetter(label, text); else label.text = text;
            label.color = Time.time - lastFeedback < 4f ? new Color(1f, 0.6f, 0.3f) : new Color(0.75f, 1f, 1f);
        }
    }
}
