using System;
using System.Collections.Generic;
using MedSimAI.Surgery.Shared;
using UnityEngine;

namespace MedSimAI.Surgery.Anatomy
{
    /// <summary>
    /// Root of KneeAnatomy_R.prefab. Composes the visible anatomy from the states of all <see cref="AnatomyTargetStateView"/>s:
    ///   site_marking  intact | marked      → marker line on the skin
    ///   incision_line intact | incised     → fresh incision line
    ///   joint_capsule intact | exposed     → skin window + wound edges, anterior soft tissue hidden, patella retracted laterally
    ///   distal_femur  intact | guide_placed | resected | trial_fitted | implanted
    ///   proximal_tibia intact | guide_placed | resected | trial_fitted | implanted
    ///   patella       intact | exposed     → patella group retracted (also implied by an exposed capsule)
    ///   wound         intact | closed      → skin closed with staples
    /// All resection geometry is an ILLUSTRATIVE EDUCATIONAL PLACEHOLDER (DRAFT — NOT CLINICALLY VALIDATED).
    /// No surgical logic here: mechanics call IAnatomyStateView.SetState on the targets; this only swaps visuals.
    /// </summary>
    [DisallowMultipleComponent]
    public class KneeAnatomyStateController : MonoBehaviour
    {
        [Header("Bones (intact / resected variants share pivots)")]
        public GameObject distalFemur; public GameObject distalFemurResected; public GameObject femoralCartilage;
        public GameObject proximalTibia; public GameObject proximalTibiaResected; public GameObject tibialCartilage;
        [Tooltip("Removed with the tibial preparation (educational simplification).")]
        public GameObject[] removedWithTibialResection = new GameObject[0];
        [Header("Skin and wound")]
        public GameObject skinIntact; public GameObject skinOpen; public GameObject siteMarking; public GameObject incisionLine; public GameObject closureLine; public GameObject skinStaples;
        [Tooltip("Hidden while the joint is exposed (anterior capsule, fat pad, retinacula, bursa, quadriceps belly) — educational simplification.")]
        public GameObject[] hiddenWhenExposed = new GameObject[0];
        [Header("Patella")]
        public Transform patellaGroup; public Transform patellaRetractedPose;
        [Min(0)] public float patellaBlendSeconds = 0.5f;
        [Header("Placeholder state visuals")]
        public GameObject guidePinsFemur; public GameObject guidePinsTibia;
        [Tooltip("Show built-in trial / implant visuals for the trial_fitted / implanted states. OFF in production: mechanics place the real implant prefabs at PlacementTarget_<implantId>.")]
        public bool showComponentVisuals;
        [Tooltip("Performance: while the intact skin fully encloses the joint, switch off the renderers of the deep anatomy (colliders stay active). Saves ~150k triangles per eye.")]
        public bool cullEnclosedAnatomy = true;
        [Tooltip("Groups whose renderers are culled while enclosed (Bones, Cartilage, Menisci, Ligaments, Tendons, SoftTissue).")]
        public Transform[] enclosedGroups = new Transform[0];
        public GameObject femoralTrialVisual, femoralComponentVisual, tibialTrialVisual, tibialTrayVisual, polyInsertVisual;

        readonly Dictionary<string, AnatomyTargetStateView> views = new Dictionary<string, AnatomyTargetStateView>();
        Vector3 patellaRestPos; Quaternion patellaRestRot; bool patellaRestKnown; float patellaT, patellaTarget;

        public event Action<string, string> TargetStateChanged;   // (targetId, state)

        public IReadOnlyDictionary<string, AnatomyTargetStateView> Targets { get { Collect(); return views; } }

        void Awake() { Collect(); CachePatella(); Refresh(true); }

        void Collect()
        {
            if (views.Count > 0) return;
            foreach (var v in GetComponentsInChildren<AnatomyTargetStateView>(true)) if (!string.IsNullOrEmpty(v.TargetId)) views[v.TargetId] = v;
        }
        void CachePatella()
        {
            if (patellaRestKnown || !patellaGroup) return;
            patellaRestPos = patellaGroup.localPosition; patellaRestRot = patellaGroup.localRotation; patellaRestKnown = true;
        }

        public string GetState(string targetId) { Collect(); return views.TryGetValue(targetId, out var v) ? v.CurrentState : null; }
        public AnatomyTargetStateView GetTarget(string targetId) { Collect(); views.TryGetValue(targetId, out var v); return v; }

        /// <summary>Convenience for tools/tests: forwards to the target's IAnatomyStateView.SetState.</summary>
        public bool SetTargetState(string targetId, string state) { var v = GetTarget(targetId); return v != null && v.SetState(state); }

        /// <summary>Every target back to its first supported state ("intact"); patella snaps home.</summary>
        public void ResetAll()
        {
            Collect();
            foreach (var v in views.Values) v.ResetState();
            patellaT = patellaTarget = 0; Refresh(true);
        }

        internal void OnTargetStateChanged(AnatomyTargetStateView v) { Refresh(false); TargetStateChanged?.Invoke(v.TargetId, v.CurrentState); }

        static bool Is(string s, params string[] any) { foreach (var a in any) if (s == a) return true; return false; }
        static void Show(GameObject g, bool on) { if (g && g.activeSelf != on) g.SetActive(on); }

        /// <summary>Re-evaluates every visual from the current target states.</summary>
        public void Refresh(bool instant)
        {
            Collect(); CachePatella();
            string S(string id) => GetState(id) ?? AnatomyStates.Intact;
            bool marked = S(AnatomyTargetIds.SiteMarking) == AnatomyStates.Marked;
            bool incised = S(AnatomyTargetIds.IncisionLine) == AnatomyStates.Incised;
            bool exposedCaps = S(AnatomyTargetIds.JointCapsule) == AnatomyStates.Exposed;
            bool closed = S(AnatomyTargetIds.Wound) == AnatomyStates.Closed;
            bool open = exposedCaps && !closed;
            string fem = S(AnatomyTargetIds.DistalFemur), tib = S(AnatomyTargetIds.ProximalTibia);
            bool femCut = Is(fem, AnatomyStates.Resected, AnatomyStates.TrialFitted, AnatomyStates.Implanted);
            bool tibCut = Is(tib, AnatomyStates.Resected, AnatomyStates.TrialFitted, AnatomyStates.Implanted);

            Show(skinOpen, open); Show(skinIntact, !open);
            Show(siteMarking, marked && !incised && !open && !closed);
            Show(incisionLine, incised && !open && !closed);
            Show(closureLine, closed); Show(skinStaples, closed);
            foreach (var g in hiddenWhenExposed) Show(g, !open);

            Show(distalFemur, !femCut); Show(distalFemurResected, femCut); Show(femoralCartilage, !femCut);
            Show(proximalTibia, !tibCut); Show(proximalTibiaResected, tibCut); Show(tibialCartilage, !tibCut);
            foreach (var g in removedWithTibialResection) Show(g, !tibCut);
            Show(guidePinsFemur, fem == AnatomyStates.GuidePlaced); Show(guidePinsTibia, tib == AnatomyStates.GuidePlaced);

            bool cv = showComponentVisuals;
            Show(femoralTrialVisual, cv && fem == AnatomyStates.TrialFitted); Show(femoralComponentVisual, cv && fem == AnatomyStates.Implanted);
            Show(tibialTrialVisual, cv && tib == AnatomyStates.TrialFitted); Show(tibialTrayVisual, cv && tib == AnatomyStates.Implanted);
            Show(polyInsertVisual, cv && tib == AnatomyStates.Implanted);

            patellaTarget = (open || S(AnatomyTargetIds.Patella) == AnatomyStates.Exposed) && !closed ? 1f : 0f;
            if (instant || patellaBlendSeconds <= 0) { patellaT = patellaTarget; ApplyPatella(); }
            ApplyEnclosureCulling();
        }

        /// <summary>True while the intact skin is shown and rendered (the deep anatomy cannot be seen).</summary>
        public bool AnatomyEnclosed
        {
            get { if (!skinIntact || !skinIntact.activeInHierarchy) return false; var r = skinIntact.GetComponent<Renderer>(); return r && r.enabled; }
        }
        /// <summary>Re-applies renderer culling (call after toggling skin renderers from tools).</summary>
        public void ApplyEnclosureCulling()
        {
            bool off = cullEnclosedAnatomy && AnatomyEnclosed;
            foreach (var g in enclosedGroups) if (g) foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = off;
        }

        void Update()
        {
            if (!patellaGroup || Mathf.Approximately(patellaT, patellaTarget)) return;
            patellaT = Mathf.MoveTowards(patellaT, patellaTarget, Time.deltaTime / Mathf.Max(0.01f, patellaBlendSeconds));
            ApplyPatella();
        }

        void ApplyPatella()
        {
            if (!patellaGroup || !patellaRetractedPose || !patellaRestKnown) return;
            float t = Mathf.SmoothStep(0, 1, patellaT);
            patellaGroup.localPosition = Vector3.Lerp(patellaRestPos, patellaRetractedPose.localPosition, t);
            patellaGroup.localRotation = Quaternion.Slerp(patellaRestRot, patellaRetractedPose.localRotation, t);
        }
    }
}
