using System;
using System.Collections;
using MedSimAI.Characters;
using UnityEngine;
using UnityEngine.AI;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Walks the persistent doctor through the hospital with the student: to a room door, back to the corridor, or beside
    /// the student. Uses her existing DoctorMovement (NavMesh, eased turning, animated walk; she never teleports). While
    /// guiding she checks that the student is following: if the student falls behind she stops, turns to them and waits,
    /// then carries on. A route that the NavMesh cannot complete (e.g. a closed door) is reported, not walked through.
    /// Outcomes are reported exactly once through the callback, which the coordinator forwards to the action lifecycle.
    /// </summary>
    public class HospitalGuide : MonoBehaviour
    {
        public enum Mode { Idle, Guiding, Waiting, WalkingWith, Holding }

        public DoctorMovement mover;
        public MedicalInstructorController instructor;
        public Transform student;                 // the student's head (camera)
        public float waitDistance = 4.2f, resumeDistance = 2.6f, arriveDistance = 0.5f, timeoutSeconds = 150f;
        public Mode State { get; private set; } = Mode.Idle;
        public Vector3 Destination { get; private set; }
        /// <summary>Line ids the guide wants spoken (the coordinator voices them in the current language).</summary>
        public event Action<string> SayRequested;

        Coroutine run; float lastWaitLine = -100f;

        Vector3 Floor(Vector3 p) { p.y = transform.position.y; return p; }
        float StudentDistance => student ? Vector3.Distance(Floor(student.position), Floor(transform.position)) : 0f;

        /// <summary>Stops guiding. A walk still in progress is reported as finished with reason "released" (e.g. the student
        /// reached the room first), so whoever requested it never keeps waiting for an outcome.</summary>
        public void Release()
        {
            if (run != null) StopCoroutine(run);
            run = null; State = Mode.Idle;
            var p = pendingDone; pendingDone = null; p?.Invoke(false, "released");
        }
        Action<bool, string> pendingDone;

        /// <summary>Stops and faces the student ("wait").</summary>
        public void Hold()
        {
            Release(); State = Mode.Holding;
            mover.Stop(); if (student) mover.Face(student.position);
        }

        /// <summary>Stays beside the student as they move ("walk with me").</summary>
        public void WalkWith()
        {
            Release(); State = Mode.WalkingWith;
            run = StartCoroutine(WalkWithRoutine());
        }

        /// <summary>True if a complete NavMesh path exists from the doctor to the point (closed doors carve the mesh).</summary>
        public bool CanReach(Vector3 point)
        {
            if (!NavMesh.SamplePosition(point, out var hit, 1.0f, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>Walks to the destination through the waypoints, keeping the student with her. done(true) on arrival.</summary>
        public void GuideTo(Vector3 destination, Action<bool, string> done, params Vector3[] via)
        {
            Release();
            if (!CanReach(destination)) { done?.Invoke(false, "no_path"); return; }
            State = Mode.Guiding; Destination = destination; pendingDone = done;
            run = StartCoroutine(GuideRoutine(destination, via, done));
        }

        IEnumerator GuideRoutine(Vector3 destination, Vector3[] via, Action<bool, string> done)
        {
            float t0 = Time.time;
            var points = new Vector3[(via?.Length ?? 0) + 1];
            for (int i = 0; i < points.Length - 1; i++) points[i] = via[i];
            points[points.Length - 1] = destination;
            foreach (var wp in points)
            {
                bool last = wp == destination;
                mover.GoToPoint(wp, last && student ? student.position : (Vector3?)null);
                int stuck = 0; yield return null; yield return null;
                while (true)
                {
                    if (Time.time - t0 > timeoutSeconds) { Finish(false, "timeout"); yield break; }
                    float toWp = Vector3.Distance(Floor(transform.position), Floor(wp));
                    if (!mover.IsMoving)
                    {
                        if (toWp <= arriveDistance + 0.15f || (!last && toWp <= 1.0f)) break;
                        if (++stuck > 3) { Finish(false, "stuck"); yield break; }
                        mover.GoToPoint(wp); yield return new WaitForSeconds(0.3f); continue;
                    }
                    // Keep the student with us: if they fall behind, stop, turn to them and wait.
                    if (student && StudentDistance > waitDistance && StudentIsBehind(wp))
                    {
                        mover.Stop(); mover.Face(student.position); State = Mode.Waiting;
                        if (Time.time - lastWaitLine > 20f) { lastWaitLine = Time.time; SayRequested?.Invoke("waiting"); }
                        float waited = 0f;
                        while (StudentDistance > resumeDistance && Time.time - t0 <= timeoutSeconds) { waited += Time.deltaTime; if (student) mover.Face(student.position); yield return null; }
                        if (waited > 4f) SayRequested?.Invoke("resume");
                        State = Mode.Guiding; mover.GoToPoint(wp, last && student ? student.position : (Vector3?)null); yield return null;
                    }
                    yield return null;
                }
            }
            if (student) mover.Face(student.position);
            Finish(true, "");

            void Finish(bool ok, string reason) { State = Mode.Idle; run = null; pendingDone = null; done?.Invoke(ok, reason); }
        }

        bool StudentIsBehind(Vector3 target)
        {
            var toTarget = Floor(target) - Floor(transform.position);
            var toStudent = Floor(student.position) - Floor(transform.position);
            return toTarget.sqrMagnitude < 1e-3f || Vector3.Dot(toTarget.normalized, toStudent.normalized) < 0.3f;
        }

        IEnumerator WalkWithRoutine()
        {
            while (true)
            {
                if (student && StudentDistance > 1.8f)
                {
                    var f = Vector3.ProjectOnPlane(student.forward, Vector3.up); f = f.sqrMagnitude > 1e-3f ? f.normalized : Vector3.forward;
                    var r = Vector3.Cross(Vector3.up, f);
                    var want = Floor(student.position) + r * 0.85f - f * 0.25f;
                    if (NavMesh.SamplePosition(want, out var hit, 0.8f, NavMesh.AllAreas)) mover.GoToPoint(hit.position, student.position);
                }
                else if (student && !mover.IsMoving) mover.Face(student.position);
                yield return new WaitForSeconds(0.5f);
            }
        }
    }
}
