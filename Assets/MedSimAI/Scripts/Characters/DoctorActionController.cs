using System;
using UnityEngine;
using UnityEngine.AI;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Physical actions of the supervising doctor: walks (NavMesh, animated, avoiding furniture and the student) to points
    /// computed from where the student and the patient actually are, then turns to face the right person. She never
    /// teleports, keeps out of the way when asked, and demonstrations are gestures only (no findings revealed).
    /// </summary>
    public class DoctorActionController : MonoBehaviour
    {
        public DoctorMovement movement; public HumanoidLifeController life; public MedicalInstructorController instructor;
        public Transform student, patientChest, observeMark, distanceMark;
        public event Action<string, string> Completed, Failed; // (type, reason/"")
        /// <summary>
        /// Integration extension point: extra validated doctor actions implemented by a host outside the ER assembly
        /// (hospital navigation: guide_to_room, wait_for_student, ...). Return null when the action is not handled, true when
        /// it started (the host then calls ReportExternal), false when it is unavailable. Null in the standalone ER scene.
        /// </summary>
        public static Func<DoctorActionController, string, string, bool?> ExtensionHandler;
        bool external;
        /// <summary>The host reports the real outcome of an extension action (completed or failed with a reason).</summary>
        public void ReportExternal(string type, bool completed, string reason = "")
        {
            if (pendingType != type) return;
            pendingType = null; external = false;
            if (completed) Completed?.Invoke(type, ""); else Failed?.Invoke(type, string.IsNullOrEmpty(reason) ? "failed" : reason);
        }
        Transform mark; string pendingType; float pendingStart, gazeUntil; bool walking; Transform gazeTarget;

        void Start() => EnsureInit();
        bool inited;
        void EnsureInit() // also on first use (an action requested in the frame the controller was added, before Start)
        {
            if (inited) return; inited = true;
            if (!movement) movement = GetComponent<DoctorMovement>();
            if (!life) life = GetComponent<HumanoidLifeController>();
            if (!instructor) instructor = GetComponent<MedicalInstructorController>();
            mark = new GameObject("[Doctor action target]").transform;
            // the student is an obstacle for her local avoidance (she walks around you, not through you)
            var rig = FindAnyObjectByType<MedSimAI.XR.XRPlayerController>();
            if (rig && !rig.GetComponent<NavMeshObstacle>()) { var o = rig.gameObject.AddComponent<NavMeshObstacle>(); o.shape = NavMeshObstacleShape.Capsule; o.radius = 0.3f; o.height = 1.7f; o.center = new Vector3(0, 0.85f, 0); o.carving = false; }
        }

        Vector3 BedCentre => patientRoot ? Floor(patientRoot.position) : Floor(transform.position);
        static Vector3 toPatient(Vector3 s, Vector3 p) { var d = p - s; d.y = 0; return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward; }
        public Transform patientRoot;
        Vector3 Floor(Vector3 p) { p.y = transform.position.y; return p; }
        Vector3 StudentFloor => Floor(student ? student.position : transform.position);
        Vector3 StudentFwd { get { var f = student ? Vector3.ProjectOnPlane(student.forward, Vector3.up) : transform.forward; return f.sqrMagnitude < 1e-3f ? Vector3.forward : f.normalized; } }

        bool WalkTo(Vector3 p, Vector3 face)
        {
            if (!NavMesh.SamplePosition(p, out var hit, 0.6f, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            var look = Floor(face) - hit.position; mark.SetPositionAndRotation(hit.position, look.sqrMagnitude > 1e-3f ? Quaternion.LookRotation(look) : transform.rotation);
            movement.GoTo(mark); walking = true; return true;
        }

        /// <summary>
        /// Best reachable standing spot at a given distance from the student, as close as possible to the preferred direction,
        /// never within ~1 m of the bed and never between the student and the patient (she must not block the examination).
        /// </summary>
        bool WalkNear(Vector3 center, float radius, Vector3 preferDir, Vector3 face, Vector3 pat, Vector3 bedCentre)
        {
            Vector3 toPat = pat - center; toPat.y = 0; float dPat = toPat.magnitude; toPat = dPat > 1e-3f ? toPat / dPat : Vector3.forward;
            float best = float.MaxValue; Vector3 bestP = Vector3.zero; bool found = false;
            for (int i = 0; i < 16; i++)
            {
                var dir = Quaternion.AngleAxis(i * 22.5f, Vector3.up) * Vector3.forward; var p = center + dir * radius;
                if (!NavMesh.SamplePosition(p, out var hit, 0.35f, NavMesh.AllAreas)) continue;
                var q = hit.position; if (Vector3.Distance(Floor(q), Floor(bedCentre)) < 1.05f || Vector3.Distance(Floor(q), Floor(pat)) < 0.95f) continue;
                var off = Floor(q) - center; if (Vector3.Dot(off.normalized, toPat) > 0.45f && off.magnitude < dPat) continue; // in the way
                var path = new NavMeshPath(); if (!NavMesh.CalculatePath(transform.position, q, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                float score = Vector3.Angle(dir, preferDir) + Mathf.Abs(off.magnitude - radius) * 40f;
                if (score < best) { best = score; bestP = q; found = true; }
            }
            if (!found) return false;
            var look = Floor(face) - bestP; mark.SetPositionAndRotation(bestP, look.sqrMagnitude > 1e-3f ? Quaternion.LookRotation(look) : transform.rotation);
            movement.GoTo(mark); walking = true; return true;
        }

        /// <summary>Starts an action; returns false (unavailable) if no safe reachable spot exists.</summary>
        public bool Execute(string type, string instrument)
        {
            EnsureInit();
            if (pendingType != null) Failed?.Invoke(pendingType, "interrupted");
            pendingType = type; pendingStart = Time.time; walking = false; external = false;
            Vector3 sf = StudentFloor, fwd = StudentFwd, rightOfStudent = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 pat = patientChest ? Floor(patientChest.position) : transform.position;
            switch (type)
            {
                case "come_to_student": return WalkNear(sf, 1.0f, -toPatient(sf, pat) + rightOfStudent * 0.3f, sf, pat, BedCentre);
                case "stand_beside_student": return WalkNear(sf, 0.8f, rightOfStudent, pat, pat, BedCentre) || WalkNear(sf, 0.8f, -rightOfStudent, pat, pat, BedCentre);
                case "approach_patient":
                {   // bedside on the side away from the student, so she never blocks the examination
                    float side = sf.x > pat.x ? -1f : 1f;
                    return WalkTo(new Vector3(pat.x + side * 0.95f, pat.y, pat.z), patientChest ? patientChest.position : pat) || WalkTo(new Vector3(pat.x - side * 0.95f, pat.y, pat.z - 0.3f), pat);
                }
                case "step_back":
                {
                    if (observeMark && Vector3.Distance(Floor(observeMark.position), sf) > 1.3f && Vector3.Distance(Floor(observeMark.position), pat) > 1.1f && WalkTo(observeMark.position, pat)) return true;
                    var away = (sf - pat); away.y = 0; away = away.sqrMagnitude < 1e-3f ? -fwd : away.normalized;
                    return WalkTo(sf + away * 1.1f + rightOfStudent * 0.6f, sf) || WalkTo(sf + away * 1.1f - rightOfStudent * 0.6f, sf);
                }
                case "go_other_side":
                {   // to the bedside on the other side from where she is now (still never between the student and the patient)
                    float mySide = Mathf.Sign(transform.position.x - pat.x); if (mySide == 0) mySide = 1;
                    return WalkTo(new Vector3(pat.x - mySide * 0.95f, pat.y, pat.z), patientChest ? patientChest.position : pat) || WalkTo(new Vector3(pat.x - mySide * 0.95f, pat.y, pat.z - 0.4f), pat);
                }
                case "observe_from_distance": return (distanceMark && WalkTo(distanceMark.position, pat)) || (observeMark && WalkTo(observeMark.position, pat));
                case "return_to_observation": return observeMark && WalkTo(observeMark.position, pat);
                case "wait": movement.Stop(); if (student) movement.Face(student.position); return true;
                case "look_at_patient": if (patientChest) { movement.Face(patientChest.position); Gaze(patientChest, 6f); } return true;
                case "look_at_student": if (student) { movement.Face(student.position); Gaze(student, 6f); } return true;
                case "demonstrate_instrument":
                {
                    var tool = FindInstrument(instrument); if (!tool) return false;
                    movement.Face(tool.position); if (life) life.PointAt(tool, 4f); if (instructor) instructor.Gesture(CharacterMood.Explaining, tool); Gaze(tool, 4f);
                    return true;
                }
                default:
                {
                    var ext = ExtensionHandler?.Invoke(this, type, instrument);
                    if (ext == true) { external = true; return true; }
                    pendingType = null; return false;
                }
            }
        }

        void Gaze(Transform t, float seconds) { gazeTarget = t; gazeUntil = Time.time + seconds; if (life) life.gazeOverride = t; }

        static Transform FindInstrument(string id)
        {
            Type t = id == "bp_cuff" ? typeof(MedSimAI.Instruments.BloodPressureController) : id == "pulse_oximeter" ? typeof(MedSimAI.Instruments.PulseOximeterController)
                : id == "stethoscope" ? typeof(MedSimAI.Instruments.StethoscopeController) : id == "ecg" ? typeof(MedSimAI.Instruments.ECGMachineController) : null;
            var o = t != null ? FindAnyObjectByType(t) as Component : null; return o ? o.transform : null;
        }

        void Update()
        {
            if (gazeUntil > 0 && Time.time > gazeUntil) { gazeUntil = 0; if (life && life.gazeOverride == gazeTarget) life.gazeOverride = null; }
            if (pendingType == null) return;
            if (external) { if (Time.time - pendingStart > 180f) ReportExternal(pendingType, false, "timeout"); return; }   // the host reports the outcome
            bool done = walking ? (Time.time - pendingStart > 0.4f && !movement.IsMoving) : Time.time - pendingStart > 0.6f;
            if (done) { var t = pendingType; pendingType = null; Completed?.Invoke(t, ""); }
            else if (Time.time - pendingStart > 25f) { var t = pendingType; pendingType = null; Failed?.Invoke(t, "timeout"); }
        }
    }
}
