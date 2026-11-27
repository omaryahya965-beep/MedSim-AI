using System.Collections;
using System.Linq;
using MedSimAI.Surgery.Mechanics;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using TMPro;
using UnityEngine;

namespace MedSimAI.Hospital
{
    /// <summary>
    /// Connects Person 3's SurgicalRobotController to Person 1's OrthopedicAssistRobot (one robot, no duplicate). During the
    /// resection objectives the arm moves its tool mount to hover above the active bone target (CCD inverse kinematics over
    /// the six documented joint axes) and shows a "simulated guidance" marker; it parks again when the cut is done or the
    /// session resets. It is an educational visual aid only: it does not cut, does not constrain the student's tools and is
    /// not a model of any real surgical robot.
    /// </summary>
    public class RobotGuidanceAdapter : MonoBehaviour
    {
        public SurgicalRobotController controller;
        public Transform robotBase, toolMount;
        public Transform[] joints = new Transform[6];      // J1_BaseYaw … J6_ToolRoll
        public Vector3[] axes = { Vector3.up, Vector3.forward, Vector3.forward, Vector3.right, Vector3.forward, Vector3.forward };
        public float[] limits = { 170f, 95f, 135f, 170f, 120f, 170f };
        public float moveSeconds = 2.4f, hoverHeight = 0.24f;
        public Transform marker;                            // translucent target ring + label (built by the scene builder)
        public bool GuidanceActive { get; private set; }
        public float LastSolveErrorMeters { get; private set; } = -1f;

        Quaternion[] rest; float[] current, goal; Coroutine motion; SurgeryEventBus bus;

        void Awake()
        {
            rest = joints.Select(j => j ? j.localRotation : Quaternion.identity).ToArray();
            current = new float[joints.Length]; goal = new float[joints.Length];
            if (controller) controller.Bind(robotBase, toolMount, joints);
            if (marker) marker.gameObject.SetActive(false);
        }

        void OnEnable() { StartCoroutine(HookBus()); }
        void OnDisable() { if (bus != null) bus.EventPublished -= OnEvent; bus = null; }

        IEnumerator HookBus()
        {
            while (!SurgeryEventBusHost.Instance) yield return null;
            bus = SurgeryEventBusHost.Instance.Bus; bus.EventPublished += OnEvent;
        }

        void OnEvent(SurgeryEvent e)
        {
            if (e.id == SurgeryEventIds.SessionStarted || e.id == SurgeryEventIds.SessionReset) { Park(); return; }
            if (e.source != SurgeryEventSources.Mechanics) return;
            if (e.id == SurgeryEventIds.ObjectiveStarted && IsResection(e.objectiveId, out var targetId)) Guide(targetId);
            else if (e.id == SurgeryEventIds.ObjectiveCompleted && IsResection(e.objectiveId, out _)) Park();
        }

        static bool IsResection(string objectiveId, out string targetId)
        {
            targetId = objectiveId == "obj.femoral_resection" ? AnatomyTargetIds.DistalFemur : objectiveId == "obj.tibial_resection" ? AnatomyTargetIds.ProximalTibia : null;
            return targetId != null;
        }

        static Transform FindTarget(string targetId) =>
            FindObjectsByType<SurgeryAnatomyTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.targetId == targetId)?.transform;

        /// <summary>Moves the tool mount above the given anatomy target and shows the simulated-guidance marker.</summary>
        public bool Guide(string targetId)
        {
            var t = FindTarget(targetId); if (!t) return false;
            var hover = t.position + Vector3.up * hoverHeight + (robotBase ? Vector3.ProjectOnPlane(robotBase.position - t.position, Vector3.up).normalized * 0.08f : Vector3.zero);
            Solve(hover, t.position);
            if (marker) { marker.position = t.position + Vector3.up * 0.005f; marker.gameObject.SetActive(true); }
            if (controller && !controller.IsGuidanceActive) controller.ActivateGuidance();
            GuidanceActive = true;
            Animate();
            return true;
        }

        /// <summary>Back to the parked pose; marker hidden.</summary>
        public void Park()
        {
            for (int i = 0; i < goal.Length; i++) goal[i] = 0f;
            if (marker) marker.gameObject.SetActive(false);
            if (controller) controller.DeactivateGuidance();
            GuidanceActive = false;
            Animate();
        }

        void Apply(float[] a) { for (int i = 0; i < joints.Length; i++) if (joints[i]) joints[i].localRotation = rest[i] * Quaternion.AngleAxis(a[i], axes[i]); }

        /// <summary>Cyclic-coordinate-descent IK on the joint angles (from the parked pose), then aims the tool mount at the bone.</summary>
        void Solve(Vector3 hover, Vector3 aimAt)
        {
            var a = new float[joints.Length];
            Apply(a);
            for (int iter = 0; iter < 40; iter++)
            {
                for (int j = joints.Length - 1; j >= 0; j--)
                {
                    if (!joints[j]) continue;
                    var axisW = joints[j].rotation * axes[j];
                    var toEnd = Vector3.ProjectOnPlane(toolMount.position - joints[j].position, axisW);
                    var toGoal = Vector3.ProjectOnPlane(hover - joints[j].position, axisW);
                    if (toEnd.sqrMagnitude < 1e-6f || toGoal.sqrMagnitude < 1e-6f) continue;
                    a[j] = Mathf.Clamp(a[j] + Vector3.SignedAngle(toEnd, toGoal, axisW) * 0.7f, -limits[j], limits[j]);
                    joints[j].localRotation = rest[j] * Quaternion.AngleAxis(a[j], axes[j]);
                }
                if ((toolMount.position - hover).sqrMagnitude < 1e-4f) break;
            }
            // wrist pitch (J5): point the mount's +Z toward the bone
            if (joints.Length > 4 && joints[4])
            {
                var axisW = joints[4].rotation * axes[4];
                var fwd = Vector3.ProjectOnPlane(toolMount.forward, axisW); var want = Vector3.ProjectOnPlane(aimAt - toolMount.position, axisW);
                if (fwd.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f) a[4] = Mathf.Clamp(a[4] + Vector3.SignedAngle(fwd, want, axisW), -limits[4], limits[4]);
            }
            Apply(a);
            LastSolveErrorMeters = Vector3.Distance(toolMount.position, hover);
            Apply(current);   // the visible arm moves there smoothly (Animate)
            goal = a;
        }

        void Animate()
        {
            if (motion != null) StopCoroutine(motion);
            if (isActiveAndEnabled) motion = StartCoroutine(MoveRoutine());
            else { current = (float[])goal.Clone(); Apply(current); }
        }

        IEnumerator MoveRoutine()
        {
            var from = (float[])current.Clone(); float t = 0;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.1f, moveSeconds));
                float e = t * t * (3f - 2f * t);
                for (int i = 0; i < current.Length; i++) current[i] = Mathf.Lerp(from[i], goal[i], e);
                Apply(current);
                yield return null;
            }
            motion = null;
        }
    }
}
