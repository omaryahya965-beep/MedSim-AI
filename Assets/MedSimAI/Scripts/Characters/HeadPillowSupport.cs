using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Natural head support for the reclined patient. Manual testing showed the head sunk deep into a tall, almost upright
    /// pillow and hard to see: in the reclined pose the back of the head lies on the backrest itself, so any pillow behind it
    /// intersects the head. Every frame after the animation (and after gaze / pose refinement) this:
    ///   1. lays a slimmer pillow flat on the backrest, under the back of the head and the neck (thin axis = backrest normal);
    ///   2. flexes the neck (and slightly the head) forward just enough that the head RESTS on the pillow surface instead of
    ///      passing through it, so the face is turned toward the room and clearly visible from the standing position.
    /// When the patient sits up the head moves away from the backrest; the pillow stays on the backrest and no flexion is added.
    /// Visual only (no clinical state).
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class HeadPillowSupport : MonoBehaviour
    {
        public Transform pillow;            // the pillow object (its thin axis is aligned to the backrest normal)
        public Transform backrest;          // the bed's head section (up = surface normal, forward = up the bed)
        public float surfaceOffset = 0.08f; // backrest top surface above the head-section pivot (mattress + sheet)
        public float pillowThickness = 0.10f, headRadius = 0.095f, maxFlexDegrees = 26f, maxSettleDegrees = 18f;
        [Range(0.3f, 1.2f)] public float pillowFlatten = 0.7f;
        Animator anim; Transform neck, head, chest; float flex; Vector3 pillowBaseScale = Vector3.one;
        /// <summary>Head-centre distance from the pillow's front surface after the last solve (m); about 0 = resting on it.</summary>
        public float LastGap { get; private set; }
        /// <summary>Diagnostics: head distance from the backrest surface and the applied neck flexion (deg) in the last frame.</summary>
        public float LastDistance { get; private set; }
        public float LastFlex => flex;
        public float LastK { get; private set; }
        public int Instances => FindObjectsByType<HeadPillowSupport>(FindObjectsSortMode.None).Length;

        void Start()
        {
            anim = GetComponentInChildren<Animator>();
            if (anim && anim.isHuman)
            {
                neck = anim.GetBoneTransform(HumanBodyBones.Neck); head = anim.GetBoneTransform(HumanBodyBones.Head);
                chest = anim.GetBoneTransform(HumanBodyBones.UpperChest); if (!chest) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            }
            if (pillow) { pillowBaseScale = pillow.localScale; pillow.localScale = new Vector3(pillowBaseScale.x, pillowBaseScale.y * pillowFlatten, pillowBaseScale.z); }
        }

        void LateUpdate()
        {
            if (!head || !neck || !backrest) return;
            Vector3 n = backrest.up, along = backrest.forward; if (Vector3.Dot(along, Vector3.up) < 0) along = -along;
            Vector3 p0 = backrest.position + n * surfaceOffset;
            float need = pillowThickness + headRadius;                 // head centre distance from the backrest surface when resting on the pillow
            float d = Vector3.Dot(head.position - p0, n); LastDistance = d;
            // reclined: the head rests on the pillow — nod forward if it would pass through it, settle back if it floats in
            // front of it; sitting up (clearly away from the backrest): leave the pose alone
            bool reclinedOnIt = d < need + 0.13f;
            var spineUp = (head.position - neck.position).normalized;
            var axis = Vector3.Cross(spineUp, n); if (axis.sqrMagnitude < 1e-6f) axis = transform.right;
            axis.Normalize();
            Quaternion cq = chest ? chest.rotation : Quaternion.identity, nq = neck.rotation, hq = head.rotation;
            float Apply(float a)
            {   // the pillow props the upper chest, neck and head forward together (60/30/10), or lets them settle back;
                // returns the head's distance from the backrest. World rotations are re-applied from the animated pose each call.
                if (chest) chest.rotation = Quaternion.AngleAxis(a * 0.6f, axis) * cq;
                neck.rotation = Quaternion.AngleAxis(a * (chest ? 0.3f : 0.75f), axis) * nq;
                head.rotation = Quaternion.AngleAxis(a * (chest ? 0.1f : 0.25f), axis) * hq;
                return Vector3.Dot(head.position - p0, n);
            }
            float target = 0f;
            if (reclinedOnIt)
            {   // measured sensitivity (m per degree) for this rig and pose, so the head lands exactly on the pillow
                float k = (Apply(10f) - d) / 10f; LastK = k;
                if (Mathf.Abs(k) > 1e-5f) target = Mathf.Clamp((need - d) / k, -maxSettleDegrees, maxFlexDegrees);
            }
            flex = Mathf.MoveTowards(flex, target, 60f * Time.deltaTime);
            Apply(flex);
            if (pillow)
            {
                // pillow lies on the backrest under the back of the head, reaching a little down toward the neck/shoulders
                var onPlane = head.position - n * Vector3.Dot(head.position - p0, n);
                var c = onPlane + n * (pillowThickness * 0.5f) - along * 0.05f;
                pillow.SetPositionAndRotation(c, Quaternion.LookRotation(along, n));
                LastGap = Vector3.Dot(head.position - p0, n) - need;
            }
        }
    }
}
