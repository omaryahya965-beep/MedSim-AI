using MedSimAI.Clinical;
using MedSimAI.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MedSimAI.Instruments
{
    /// <summary>
    /// Base for grabbable instruments. Detects valid placement zones from the instrument's probe point,
    /// gives visual + haptic feedback, and reports exams to the ClinicalScenarioManager (the only source of results).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public abstract class MedicalInstrumentBase : MonoBehaviour
    {
        public abstract string InstrumentId { get; }
        public Transform probePoint;
        public float probeRadius = 0.06f;
        public LayerMask zoneMask = ~0;
        [Tooltip("Axis on the probe that must face into the zone (e.g. stethoscope diaphragm normal).")] public Vector3 probeAxis = Vector3.forward;
        public AudioSource sfx;
        public AudioClip pickupClip, confirmClip;

        protected XRGrabInteractable grab;
        protected Rigidbody body;
        protected IXRSelectInteractor holder;
        protected PlacementZone hoverZone;
        protected Vector3 homePos; protected Quaternion homeRot; Transform homeParent;
        readonly Collider[] hits = new Collider[8];

        public bool IsHeld => holder != null;
        protected ClinicalSession Session => ClinicalScenarioManager.Instance.Session;

        protected virtual void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnRelease);
            homePos = transform.position; homeRot = transform.rotation; homeParent = transform.parent;
            if (!probePoint) probePoint = transform;
            if (!pickupClip) pickupClip = MedSimAI.Audio.ProceduralClinicalAudio.Click();
            if (!confirmClip) confirmClip = MedSimAI.Audio.ProceduralClinicalAudio.Beep(1320, 0.07f);
        }

        protected virtual void OnGrab(SelectEnterEventArgs a)
        {
            holder = a.interactorObject;
            RecordEvent(ClinicalEvents.InstrumentPicked, InstrumentId);
            Detach();
            ShowGuidance(true);
            Play(pickupClip);
            Haptic(0.25f, 0.05f);
        }

        protected virtual void OnRelease(SelectExitEventArgs a)
        {
            holder = null;
            ShowGuidance(false);
            if (hoverZone) hoverZone.SetHighlight(false, false);
            if (hoverZone && hoverZone.Accepts(InstrumentId) && SupportsSnap) { RecordEvent(ClinicalEvents.InstrumentOnPatient, InstrumentId + "@" + hoverZone.zoneId); SnapTo(hoverZone); OnPlaced(hoverZone); }
            else if (!hoverZone && NearWrongZone()) RecordEvent(ClinicalEvents.ZoneRejected, InstrumentId);
        }

        /// <summary>Observation event for the SupervisorDirector (not scored). Safe when no session is running.</summary>
        protected void RecordEvent(string id, string detail)
        {
            var m = ClinicalScenarioManager.Instance; if (!m || m.Session == null) return;
            m.Session.Record(id, detail);
        }

        /// <summary>True when the probe is on the patient but over a zone that does not accept this instrument.</summary>
        public bool NearWrongZone()
        {
            int n = Physics.OverlapSphereNonAlloc(probePoint.position, probeRadius * 1.5f, hits, zoneMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++) { var z = hits[i].GetComponent<PlacementZone>(); if (z && !z.Accepts(InstrumentId)) return true; }
            return false;
        }

        protected virtual bool SupportsSnap => false;
        protected virtual void OnPlaced(PlacementZone zone) { }
        protected virtual void OnDetached() { }

        protected virtual void Update()
        {
            if (!IsHeld) return;
            var zone = FindZone();
            if (zone != hoverZone)
            {
                if (hoverZone) hoverZone.SetHighlight(IsHeld, false); // back to faint guidance
                hoverZone = zone;
                if (hoverZone) { hoverZone.SetHighlight(true, IsAligned(hoverZone)); Haptic(0.15f, 0.03f); }
            }
            else if (hoverZone) hoverZone.SetHighlight(true, IsAligned(hoverZone));
            WhileHeld(hoverZone);
        }

        protected virtual void WhileHeld(PlacementZone zone) { }

        /// <summary>Closest accepting zone overlapping the probe point (public for automated tests).</summary>
        public PlacementZone FindZone()
        {
            int n = Physics.OverlapSphereNonAlloc(probePoint.position, probeRadius, hits, zoneMask, QueryTriggerInteraction.Collide);
            PlacementZone best = null; float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var z = hits[i].GetComponent<PlacementZone>();
                if (!z || !z.Accepts(InstrumentId)) continue;
                float d = (hits[i].ClosestPoint(probePoint.position) - probePoint.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = z; }
            }
            return best;
        }

        public bool IsAligned(PlacementZone z)
        {
            if (z.maxAngle >= 180) return true;
            var axis = probePoint.TransformDirection(probeAxis);
            return Vector3.Angle(axis, -z.transform.forward) <= z.maxAngle || Vector3.Angle(axis, z.transform.forward) <= z.maxAngle;
        }

        protected void SnapTo(PlacementZone z)
        {
            var target = z.snapPoint ? z.snapPoint : z.transform;
            if (body) { body.isKinematic = true; body.useGravity = false; }
            transform.SetParent(target, true);
            z.Occupied = true; attachedZone = z;
            if (snapRoutine != null) StopCoroutine(snapRoutine);
            if (isActiveAndEnabled && Application.isPlaying) snapRoutine = StartCoroutine(EaseTo(target)); else transform.SetPositionAndRotation(target.position, target.rotation);
            Play(confirmClip);
            Haptic(0.5f, 0.08f);
        }
        protected PlacementZone attachedZone;
        Coroutine snapRoutine;

        /// <summary>Short eased snap so placement reads as a physical action rather than a teleport.</summary>
        System.Collections.IEnumerator EaseTo(Transform target)
        {
            Vector3 p0 = transform.position; Quaternion r0 = transform.rotation;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.18f)
            {
                float e = 1 - (1 - t) * (1 - t);
                transform.SetPositionAndRotation(Vector3.Lerp(p0, target.position, e), Quaternion.Slerp(r0, target.rotation, e));
                yield return null;
            }
            transform.SetPositionAndRotation(target.position, target.rotation); snapRoutine = null;
        }

        /// <summary>Placement guidance: while held, every zone that accepts this instrument glows faintly.</summary>
        protected void ShowGuidance(bool on)
        {
            foreach (var z in Object.FindObjectsByType<PlacementZone>(FindObjectsSortMode.None))
                if (z.Accepts(InstrumentId) && z != hoverZone) z.SetHighlight(on, false);
        }

        /// <summary>Test/automation hook: performs the same snap + placement flow as releasing the instrument in a zone.</summary>
        public void PlaceInZone(PlacementZone z)
        {
            if (!z) return;
            if (!z.Accepts(InstrumentId)) { RecordEvent(ClinicalEvents.ZoneRejected, InstrumentId); return; }
            if (SupportsSnap) { RecordEvent(ClinicalEvents.InstrumentOnPatient, InstrumentId + "@" + z.zoneId); SnapTo(z); OnPlaced(z); }
        }

        protected void Detach()
        {
            if (attachedZone) { attachedZone.Occupied = false; attachedZone = null; transform.SetParent(homeParent, true); OnDetached(); }
        }

        public virtual void ResetInstrument()
        {
            Detach();
            if (grab.isSelected && grab.interactionManager) grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            transform.SetParent(homeParent, true);
            transform.SetPositionAndRotation(homePos, homeRot);
            if (body) { body.isKinematic = true; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }

        protected void Play(AudioClip c) { if (sfx && c) sfx.PlayOneShot(c); }

        protected void Haptic(float amp, float dur)
        {
            if (holder is XRBaseInputInteractor input) input.SendHapticImpulse(amp, dur);
        }
    }
}
