using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Small anatomical corrections applied after the Animator (and before the procedural life layer reads the pose):
    /// removes most of the hand's roll around the forearm axis (the "candy-wrapper" pinched wrist the reclined clip
    /// produced) and adds a slight natural elbow flex where the arm lay fully straight.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PoseRefinement : MonoBehaviour
    {
        public Animator animator;
        [Range(0, 1)] public float wristTwistKeep = 0.25f;
        [Range(0, 25)] public float minElbowFlex = 10f;
        Transform lUpper, lLower, lHand, rUpper, rLower, rHand;

        void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!animator || !animator.isHuman) { enabled = false; return; }
            lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); lLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm); lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm); rLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm); rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        void LateUpdate() { Fix(lUpper, lLower, lHand); Fix(rUpper, rLower, rHand); }

        void Fix(Transform upper, Transform lower, Transform hand)
        {
            if (!upper || !lower || !hand) return;
            // 1) elbow: if (almost) straight, flex slightly around the elbow hinge (axis ⟂ to arm and to the body's up)
            Vector3 ua = (lower.position - upper.position).normalized, la = (hand.position - lower.position).normalized;
            float bend = Vector3.Angle(ua, la);
            if (bend < minElbowFlex)
            {
                Vector3 hinge = Vector3.Cross(ua, transform.up); if (hinge.sqrMagnitude < 1e-4f) hinge = transform.right;
                // flex so the forearm lifts towards the body's front (up out of the mattress)
                var q = Quaternion.AngleAxis(minElbowFlex - bend, hinge.normalized);
                var test = q * la; if (Vector3.Dot(test, transform.up) < Vector3.Dot(la, transform.up)) q = Quaternion.Inverse(q);
                var handRot = hand.rotation; lower.rotation = q * lower.rotation; hand.rotation = q * handRot;
            }
            // 2) wrist: keep the swing (bend) of the hand but drop most of its twist about the forearm
            Vector3 axisLocal = lower.InverseTransformDirection((hand.position - lower.position).normalized);
            var local = Quaternion.Inverse(lower.rotation) * hand.rotation;
            Vector3 r = new Vector3(local.x, local.y, local.z); Vector3 proj = Vector3.Project(r, axisLocal);
            var twist = new Quaternion(proj.x, proj.y, proj.z, local.w);
            if (twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w < 1e-8f) return;
            twist = Normalize(twist);
            var swing = local * Quaternion.Inverse(twist);
            hand.rotation = lower.rotation * swing * Quaternion.Slerp(Quaternion.identity, twist, wristTwistKeep);
        }

        static Quaternion Normalize(Quaternion q) { float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w); return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m); }
    }
}
