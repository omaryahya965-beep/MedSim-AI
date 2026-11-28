using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Characters;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.Characters;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.AI;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Embodied teaching in the operating room: explain → DEMONSTRATE → let the student try.
    /// On "show me / do it in front of me" (demonstrate_step) or "show me the whole procedure" (demonstrate_procedure) the
    /// addressed character (Dr. Hassan or Sami) really performs the step in front of the student: walks to the instrument
    /// tray, reaches for and takes the required tool (procedural arm IK), walks to the knee, puts the tool's working tip on
    /// the real target and moves it the way the step needs (draws the mark along the guide, follows the incision line,
    /// retracts, saws through the guide slot, carries and seats implants, impacts, staples), narrating with short APPROVED
    /// lines, while the anatomy shows the step's visible result.
    /// A demonstration NEVER changes the student's progress and never earns credit: nothing goes through the mechanics'
    /// validation; afterwards the anatomy, tools and implants are restored exactly and the student is invited to try. It is
    /// logged on the bus as assistance (hint.given, reason "demonstration:…") so the assessment counts it.
    /// The student can stop it at any time ("enough, let me try"), or leave the room.
    /// </summary>
    public class SurgicalDemonstrator : MonoBehaviour
    {
        public SurgicalRoomBridge bridge;
        public float stepPause = 0.6f;
        public static SurgicalDemonstrator Instance { get; private set; }
        public bool Running => run != null;
        public string CurrentObjective { get; private set; } = "";
        public readonly List<string> Log = new List<string>();            // tests / evidence: what was demonstrated
        public SurgicalCharacter Demonstrator { get; private set; }

        Coroutine run; Action<bool, string> done; Snapshot snap; ArmReachIK ik; Transform heldTool;
        SurgicalScenarioData Scenario => bridge && bridge.controller ? bridge.controller.Scenario : null;
        string Lang => bridge && bridge.controller && bridge.controller.Core != null ? bridge.controller.Core.Language : "en";

        void Awake() { Instance = this; }
        void OnEnable()
        {
            SurgeryCharacterActuator.DemonstrationHandler = Begin;
            MedSimAI.Surgery.Integration.SurgerySessionController.StopDemonstrationRequested += StopRequested;
        }
        void OnDisable()
        {
            if (SurgeryCharacterActuator.DemonstrationHandler == (Func<CharacterActionRequest, Action<bool, string>, bool>)Begin) SurgeryCharacterActuator.DemonstrationHandler = null;
            MedSimAI.Surgery.Integration.SurgerySessionController.StopDemonstrationRequested -= StopRequested;
        }
        void StopRequested() { if (Running) Abort("stopped_by_student", true); }

        // ------------------------------------------------------------------ entry
        public bool Begin(CharacterActionRequest r, Action<bool, string> onDone)
        {
            if (!bridge || !bridge.Active || Scenario == null || bridge.mechanics == null) return false;
            if (Running) Abort("replaced", false);
            var who = r.characterId == SurgeryCharacterIds.Assistant ? bridge.assistant : bridge.doctorRole;
            if (!who) who = bridge.doctorRole ? bridge.doctorRole : bridge.assistant;
            if (!who) return false;
            // the addressed character is not in the operating room yet (e.g. still walking in): the other one, who is here, shows it
            var other = who == bridge.doctorRole ? bridge.assistant : bridge.doctorRole;
            var site = SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR);
            if (other && site && Flat(who.transform.position, site.position) > 6f && Flat(other.transform.position, site.position) < 6f) who = other;
            bool full = r.action == SurgeryActionCatalog.DemonstrateProcedure;
            var engine = bridge.mechanics.ScenarioEngine;
            var current = engine != null ? engine.CurrentObjective : null;
            var steps = Scenario.Ordered.ToList();
            int from = current != null ? Math.Max(0, steps.FindIndex(o => o.id == current.id)) : 0;
            var plan = full ? steps.Skip(from).ToList() : (current != null ? new List<SurgicalObjective> { steps.First(o => o.id == current.id) } : new List<SurgicalObjective>());
            if (plan.Count == 0) return false;
            done = onDone; Demonstrator = who;
            run = StartCoroutine(Run(who, plan, full));
            return true;
        }

        // ------------------------------------------------------------------ the demonstration
        IEnumerator Run(SurgicalCharacter c, List<SurgicalObjective> plan, bool full)
        {
            var ctl = bridge.controller; var core = ctl.Core;
            if (core != null) core.Supervisor.Demonstrating = true;
            snap = Snapshot.Take(bridge.mechanics);
            if (!c.TryGetComponent(out ik)) ik = c.gameObject.AddComponent<ArmReachIK>();
            c.Interrupt(); c.LookAt(ctl && Camera.main ? Camera.main.transform : null);
            Publish("demonstration:" + (full ? "procedure" : plan[0].id));
            Log.Add((full ? "procedure from " : "step ") + plan[0].id + " by " + c.characterId);
            Say(c, Line(full ? "open_full" : "open_step"));
            yield return WaitSpeech();
            foreach (var o in plan)
            {
                CurrentObjective = o.id; Log.Add("demonstrating " + o.id);
                if (full) Say(c, (Lang == "ar" ? "الخطوة: " : "Step: ") + o.title.Get(Lang) + ".");
                yield return DemonstrateObjective(c, o);
                yield return WaitSpeech();
                yield return new WaitForSeconds(stepPause);
            }
            CurrentObjective = "";
            yield return ReturnTool(c);
            ik.Release();
            snap.Restore(bridge.mechanics);
            SiteMarkingGuide.Instance?.ResetDrawing();
            Say(c, Line(full ? "your_turn_full" : "your_turn"));
            c.GoToAnchor(c.homeAnchor, _ => { }, Camera.main ? Camera.main.transform : null);
            yield return WaitSpeech();
            Finish(true, "");
        }

        IEnumerator DemonstrateObjective(SurgicalCharacter c, SurgicalObjective o)
        {
            var target = TargetCollider(o.targets.FirstOrDefault());
            var view = bridge.mechanics.GetComponentInChildren<KneeAnatomyStateController>(true) is var kc && kc ? kc : FindAnyObjectByType<KneeAnatomyStateController>();
            // placement steps: carry and seat each component of this step, then impact
            if (!string.IsNullOrEmpty(o.implantId))
            {
                foreach (var imp in ImplantsFor(o))
                {
                    yield return ReturnTool(c);
                    Say(c, Line("implant", Name(imp.implantId)));
                    yield return CarryImplant(c, imp);
                }
                foreach (var t in o.targets) view?.SetTargetState(t, o.completionState);
                if (o.requiredTools.Length > 0)
                {
                    var tool = o.requiredTools[0];
                    yield return FetchTool(c, tool);
                    Say(c, Line("impact", Name(tool)));
                    yield return ToolMotion(c, tool, target, "tap");
                }
                yield break;
            }
            for (int i = 0; i < o.requiredTools.Length; i++)
            {
                var tool = o.requiredTools[i];
                yield return FetchTool(c, tool);
                Say(c, Line(ToolLineKey(tool), Name(tool)));
                yield return ToolMotion(c, tool, target, Motion(tool));
                string state = SurgeryInteractionValidator.IsCuttingGuide(tool) ? AnatomyStates.GuidePlaced : o.completionState;
                foreach (var t in o.targets) view?.SetTargetState(t, state);
                if (tool == InstrumentIds.SkinMarker) SiteMarkingGuide.DemoPen = false;
            }
        }

        // ------------------------------------------------------------------ physical pieces
        IEnumerator FetchTool(SurgicalCharacter c, string toolId)
        {
            if (heldTool && heldTool.GetComponent<SurgeryInstrumentTag>()?.instrumentId == toolId) yield break;
            yield return ReturnTool(c);
            var tool = FindTool(toolId); if (!tool) yield break;
            yield return Walk(c, NearPoint(tool.position, 0.42f), tool);   // right next to the instrument, within arm's reach
            ik.ReachTo(tool.position + Vector3.up * 0.03f); yield return new WaitForSeconds(0.55f);
            c.Pick(tool); Grip(c, tool); heldTool = tool;
            ik.Release(); yield return new WaitForSeconds(0.3f);
        }

        IEnumerator ReturnTool(SurgicalCharacter c)
        {
            if (!heldTool) yield break;
            var tag = heldTool.GetComponent<SurgeryInstrumentTag>();
            var slot = tag ? SurgeryAnchor.Find(AnchorIds.InstrumentSlot(tag.instrumentId)) : null;
            if (slot)
            {
                yield return Walk(c, NearPoint(slot.position, 0.42f), slot);
                ik.ReachTo(slot.position + Vector3.up * 0.04f); yield return new WaitForSeconds(0.45f);
            }
            c.PutBack(slot); heldTool = null;
            ik.Release(); yield return new WaitForSeconds(0.2f);
        }

        IEnumerator ToolMotion(SurgicalCharacter c, string toolId, Collider target, string motion)
        {
            if (!heldTool || !target) yield break;
            yield return Walk(c, WorkSpot(target.bounds.center), target.transform);
            var tip = heldTool.GetComponent<SurgeryInstrumentTag>()?.tip ?? heldTool;
            var b = target.bounds; var top = new Vector3(b.center.x, b.max.y, b.center.z);
            var toward = Vector3.ProjectOnPlane(b.center - c.transform.position, Vector3.up).normalized;
            Vector3 down = (Vector3.down * 0.85f + toward * 0.5f).normalized;
            List<Vector3> path = PathFor(toolId, target, motion);
            if (toolId == InstrumentIds.SkinMarker) SiteMarkingGuide.DemoPen = true;
            // approach above the start, touch down, follow the path, lift
            float t = 0; var start = path[0];
            ik.ReachWithTool(heldTool, tip, start + Vector3.up * 0.08f, down); yield return new WaitForSeconds(0.6f);
            for (t = 0; t < 1f; t += Time.deltaTime / 0.35f) { ik.ReachWithTool(heldTool, tip, Vector3.Lerp(start + Vector3.up * 0.08f, start, t), down); yield return null; }
            float dur = motion == "trace" ? 3.0f : motion == "saw" ? 2.4f : motion == "tap" ? 1.6f : 1.8f;
            for (t = 0; t < dur; t += Time.deltaTime)
            {
                float u = t / dur; Vector3 p;
                switch (motion)
                {
                    case "trace": p = Along(path, u); break;
                    case "saw": p = start + toward * (0.012f * Mathf.Sin(t * 40f)) + Vector3.down * (0.01f * u); break;        // oscillating through the guide slot
                    case "tap": p = start + Vector3.up * (0.015f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.5f))); break;          // impaction taps
                    case "retract": p = start - toward * (0.025f * Mathf.SmoothStep(0, 1, u)); break;                          // pull the edge aside
                    default: p = start; break;                                                                               // hold (guide)
                }
                ik.ReachWithTool(heldTool, tip, p, down);
                if (toolId == InstrumentIds.SkinMarker) SiteMarkingGuide.DemoTip = tip.position;
                yield return null;
            }
            SiteMarkingGuide.DemoTip = null;
            for (t = 0; t < 1f; t += Time.deltaTime / 0.35f) { ik.ReachWithTool(heldTool, tip, Vector3.Lerp(path[path.Count - 1], path[path.Count - 1] + Vector3.up * 0.1f, t), down); yield return null; }
            ik.Release();
        }

        IEnumerator CarryImplant(SurgicalCharacter c, SurgeryImplantTag imp)
        {
            var seat = GameObject.Find("PlacementTarget_" + imp.implantId)?.transform; if (!seat) yield break;
            yield return Walk(c, NearPoint(imp.transform.position, 0.55f), imp.transform);
            ik.ReachTo(imp.transform.position + Vector3.up * 0.03f); yield return new WaitForSeconds(0.55f);
            var rb = imp.GetComponent<Rigidbody>(); if (rb) { rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
            var hand = ik.Hand ? ik.Hand : c.transform;
            imp.transform.SetParent(hand, true);
            ik.Release(); yield return new WaitForSeconds(0.3f);
            yield return Walk(c, WorkSpot(seat.position), seat);
            imp.transform.SetParent(null, true);
            Vector3 p0 = imp.transform.position; Quaternion r0 = imp.transform.rotation;
            for (float t = 0; t < 1f; t += Time.deltaTime / 1.2f)
            {
                float e = Mathf.SmoothStep(0, 1, t);
                imp.transform.SetPositionAndRotation(Vector3.Lerp(p0, seat.position, e), Quaternion.Slerp(r0, seat.rotation, e));
                ik.ReachTo(imp.transform.position + Vector3.up * 0.02f); yield return null;
            }
            imp.transform.SetPositionAndRotation(seat.position, seat.rotation);
            ik.Release(); yield return new WaitForSeconds(0.25f);
        }

        IEnumerator Walk(SurgicalCharacter c, Vector3 to, Transform face)
        {
            bool fin = false; c.GoTo(to, _ => fin = true, face);
            float t0 = Time.time; while (!fin && Time.time - t0 < 14f) yield return null;
            if (face) c.LookAt(face);
            yield return new WaitForSeconds(0.25f);
        }

        // ------------------------------------------------------------------ geometry
        /// <summary>A standing spot beside the table, close enough to reach the point, not between the student and the knee.</summary>
        Vector3 WorkSpot(Vector3 point)
        {
            var student = Camera.main ? Camera.main.transform.position : point - Vector3.right;
            var candidates = new[] { new Vector3(-0.48f, 0, 0.28f), new Vector3(-0.5f, 0, 0.42f), new Vector3(-0.58f, 0, 0.32f), new Vector3(-0.6f, 0, 0.5f), new Vector3(-0.5f, 0, -0.38f), new Vector3(-0.55f, 0, -0.42f), new Vector3(0.72f, 0, 0.2f), new Vector3(0.7f, 0, -0.1f), new Vector3(-0.62f, 0, 0.05f) };
            Vector3 best = point; float bestScore = float.MaxValue;
            foreach (var off in candidates)
            {
                var p = new Vector3(point.x, 0, point.z) + off;
                if (!NavMesh.SamplePosition(p, out var hit, 0.15f, NavMesh.AllAreas)) continue;
                var q = hit.position; float reach = Flat(q, point); float toStudent = Flat(q, student);
                var line = Flat(student, point); var proj = Vector3.Project(Vector3.ProjectOnPlane(q - student, Vector3.up), Vector3.ProjectOnPlane(point - student, Vector3.up));
                float offLine = Flat(student + proj, q);
                float score = reach * 4f + (toStudent < 0.75f ? 10f : 0f) + (offLine < 0.35f && proj.magnitude < line ? 6f : 0f);
                if (score < bestScore) { bestScore = score; best = q; }
            }
            return best;
        }
        /// <summary>A reachable standing spot about <paramref name="dist"/> from a point, on the side nearest the demonstrator.</summary>
        Vector3 NearPoint(Vector3 point, float dist)
        {
            var from = Demonstrator ? Demonstrator.transform.position : point;
            Vector3 best = new Vector3(point.x, 0, point.z); float bestScore = float.MaxValue;
            for (float d = dist; d <= dist + 0.3f; d += 0.15f)
                for (int i = 0; i < 16; i++)
                {
                    var p = new Vector3(point.x, 0, point.z) + Quaternion.AngleAxis(i * 22.5f, Vector3.up) * Vector3.forward * d;
                    if (!NavMesh.SamplePosition(p, out var hit, 0.12f, NavMesh.AllAreas)) continue;
                    float score = Flat(hit.position, point) * 3f + Flat(hit.position, from);
                    if (score < bestScore) { bestScore = score; best = hit.position; }
                }
            return best;
        }
        static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

        List<Vector3> PathFor(string toolId, Collider target, string motion)
        {
            var g = SiteMarkingGuide.Instance;
            if (motion == "trace" && g && g.GuideWorldPoints.Count > 1) return g.GuideWorldPoints.ToList();   // along the planned line on the skin
            var b = target.bounds; var top = new Vector3(b.center.x, b.max.y, b.center.z);
            if (motion == "trace") return new List<Vector3> { top - Vector3.forward * b.extents.z * 0.7f, top + Vector3.forward * b.extents.z * 0.7f };
            return new List<Vector3> { top };
        }
        static Vector3 Along(List<Vector3> pts, float u)
        {
            if (pts.Count == 1) return pts[0];
            float f = Mathf.Clamp01(u) * (pts.Count - 1); int i = Mathf.Min(pts.Count - 2, Mathf.FloorToInt(f));
            return Vector3.Lerp(pts[i], pts[i + 1], f - i);
        }

        /// <summary>The tool sits in the palm with its working tip along the fingers (instead of at the wrist pivot).</summary>
        void Grip(SurgicalCharacter c, Transform tool)
        {
            var anim = c.GetComponentInChildren<Animator>(); var hand = ik.Hand;
            if (!anim || !anim.isHuman || !hand) return;
            var mid = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal); var thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            if (!mid) return;
            var fingers = (mid.position - hand.position).normalized;
            var side = thumb ? Vector3.ProjectOnPlane(thumb.position - hand.position, fingers).normalized : hand.up;
            tool.SetParent(hand, true);
            tool.rotation = Quaternion.LookRotation(fingers, side);
            tool.position = hand.position + fingers * Vector3.Distance(hand.position, mid.position) * 0.75f;
        }

        // ------------------------------------------------------------------ helpers
        Collider TargetCollider(string id) => string.IsNullOrEmpty(id) ? null : FindObjectsByType<SurgeryAnatomyTag>(FindObjectsSortMode.None).FirstOrDefault(t => t.targetId == id)?.GetComponent<Collider>();
        static Transform FindTool(string id) { foreach (var t in FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None)) if (t.instrumentId == id) return t.transform; return null; }
        IEnumerable<SurgeryImplantTag> ImplantsFor(SurgicalObjective o)
        {
            var all = FindObjectsByType<SurgeryImplantTag>(FindObjectsSortMode.None);
            bool trial = o.implantId.Contains("trial");
            var ids = o.id == "obj.trial_reduction" ? new[] { ImplantIds.FemoralTrial, ImplantIds.TibialTrial }
                    : o.id == "obj.implant_tibial" ? new[] { ImplantIds.TibialTray, ImplantIds.PolyInsert } : new[] { o.implantId };
            foreach (var id in ids) { var t = all.FirstOrDefault(x => x.implantId == id); if (t) yield return t; }
        }
        string Name(string id) => Scenario?.Instrument(id)?.name.Get(Lang) ?? Scenario?.implants.FirstOrDefault(i => i.id == id)?.name.Get(Lang) ?? id;
        static string Motion(string tool) => tool == InstrumentIds.SkinMarker || tool == InstrumentIds.Scalpel || tool == InstrumentIds.SkinStapler || tool == InstrumentIds.NeedleDriver ? "trace"
            : tool == InstrumentIds.OscillatingSaw ? "saw" : tool == InstrumentIds.Retractor ? "retract" : tool == InstrumentIds.Mallet || tool == InstrumentIds.Impactor ? "tap" : "hold";
        static string ToolLineKey(string tool) => tool == InstrumentIds.SkinMarker ? "marker" : tool == InstrumentIds.Scalpel ? "scalpel" : tool == InstrumentIds.Retractor ? "retractor"
            : SurgeryInteractionValidator.IsCuttingGuide(tool) ? "guide" : tool == InstrumentIds.OscillatingSaw ? "saw" : tool == InstrumentIds.SkinStapler || tool == InstrumentIds.NeedleDriver ? "close" : "generic";

        void Say(SurgicalCharacter c, string text) { if (bridge && bridge.controller && !string.IsNullOrEmpty(text)) { Log.Add("say " + c.characterId + ": " + text); bridge.controller.Say(c.characterId, text); } }
        IEnumerator WaitSpeech() { float t0 = Time.time; yield return null; while (bridge && bridge.controller && !bridge.controller.SpeechIdle && Time.time - t0 < 25f) yield return null; }
        void Publish(string reason)
        {
            var host = SurgeryEventBusHost.Instance; if (!host) return;
            host.Bus.Publish(new SurgeryEvent(SurgeryEventIds.HintGiven, SurgeryEventSources.AI) { characterId = Demonstrator ? Demonstrator.characterId : "", objectiveId = bridge.mechanics.ScenarioEngine?.CurrentObjectiveId ?? "", reason = reason });
        }

        /// <summary>Stops the demonstration now and puts everything back (student asked, left the room, or a new request).</summary>
        public void Abort(string why, bool sayYourTurn)
        {
            if (!Running) return;
            StopCoroutine(run); run = null; CurrentObjective = "";
            SiteMarkingGuide.DemoPen = false; SiteMarkingGuide.DemoTip = null;
            if (ik) ik.Release();
            var c = Demonstrator;
            if (heldTool && c) { var tag = heldTool.GetComponent<SurgeryInstrumentTag>(); c.PutBack(tag ? SurgeryAnchor.Find(AnchorIds.InstrumentSlot(tag.instrumentId)) : null); }
            heldTool = null;
            if (snap != null && bridge && bridge.mechanics) snap.Restore(bridge.mechanics);
            SiteMarkingGuide.Instance?.ResetDrawing();
            if (sayYourTurn && c) { Say(c, Line("your_turn")); c.GoToAnchor(c.homeAnchor, _ => { }, Camera.main ? Camera.main.transform : null); }
            Log.Add("aborted: " + why);
            Finish(false, why);
        }

        void Finish(bool ok, string why)
        {
            run = null;
            var core = bridge && bridge.controller ? bridge.controller.Core : null; if (core != null) core.Supervisor.Demonstrating = false;
            var d = done; done = null; d?.Invoke(ok, why);
        }

        // ------------------------------------------------------------------ approved narration (simulation-level, no parameters)
        static readonly Dictionary<string, (string en, string ar)> Lines = new Dictionary<string, (string, string)>
        {
            ["open_step"] = ("Watch me first. I'll do this step slowly, then it's your turn.", "تابعني أول. رح أعمل هالخطوة على مهل، وبعدين دورك."),
            ["open_full"] = ("I'll take you through the whole procedure, one step at a time. Watch what I do and why.", "رح أمشي معك بالعملية كاملة، خطوة خطوة. ركّز على شو بعمل وليش."),
            ["marker"] = ("I take the {0} and draw a clear mark on the front of the right knee, along the planned site.", "بمسك {0} وبرسم علامة واضحة على مقدّمة الركبة اليمنى، على مكان العملية المخطط."),
            ["scalpel"] = ("Now the {0}: one smooth, straight stroke along the marked line over the front of the knee.", "هلّق {0}: بشق واحد ناعم ومستقيم على الخط المعلَّم فوق مقدّمة الركبة."),
            ["retractor"] = ("With the {0} I hold the edges apart gently, so the joint capsule can be seen.", "بالـ{0} بفتح طرفي الجرح بلطف حتى تبيّن محفظة المفصل."),
            ["guide"] = ("First the {0}, positioned on the bone so the cut is guided.", "أول إشي {0}، بثبّته على العظم حتى يكون القطع موجَّه."),
            ["saw"] = ("Then the {0}, through the slot of the guide, with steady, controlled movements.", "بعدين {0}، من خلال فتحة الدليل، بحركات ثابتة ومتحكَّم فيها."),
            ["implant"] = ("Now the {0}: I carry it in and seat it on the prepared bone.", "هلّق {0}: بجيبها وبركّبها على العظم المحضَّر."),
            ["impact"] = ("I seat it fully with the {0}, using light, controlled taps.", "بثبّتها تماماً باستخدام {0}، بضربات خفيفة ومتحكَّم فيها."),
            ["close"] = ("Finally, I close the wound along its whole length with the {0}.", "بالآخر، بسكّر الجرح على طوله كامل باستخدام {0}."),
            ["generic"] = ("I use the {0} on the target for this step.", "بستخدم {0} على المكان المطلوب بهالخطوة."),
            ["your_turn"] = ("Now it's your turn: do the same, and I'll guide you.", "هلّق دورك: اعمل نفس الإشي، وأنا معك."),
            ["your_turn_full"] = ("That's the whole procedure. Now it's your turn, step by step. I'll be right here.", "هاي كانت العملية كاملة. هلّق دورك، خطوة خطوة، وأنا جنبك."),
        };
        string Line(string key, string arg = "")
        {
            if (!Lines.TryGetValue(key, out var l)) return "";
            var s = Lang == "ar" ? l.ar : l.en; return s.Contains("{0}") ? string.Format(s, arg) : s;
        }

        // ------------------------------------------------------------------ restore point
        /// <summary>Everything a demonstration may touch: anatomy states, instrument and implant poses/parents.</summary>
        class Snapshot
        {
            readonly Dictionary<string, string> states = new Dictionary<string, string>();
            readonly List<(Transform t, Transform parent, Vector3 p, Quaternion r, bool kinematic, RigidbodyInterpolation interp)> poses = new List<(Transform, Transform, Vector3, Quaternion, bool, RigidbodyInterpolation)>();
            public static Snapshot Take(SurgeryMechanicsRoot m)
            {
                var s = new Snapshot();
                var k = m.GetComponentInChildren<KneeAnatomyStateController>(true) is var kc && kc ? kc : FindAnyObjectByType<KneeAnatomyStateController>();
                if (k) foreach (var kv in k.Targets) s.states[kv.Key] = kv.Value.CurrentState;
                foreach (var t in FindObjectsByType<SurgeryImplantTag>(FindObjectsSortMode.None)) s.Add(t.transform);
                foreach (var t in FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None)) s.Add(t.transform);
                return s;
            }
            void Add(Transform t) { var rb = t.GetComponent<Rigidbody>(); poses.Add((t, t.parent, t.position, t.rotation, rb && rb.isKinematic, rb ? rb.interpolation : RigidbodyInterpolation.None)); }
            static bool HeldByStudent(Transform t)
            {
                var d = FindAnyObjectByType<SurgicalDesktopInteractionAdapter>(); if (d && d.isActiveAndEnabled && d.HeldObject == t) return true;
                var g = t.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(); return g && g.isSelected;
            }
            public void Restore(SurgeryMechanicsRoot m)
            {
                var k = m.GetComponentInChildren<KneeAnatomyStateController>(true) is var kc && kc ? kc : FindAnyObjectByType<KneeAnatomyStateController>();
                if (k) foreach (var kv in states) k.SetTargetState(kv.Key, kv.Value);
                foreach (var (t, parent, p, r, kin, interp) in poses)
                {
                    if (!t || HeldByStudent(t)) continue; t.SetParent(parent, true); t.SetPositionAndRotation(p, r);
                    var rb = t.GetComponent<Rigidbody>(); if (rb) { rb.interpolation = interp; rb.position = p; rb.rotation = r; rb.isKinematic = kin; if (!kin) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
                }
            }
        }
    }
}
