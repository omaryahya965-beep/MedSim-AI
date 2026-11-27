using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MedSimAI.Instruments
{
    /// <summary>
    /// Physical hinged hospital door. The student grabs the lever handle: the lever rotates down (releasing the latch),
    /// then the leaf follows the hand around the hinge axis (push or pull). The leaf has a solid collider moved by a
    /// kinematic rigidbody so the player cannot walk through it; it keeps a little momentum with friction after release.
    /// Sounds: latch click, swing (scaled by angular speed), close thud. Haptic pulse on unlatch and when fully open.
    /// </summary>
    public class HospitalDoor : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Pivot on the hinge axis (vertical). The leaf is its child.")] public Transform hinge;
        public Transform leaf;
        [Tooltip("Lever handle (child of the leaf); rotates around its local X when pressed.")] public Transform handle;
        public XRSimpleInteractable handleInteractable;
        [Tooltip("+1 opens the leaf in the hinge's positive yaw direction, -1 negative.")] public float openSign = 1f;
        public float maxAngle = 100f, leverDownAngle = 32f, friction = 140f, followGain = 14f;
        [Header("Audio")]
        public AudioSource source; public AudioClip latchClip, swingClip, closeClip;
        public event Action Opened;
        public float Angle { get; private set; }
        public bool Latched { get; private set; } = true;
        public bool IsOpen => Angle > 35f;

        IXRSelectInteractor holder; float autoTarget = -1f; float velocity, leverT, grabOffsetDeg; bool openedFired; Quaternion handleRest; Rigidbody body;
        Quaternion hingeRest, leafRestLocal; Vector3 leafRestLocalPos;

        void Awake()
        {
            if (!hinge) hinge = transform;
            hingeRest = hinge.localRotation;
            if (handle) handleRest = handle.localRotation;
            if (leaf) { leafRestLocal = leaf.localRotation; leafRestLocalPos = leaf.localPosition; body = leaf.GetComponent<Rigidbody>(); if (!body) body = leaf.gameObject.AddComponent<Rigidbody>(); body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate; }
            if (handleInteractable) { handleInteractable.selectEntered.AddListener(OnGrab); handleInteractable.selectExited.AddListener(OnRelease); }
        }

        void OnGrab(SelectEnterEventArgs a)
        {
            holder = a.interactorObject;
            grabOffsetDeg = HandAngle() - Angle;
        }
        void OnRelease(SelectExitEventArgs a) { holder = null; }

        /// <summary>Signed opening angle of the hand around the hinge, relative to the closed leaf direction.</summary>
        float HandAngle()
        {
            if (holder == null) return Angle;
            var p = holder.GetAttachTransform(handleInteractable).position;
            var parent = hinge.parent;
            Vector3 local = parent ? parent.InverseTransformPoint(p) : p;
            Vector3 pivot = parent ? parent.InverseTransformPoint(hinge.position) : hinge.position;
            Vector3 closedDir = hingeRest * Vector3.right; // leaf extends along the hinge's local +X when closed
            Vector3 v = local - pivot; v.y = 0; closedDir.y = 0;
            if (v.sqrMagnitude < 1e-4f) return Angle;
            float a = Vector3.SignedAngle(closedDir, v, Vector3.up);
            return a * openSign;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // Lever: pressed while held (latch releases once the lever is mostly down).
            leverT = Mathf.MoveTowards(leverT, holder != null ? 1f : 0f, dt / 0.16f);
            if (handle) handle.localRotation = handleRest * Quaternion.Euler(-leverDownAngle * Smooth(leverT), 0, 0);
            if (Latched && leverT > 0.8f) { Latched = false; Play(latchClip, 0.9f); Haptic(0.45f, 0.06f); }

            float target = Angle;
            if (holder != null && !Latched) target = Mathf.Clamp(HandAngle() - grabOffsetDeg, 0f, maxAngle);
            if (holder != null && !Latched) velocity = Mathf.Lerp(velocity, (target - Angle) * followGain, 1 - Mathf.Exp(-12f * dt));
            else if (autoTarget >= 0f) { velocity = Mathf.Lerp(velocity, (autoTarget - Angle) * 2.2f, 1 - Mathf.Exp(-6f * dt)); if (Mathf.Abs(autoTarget - Angle) < 0.5f) autoTarget = -1f; }
            else velocity = Mathf.MoveTowards(velocity, 0, friction * dt);

            float prev = Angle;
            Angle = Mathf.Clamp(Angle + velocity * dt, 0f, maxAngle);
            if (Angle <= 0f && prev > 0.5f) { Play(closeClip, Mathf.Clamp01(Mathf.Abs(velocity) / 60f + 0.6f)); velocity = 0; Latched = true; }
            if (Angle >= maxAngle && prev < maxAngle) { velocity = 0; Haptic(0.25f, 0.04f); }
            ApplyAngle();

            // Swing sound follows angular speed.
            if (source && swingClip)
            {
                float sp = Mathf.Abs(Angle - prev) / Mathf.Max(dt, 1e-4f);
                if (sp > 8f && !source.isPlaying) { source.clip = swingClip; source.loop = true; source.Play(); }
                source.volume = Mathf.Lerp(source.volume, Mathf.Clamp01(sp / 90f) * 0.7f, dt * 8f);
                if (sp < 3f && source.isPlaying && source.clip == swingClip && source.volume < 0.05f) source.Stop();
            }
            if (!openedFired && IsOpen) { openedFired = true; Opened?.Invoke(); }
        }

        static float Smooth(float t) => t * t * (3 - 2 * t);

        void ApplyAngle()
        {
            var rot = hingeRest * Quaternion.Euler(0, Angle * openSign, 0);
            hinge.localRotation = rot;
            if (leaf) { leaf.localRotation = leafRestLocal; leaf.localPosition = leafRestLocalPos; } // exact pose from the hinge; never accumulate the body's interpolation lag
            if (body) { body.MoveRotation(leaf.rotation); body.MovePosition(leaf.position); }
        }

        void Play(AudioClip c, float vol) { if (source && c) source.PlayOneShot(c, vol); }
        void Haptic(float a, float d) { if (holder is XRBaseInputInteractor i) i.SendHapticImpulse(a, d); }

        /// <summary>Automation / desktop hook: unlatches and swings the door open smoothly (used by tests and the desktop driver).</summary>
        public void OpenBy(float targetAngle = 85f) { Latched = false; Play(latchClip, 0.9f); autoTarget = Mathf.Clamp(targetAngle, 0, maxAngle); }

        public void ResetDoor()
        {
            Angle = 0; velocity = 0; autoTarget = -1f; Latched = true; openedFired = false; leverT = 0; holder = null;
            if (hinge) hinge.localRotation = hingeRest;
            if (handle) handle.localRotation = handleRest;
            // The leaf is an interpolated kinematic body: teleport it with the hinge, otherwise interpolation writes the old (open)
            // pose back onto the transform and the leaf stays visually open while Angle is 0.
            if (leaf) { leaf.localRotation = leafRestLocal; leaf.localPosition = leafRestLocalPos; if (body) { body.position = leaf.position; body.rotation = leaf.rotation; } }
        }
    }
}
