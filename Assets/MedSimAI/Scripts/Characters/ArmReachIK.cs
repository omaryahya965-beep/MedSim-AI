using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Procedural arm reach for humanoid characters (no animation assets needed): an analytic two-bone IK applied after the
    /// Animator (LateUpdate, late execution order), blended in and out smoothly. Used by the demonstrations: Dr. Hassan and
    /// the assistant reach for an instrument, hold it on the patient and move it along a path, so a demonstration is a visible
    /// physical action rather than dialogue.
    ///   • <see cref="ReachTo"/>: move the right (or left) hand to a world point (optionally with a hand rotation);
    ///   • <see cref="ReachWithTool"/>: place a HELD tool's tip at a point (the hand is solved so the tip lands there);
    ///   • <see cref="Release"/>: blend back to the animation.
    /// The elbow bends outward/down (pole hint), reach is clamped to the arm's length, and the solve is skipped when the
    /// Animator is not humanoid. Visual only: it never decides an action.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class ArmReachIK : MonoBehaviour
    {
        public float blendSeconds = 0.35f;
        public bool rightArm = true;
        Animator anim; Transform upper, lower, hand, spine, chest;
        Vector3 target; Quaternion? handRot; Transform tool, toolTip; float weight, goal; bool valid;
        public bool Reaching => goal > 0.5f;
        public float Weight => weight;
        /// <summary>Distance between the hand (or held tool tip) and the requested point after the last solve (m).</summary>
        public float LastError { get; private set; }
        public Transform Hand { get { Init(); return hand; } }

        void Init()
        {
            if (anim) return;
            anim = GetComponentInChildren<Animator>();
            if (!anim || !anim.isHuman) return;
            upper = anim.GetBoneTransform(rightArm ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            lower = anim.GetBoneTransform(rightArm ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            hand = anim.GetBoneTransform(rightArm ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            valid = upper && lower && hand;
            spine = anim.GetBoneTransform(HumanBodyBones.Spine); chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        }

        public void ReachTo(Vector3 point, Quaternion? rotation = null) { Init(); target = point; handRot = rotation; tool = null; toolTip = null; goal = 1f; }
        /// <summary>Puts the held tool's tip on <paramref name="tipPoint"/>; with <paramref name="toolForward"/>, the tool's +Z
        /// (its working direction) is turned that way (e.g. a pen pointing down onto the skin) by rotating the hand.</summary>
        public void ReachWithTool(Transform heldTool, Transform tip, Vector3 tipPoint, Vector3? toolForward = null)
        {
            Init(); target = tipPoint; tool = heldTool; toolTip = tip; goal = 1f; toolDir = toolForward;
            handRot = null;
        }
        Vector3? toolDir;
        public void Release() { goal = 0f; }

        void LateUpdate()
        {
            weight = Mathf.MoveTowards(weight, goal, Time.deltaTime / Mathf.Max(0.01f, blendSeconds));
            if (weight <= 0f || !valid) return;
            float w = Mathf.SmoothStep(0, 1, weight);
            Vector3 handGoal = target;
            bool withTool = tool && toolTip && tool.IsChildOf(hand);
            Quaternion? want = handRot;
            if (withTool && toolDir.HasValue && toolDir.Value.sqrMagnitude > 1e-6f)
            {   // hand rotation that turns the tool's +Z along toolDir (tool.rotation = hand.rotation * localToolRotation)
                var local = Quaternion.Inverse(hand.rotation) * tool.rotation;
                var up = Vector3.ProjectOnPlane(transform.forward, toolDir.Value); if (up.sqrMagnitude < 1e-4f) up = Vector3.up;
                want = Quaternion.LookRotation(toolDir.Value.normalized, up) * Quaternion.Inverse(local);
            }
            if (withTool) handGoal = target - (toolTip.position - hand.position);
            Lean(handGoal, w);   // lean over the table first when the point is beyond comfortable arm reach
            for (int pass = 0; pass < (withTool ? 2 : 1); pass++)
            {
                if (withTool) handGoal = target - (toolTip.position - hand.position);   // the tip, not the wrist, goes to the point
                Solve(handGoal, w);
                if (want.HasValue) hand.rotation = Quaternion.Slerp(hand.rotation, want.Value, w);
            }
            LastError = Vector3.Distance(tool && toolTip ? toolTip.position : hand.position, target);
        }

        /// <summary>Bends the spine and chest forward (up to <see cref="maxLeanDegrees"/>) so the shoulder comes closer to a far point.</summary>
        void Lean(Vector3 goal, float w)
        {
            if (!spine) return;
            float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            float excess = Vector3.Distance(upper.position, goal) - reach * 0.9f;
            if (excess <= 0f) return;
            float torso = Mathf.Max(0.3f, Vector3.Distance(spine.position, upper.position));
            float angle = Mathf.Clamp(Mathf.Asin(Mathf.Clamp01(excess / torso)) * Mathf.Rad2Deg * 1.2f, 0f, maxLeanDegrees) * w;
            var flat = Vector3.ProjectOnPlane(goal - spine.position, Vector3.up); if (flat.sqrMagnitude < 1e-6f) return;
            var axis = Vector3.Cross(Vector3.up, flat.normalized);
            spine.rotation = Quaternion.AngleAxis(angle * 0.55f, axis) * spine.rotation;
            if (chest) chest.rotation = Quaternion.AngleAxis(angle * 0.45f, axis) * chest.rotation;
        }
        public float maxLeanDegrees = 32f;

        /// <summary>Analytic two-bone IK (law of cosines) with an outward/downward elbow pole.</summary>
        void Solve(Vector3 goalPos, float w)
        {
            Vector3 a = upper.position, b = lower.position, c = hand.position;
            float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c);
            float reach = lab + lbc;
            Vector3 toGoal = goalPos - a; float d = Mathf.Clamp(toGoal.magnitude, 0.05f * reach, reach * 0.999f);
            Vector3 dir = toGoal.sqrMagnitude > 1e-8f ? toGoal.normalized : transform.forward;
            // pole: elbow out to the side and down, as a person reaching forward over a bed or a table
            Vector3 side = rightArm ? transform.right : -transform.right;
            Vector3 pole = Vector3.ProjectOnPlane(side * 0.6f + Vector3.down * 0.8f - transform.forward * 0.2f, dir);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(Vector3.down, dir);
            pole.Normalize();
            float cosA = Mathf.Clamp((lab * lab + d * d - lbc * lbc) / (2f * lab * d), -1f, 1f);
            float angA = Mathf.Acos(cosA);
            Vector3 elbow = a + (Quaternion.AngleAxis(angA * Mathf.Rad2Deg, Vector3.Cross(dir, pole).normalized) * dir) * lab;
            // rotate the upper arm so the elbow goes to 'elbow', then the forearm so the wrist goes to the goal
            var upRot = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
            upper.rotation = Quaternion.Slerp(upper.rotation, upRot, w);
            b = lower.position; c = hand.position;
            var goalClamped = a + dir * d;
            var loRot = Quaternion.FromToRotation(c - b, goalClamped - b) * lower.rotation;
            lower.rotation = Quaternion.Slerp(lower.rotation, loRot, w);
        }
    }
}
