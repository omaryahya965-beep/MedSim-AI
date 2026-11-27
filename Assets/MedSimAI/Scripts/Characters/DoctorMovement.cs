using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Moves the supervising doctor naturally between authored marks (corridor → doorway → observation spot).
    /// Uses a NavMeshAgent when a NavMesh exists (AI Navigation), otherwise a smooth waypoint path. Drives the Animator
    /// "Speed" (m/s) parameter so walk/idle clips blend; rotation is eased (no snapping, no teleporting). Root motion off.
    /// </summary>
    public class DoctorMovement : MonoBehaviour
    {
        public float walkSpeed = 1.05f, turnSpeedDeg = 140f, stopDistance = 0.08f, accel = 1.6f;
        public Animator animator;
        public NavMeshAgent agent;
        readonly Queue<Vector3> path = new Queue<Vector3>();
        Quaternion? finalFacing; Vector3? faceTarget; float speed;
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        public bool IsMoving => path.Count > 0 || (agent && agent.enabled && agent.isOnNavMesh && (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.02f));
        public Vector3 Velocity { get; private set; }

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (agent) { agent.updateRotation = false; agent.speed = walkSpeed; agent.acceleration = accel * 4; agent.stoppingDistance = stopDistance; agent.autoBraking = true; }
        }

        bool UseAgent => agent && agent.enabled && agent.isOnNavMesh;

        /// <summary>Walk to a mark (via optional intermediate waypoints) and end facing the mark's forward.</summary>
        public void GoTo(Transform mark, params Transform[] via)
        {
            path.Clear(); faceTarget = null;
            finalFacing = mark ? mark.rotation : (Quaternion?)null;
            if (UseAgent) { agent.SetDestination(mark.position); return; }
            foreach (var v in via) if (v) path.Enqueue(v.position);
            if (mark) path.Enqueue(mark.position);
        }

        /// <summary>Stop where she is (e.g. "wait").</summary>
        public void Stop() { path.Clear(); if (UseAgent) agent.ResetPath(); }

        /// <summary>Turn on the spot to face a point (e.g. the patient, the student, an instrument).</summary>
        public void Face(Vector3 worldPoint) { faceTarget = worldPoint; finalFacing = null; }

        void Update()
        {
            Vector3 desired = Vector3.zero;
            if (UseAgent) { desired = agent.desiredVelocity; }
            else if (path.Count > 0)
            {
                var to = path.Peek() - transform.position; to.y = 0;
                if (to.magnitude <= stopDistance) path.Dequeue();
                else desired = to.normalized * walkSpeed * Mathf.Clamp01(to.magnitude / 0.45f + 0.35f);
            }
            float targetSpeed = desired.magnitude;
            speed = Mathf.MoveTowards(speed, targetSpeed, accel * Time.deltaTime);
            Vector3 dir = desired.sqrMagnitude > 1e-4f ? desired.normalized : transform.forward;
            Velocity = dir * speed;
            if (!UseAgent) transform.position += Velocity * Time.deltaTime;

            // Rotation: travel direction while walking, then the mark's facing or a look target.
            Quaternion want = transform.rotation;
            if (speed > 0.05f) want = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z));
            else if (faceTarget.HasValue) { var f = faceTarget.Value - transform.position; f.y = 0; if (f.sqrMagnitude > 1e-3f) want = Quaternion.LookRotation(f); }
            else if (finalFacing.HasValue) want = Quaternion.Euler(0, finalFacing.Value.eulerAngles.y, 0);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeedDeg * Time.deltaTime);

            if (animator && animator.runtimeAnimatorController) animator.SetFloat(SpeedHash, speed);
        }
    }
}
