using System;
using System.Collections;
using MedSimAI.Characters;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;
using UnityEngine.AI;

namespace MedSimAI.Surgery.Characters
{
    /// <summary>
    /// Physical behaviour of a surgical character (surgeon or assistant): walk to anchors on the NavMesh (or glide if no
    /// NavMesh is baked yet), face a target, hold and offer an instrument, and report real outcomes. It never decides
    /// what to do: the validated action comes from SurgeryCharacterActuator.
    /// </summary>
    public class SurgicalCharacter : MonoBehaviour
    {
        public string characterId = SurgeryCharacterIds.Assistant;
        public string homeAnchor = AnchorIds.AssistantHome;
        public float walkSpeed = 1.1f, turnSpeed = 240f, arriveDistance = 0.35f, actionTimeout = 15f;
        public Transform student;            // player head (set by the session controller)
        public Transform handSocket;         // where a held instrument sits (right hand bone if found)

        public CharacterSpeechController Speech { get; private set; }
        public bool Busy { get; private set; }
        public Transform Held { get; private set; }
        Animator anim; NavMeshAgent agent; Coroutine current; Transform lookTarget;
        // Integration: when the body already has the clinical doctor's DoctorMovement (the persistent hospital doctor), all
        // walking/turning goes through it, so one navigation + animation lifecycle drives her in every room.
        DoctorMovement mover; bool hasCharacterBase; float nextFace;
        public bool UsesDoctorMovement => mover;

        void Awake()
        {
            Speech = GetComponentInChildren<CharacterSpeechController>();
            anim = GetComponentInChildren<Animator>();
            agent = GetComponent<NavMeshAgent>();
            if (!handSocket && anim && anim.isHuman) handSocket = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (!handSocket) handSocket = transform;
            mover = GetComponent<DoctorMovement>();
            hasCharacterBase = GetComponent<SimCharacterBase>();
        }

        void Update()
        {
            if (mover)
            {
                if (lookTarget && !IsMoving && Time.time >= nextFace) { mover.Face(lookTarget.position); nextFace = Time.time + 0.4f; }
            }
            else
            {
                if (lookTarget && !IsMoving)
                {
                    var d = lookTarget.position - transform.position; d.y = 0;
                    if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), turnSpeed * Time.deltaTime);
                }
                if (Has("Speed")) anim.SetFloat("Speed", IsMoving ? walkSpeed : 0f);
            }
            if (!hasCharacterBase && Has("Speaking")) anim.SetBool("Speaking", Speech && Speech.IsSpeaking);
        }

        System.Collections.Generic.HashSet<string> parameters;
        /// <summary>True when the Animator has this parameter (avoids per-frame warnings with other controllers).</summary>
        bool Has(string p)
        {
            if (!anim || !anim.runtimeAnimatorController) return false;
            if (parameters == null) { parameters = new System.Collections.Generic.HashSet<string>(); foreach (var x in anim.parameters) parameters.Add(x.name); }
            return parameters.Contains(p);
        }

        bool IsMoving => mover ? mover.IsMoving : agent && agent.enabled && agent.isOnNavMesh ? (agent.pathPending || agent.remainingDistance > Mathf.Max(arriveDistance, agent.stoppingDistance)) : gliding;
        bool gliding;

        public void Interrupt() { if (current != null) StopCoroutine(current); current = null; Busy = false; gliding = false; if (mover) mover.Stop(); else if (agent && agent.enabled && agent.isOnNavMesh) agent.ResetPath(); }

        public void LookAt(Transform t) { lookTarget = t; nextFace = 0; if (mover && t) mover.Face(t.position); }

        /// <summary>Walk to a world position; calls done(true) on arrival, done(false) on timeout / no path.</summary>
        public void GoTo(Vector3 target, Action<bool> done, Transform faceWhenThere = null)
        {
            Interrupt(); current = StartCoroutine(GoRoutine(target, done, faceWhenThere));
        }

        public void GoToAnchor(string anchorId, Action<bool> done, Transform faceWhenThere = null)
        {
            var a = SurgeryAnchor.Find(anchorId);
            if (!a) { done?.Invoke(false); return; }
            GoTo(a.position, done, faceWhenThere ? faceWhenThere : a);
        }

        IEnumerator GoRoutine(Vector3 target, Action<bool> done, Transform face)
        {
            Busy = true; lookTarget = null; float t0 = Time.time; bool ok;
            if (mover)
            {
                mover.GoToPoint(target, face ? face.position : (Vector3?)null);
                yield return null; yield return null;   // let the path start
                while (Time.time - t0 < actionTimeout && mover.IsMoving) yield return null;
                var a = transform.position; a.y = 0; var b = target; b.y = 0;
                ok = Vector3.Distance(a, b) <= arriveDistance + 0.15f;
            }
            else if (agent && agent.enabled && agent.isOnNavMesh)
            {
                agent.speed = walkSpeed; agent.stoppingDistance = arriveDistance * 0.8f;
                if (!agent.SetDestination(target)) { Busy = false; done?.Invoke(false); yield break; }
                while (Time.time - t0 < actionTimeout && (agent.pathPending || agent.remainingDistance > arriveDistance)) yield return null;
                ok = !agent.pathPending && agent.remainingDistance <= arriveDistance + 0.05f;
            }
            else
            {
                gliding = true; var flat = new Vector3(target.x, transform.position.y, target.z);
                while (Time.time - t0 < actionTimeout && Vector3.Distance(transform.position, flat) > arriveDistance)
                {
                    var dir = (flat - transform.position); dir.y = 0;
                    if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
                    transform.position = Vector3.MoveTowards(transform.position, flat, walkSpeed * Time.deltaTime);
                    yield return null;
                }
                gliding = false; ok = Vector3.Distance(transform.position, flat) <= arriveDistance + 0.05f;
            }
            lookTarget = face; Busy = false; current = null; done?.Invoke(ok);
        }

        /// <summary>Takes an instrument into the hand (assistant). Physics off while held so it cannot fall or be pushed.</summary>
        public bool Pick(Transform instrument)
        {
            if (!instrument) return false;
            var rb = instrument.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true;
            // Integration: XRI would otherwise put the instrument back under the assistant's hand when the student lets go.
            var grab = instrument.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(); if (grab) grab.retainTransformParent = false;
            instrument.SetParent(handSocket, true);
            instrument.localPosition = Vector3.zero; instrument.localRotation = Quaternion.identity;
            Held = instrument; if (Has("Point")) anim.SetTrigger("Point");
            return true;
        }

        /// <summary>Called when the player has taken the instrument (verified tool.grabbed): forget it without moving it.</summary>
        public void ReleaseToPlayer() { if (Held && Held.parent == handSocket) Held.SetParent(null, true); Held = null; }

        /// <summary>Puts a held instrument back at a slot (hand-over not taken, or reset).</summary>
        public void PutBack(Transform slot)
        {
            if (!Held) return;
            Held.SetParent(null, true);
            if (slot) Held.SetPositionAndRotation(slot.position, slot.rotation);
            var rb = Held.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = false;
            Held = null;
        }

        public Vector3 BesideStudent(float side = 0.7f, float forward = 0.3f)
        {
            if (!student) return transform.position;
            var f = student.forward; f.y = 0; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            var r = Vector3.Cross(Vector3.up, f);
            var p = student.position + r * side + f * forward; p.y = transform.position.y;
            return p;
        }
    }
}
