using System;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Physical actions of the bedside patient, layered on top of the Animator pose (runs after the Animator and the wrist
    /// refinement, before the procedural life layer, so breathing/gaze/blinks keep working on top):
    ///  • posture: sit up (torso and the bed's backrest rise together about the hinge), recline, turn to a side, settle;
    ///  • arms: analytic two-bone IK (shoulder–elbow–wrist with an elbow pole) for raise / extend towards the student /
    ///    point to the approved pain site / relax — smooth critically-damped weights, never instant snaps;
    ///  • breathing depth/rate, mouth open, eye contact, holding still for an examination.
    /// Exam zones and attached instruments are parented to the bones, so they follow every movement.
    /// Legs stay under the blanket (they do not move), so the bedridden mesh and draped blanket remain valid.
    /// </summary>
    [DefaultExecutionOrder(-45)]
    public class PatientActionController : MonoBehaviour
    {
        public Animator animator; public HumanoidLifeController life; public FaceRig face;
        public Transform student; // player head
        public const float SitUpDegrees = 24f;
        public event Action<string, string> Completed; // (type, side)

        Transform spine, chest, neck, head, lUA, lLA, lH, rUA, rLA, rH, backrest, headSection;
        Vector3 backrestPos, headSectionPos; Quaternion backrestRot, headSectionRot;
        Vector3 pivot; // the torso's own bend point (lumbar joint at rest): backrest + pillow rotate about it, so they stay with the back and head

        // targets (0..1 weights / signed values) and their smoothed values
        float sitT, sit, sitV; float turnT, turn, turnV; float lArmT, lArm, lArmV, rArmT, rArmV, rArm; float mouthT, mouth, mouthV;
        enum ArmGoal { Rest, Raise, Extend, Point }
        ArmGoal lGoal, rGoal; float settleTime = -1, deepUntil, mouthUntil, holdUntil, lookUntil, armHoldUntil;
        string pendingType, pendingSide; Func<bool> pendingDone; float pendingDeadline;
        float nextIdleShift, nextDiscomfort, discomfortUntil; public CharacterSpeechController speech; public bool autonomous = true;

        void Start()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (!life) life = GetComponent<HumanoidLifeController>();
            if (!face) face = GetComponentInChildren<FaceRig>();
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            spine = B(HumanBodyBones.Spine); chest = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.UpperChest); neck = B(HumanBodyBones.Neck); head = B(HumanBodyBones.Head);
            lUA = B(HumanBodyBones.LeftUpperArm); lLA = B(HumanBodyBones.LeftLowerArm); lH = B(HumanBodyBones.LeftHand);
            rUA = B(HumanBodyBones.RightUpperArm); rLA = B(HumanBodyBones.RightLowerArm); rH = B(HumanBodyBones.RightHand);
            var bed = GameObject.Find("Hospital Bed");
            if (bed) { foreach (var t in bed.GetComponentsInChildren<Transform>(true)) { if (t.name == "Head_Section") headSection = t; else if (t.name == "v2_Backrest") backrest = t; } }
            if (headSection) { headSectionPos = headSection.position; headSectionRot = headSection.rotation; }
            if (backrest) { backrestPos = backrest.position; backrestRot = backrest.rotation; }
            pivot = spine ? spine.position : transform.position;
            if (!speech) speech = GetComponentInChildren<CharacterSpeechController>();
            nextIdleShift = Time.time + UnityEngine.Random.Range(35f, 70f); nextDiscomfort = Time.time + UnityEngine.Random.Range(60f, 110f);
        }

        // ------------------------------------------------------------------ actions (validated upstream)
        /// <summary>Starts an action. Returns false if it is not something this body can do right now.</summary>
        public bool Execute(string type, string side)
        {
            bool left = side == "left", right = side == "right", both = side == "both" || side == "none";
            Interrupted(type);
            switch (type)
            {
                case "sit_up": sitT = 1f; Pending(type, side, () => sit > 0.95f, 6); return true;
                case "recline": sitT = 0f; turnT = 0; Pending(type, side, () => sit < 0.05f && Mathf.Abs(turn) < 0.05f, 6); return true;
                case "turn_to_side": turnT = left ? -1f : 1f; Pending(type, side, () => Mathf.Abs(turn - turnT) < 0.05f, 5); return true;
                case "shift_on_bed": settleTime = 0f; Pending(type, side, () => settleTime < 0, 4); return true;
                case "raise_arm": SetArm(left, ArmGoal.Raise); armHoldUntil = Time.time + 25f; Pending(type, side, () => ArmAt(left, 0.95f), 5); return true;
                case "extend_arm": SetArm(left, ArmGoal.Extend); armHoldUntil = Time.time + 25f; Pending(type, side, () => ArmAt(left, 0.95f), 5); return true;
                case "point_to_pain": SetArm(false, ArmGoal.Point); armHoldUntil = Time.time + 5f; Pending(type, "right", () => ArmAt(false, 0.95f), 5); return true;
                case "lower_arm":
                    if (left || both) SetArm(true, ArmGoal.Rest); if (right || both) SetArm(false, ArmGoal.Rest);
                    Pending(type, side, () => lArm < 0.05f && rArm < 0.05f, 5); return true;
                case "deep_breath": deepUntil = Time.time + 13f; Pending(type, side, () => Time.time > deepUntil, 16); return true;
                case "breathe_normally": deepUntil = 0; Pending(type, side, () => true, 1); return true;
                case "open_mouth": mouthT = 1f; mouthUntil = Time.time + 5f; Pending(type, side, () => mouth > 0.9f, 3); return true;
                case "close_mouth": mouthT = 0f; mouthUntil = 0; Pending(type, side, () => mouth < 0.1f, 3); return true;
                case "look_at_student": lookUntil = Time.time + 8f; Pending(type, side, () => true, 1); return true;
                case "hold_still": holdUntil = Time.time + 12f; Pending(type, side, () => true, 1); return true;
                case "rest": sitT = 0; turnT = 0; SetArm(true, ArmGoal.Rest); SetArm(false, ArmGoal.Rest); mouthT = 0; deepUntil = 0; holdUntil = 0;
                    Pending(type, side, () => sit < 0.05f && lArm < 0.05f && rArm < 0.05f, 6); return true;
                default: return false;
            }
        }
        public event Action<string, string> WasInterrupted;
        void Interrupted(string next) { if (pendingType != null) { WasInterrupted?.Invoke(pendingType, pendingSide); pendingType = null; } }
        void Pending(string type, string side, Func<bool> done, float timeout) { pendingType = type; pendingSide = side; pendingDone = done; pendingDeadline = Time.time + timeout; }
        void SetArm(bool left, ArmGoal g) { if (left) { lGoal = g; lArmT = g == ArmGoal.Rest ? 0 : 1; } else { rGoal = g; rArmT = g == ArmGoal.Rest ? 0 : 1; } }
        bool ArmAt(bool left, float w) => left ? lArm >= w : rArm >= w;

        public float Sit => sit; public float Turn => turn; public float LeftArm => lArm; public float RightArm => rArm; public float Mouth => mouth;

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!animator) return;
            float dt = Time.deltaTime;
            sit = Mathf.SmoothDamp(sit, sitT, ref sitV, 0.9f, 2f, dt);
            turn = Mathf.SmoothDamp(turn, turnT, ref turnV, 0.7f, 3f, dt);
            if (armHoldUntil > 0 && Time.time > armHoldUntil) { armHoldUntil = 0; SetArm(true, ArmGoal.Rest); SetArm(false, ArmGoal.Rest); } // hands rest again after holding a while
            lArm = Mathf.SmoothDamp(lArm, lArmT, ref lArmV, 0.45f, 4f, dt); rArm = Mathf.SmoothDamp(rArm, rArmT, ref rArmV, 0.45f, 4f, dt);
            if (mouthUntil > 0 && Time.time > mouthUntil) { mouthUntil = 0; mouthT = 0; }
            mouth = Mathf.SmoothDamp(mouth, mouthT, ref mouthV, 0.18f, 8f, dt);

            // posture: backrest and torso rotate together (sitting up = less reclined)
            float sitAngle = -SitUpDegrees * sit;
            if (headSection) { headSection.SetPositionAndRotation(headSectionPos, headSectionRot); headSection.RotateAround(pivot, Vector3.right, sitAngle); }
            if (backrest) { backrest.SetPositionAndRotation(backrestPos, backrestRot); backrest.RotateAround(pivot, Vector3.right, sitAngle); }
            if (spine) spine.rotation = Quaternion.AngleAxis(sitAngle, Vector3.right) * spine.rotation; // whole back rigid with the backrest (no pillow sweep)

            // body frame after posture
            Vector3 up = neck && spine ? (neck.position - spine.position).normalized : transform.up;
            Vector3 right = rUA && lUA ? (rUA.position - lUA.position).normalized : transform.right;
            Vector3 fwd = Vector3.Cross(right, up).normalized;

            // turn towards a side: twist + roll of the torso, small and comfortable
            if (Mathf.Abs(turn) > 0.001f && spine)
            {
                spine.rotation = Quaternion.AngleAxis(turn * 11f, up) * Quaternion.AngleAxis(-turn * 9f, fwd) * spine.rotation;
                if (neck) neck.rotation = Quaternion.AngleAxis(turn * 12f, up) * neck.rotation;
            }
            // settle / small adjustment on the bed (1.8 s, decaying)
            if (settleTime >= 0 && spine)
            {
                settleTime += dt; float k = Mathf.Clamp01(1f - settleTime / 1.8f);
                spine.rotation = Quaternion.AngleAxis(Mathf.Sin(settleTime * 7f) * 4f * k, fwd) * Quaternion.AngleAxis(Mathf.Sin(settleTime * 5f) * 3f * k, right) * spine.rotation;
                if (settleTime > 1.8f) settleTime = -1;
            }

            // arms (IK after the torso so targets are relative to the new posture)
            if (lArm > 0.001f) Arm(lUA, lLA, lH, lGoal, true, lArm, up, right, fwd);
            if (rArm > 0.001f) Arm(rUA, rLA, rH, rGoal, false, rArm, up, right, fwd);

            // breathing depth / mouth / gaze / stillness via the life layer and face
            bool deep = Time.time < deepUntil;
            if (life)
            {
                life.breathDepth = Mathf.MoveTowards(life.breathDepth, deep ? 3.4f : 1f, dt * 1.5f);
                life.breathRateOverride = deep ? 7f : 0f;
                life.holdStill = Time.time < holdUntil;
                life.gazeWeight = Time.time < lookUntil ? 1f : life.gazeWeight;
            }
            if (face && mouth > 0.01f) face.Add("jawOpen", 0.62f * mouth);

            Autonomous();
            if (pendingType != null)
            {
                bool done = pendingDone != null && pendingDone();
                if (done || Time.time > pendingDeadline) { var t = pendingType; var s = pendingSide; pendingType = null; if (done) Completed?.Invoke(t, s); else WasInterrupted?.Invoke(t, s); }
            }
        }

        /// <summary>
        /// Subtle life when nobody asks for anything: an occasional small settling movement, and (in pain states) briefly
        /// bringing a hand towards the chest. Never during speech, an action, or when the student asked him to keep still.
        /// </summary>
        void Autonomous()
        {
            if (!autonomous) return;
            bool busy = pendingType != null || (speech && speech.IsSpeaking) || Time.time < holdUntil || lGoal != ArmGoal.Rest || (rGoal != ArmGoal.Rest && discomfortUntil == 0);
            if (discomfortUntil > 0 && Time.time > discomfortUntil) { discomfortUntil = 0; SetArm(false, ArmGoal.Rest); }
            if (busy) return;
            if (Time.time > nextIdleShift) { settleTime = 0; nextIdleShift = Time.time + UnityEngine.Random.Range(35f, 70f); }
            bool pain = life && life.mood == CharacterMood.Discomfort;
            if (pain && Time.time > nextDiscomfort) { rGoal = ArmGoal.Point; rArmT = 0.55f; discomfortUntil = Time.time + 2.8f; nextDiscomfort = Time.time + UnityEngine.Random.Range(60f, 110f); }
        }

        void Arm(Transform ua, Transform la, Transform hand, ArmGoal goal, bool left, float w, Vector3 up, Vector3 right, Vector3 fwd)
        {
            if (!ua || !la || !hand) return;
            Vector3 side = left ? -right : right, sh = ua.position, target, hint;
            switch (goal)
            {
                case ArmGoal.Raise: target = sh + fwd * 0.40f + up * 0.30f + side * 0.10f; hint = sh + side * 0.28f - up * 0.08f; break; // forward and up in front of the chest
                case ArmGoal.Extend:
                    var to = student ? (student.position - sh) : fwd; to.y = Mathf.Clamp(to.y, -0.2f, 0.25f);
                    target = sh + to.normalized * 0.58f; hint = sh + side * 0.22f - up * 0.25f; break;
                case ArmGoal.Point:
                    Vector3 sternum = chest ? chest.position + fwd * 0.13f + up * 0.03f : sh + fwd * 0.2f - side * 0.18f;
                    target = sternum + fwd * 0.04f; hint = sh + side * 0.28f - up * 0.18f + fwd * 0.05f; break;
                default: return;
            }
            SolveTwoBone(ua, la, hand, target, hint, Mathf.SmoothStep(0, 1, w));
        }

        /// <summary>Analytic two-bone IK (law of cosines) with an elbow pole, blended with the animated pose.</summary>
        public static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float w)
        {
            Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
            float ab = (bPos - aPos).magnitude, bc = (cPos - bPos).magnitude;
            Vector3 at = target - aPos; float dist = Mathf.Clamp(at.magnitude, Mathf.Abs(ab - bc) + 1e-3f, ab + bc - 1e-3f);
            Vector3 dir = at.normalized;
            float cosA = Mathf.Clamp((ab * ab + dist * dist - bc * bc) / (2 * ab * dist), -1f, 1f);
            Vector3 toHint = hint - aPos; Vector3 n = Vector3.Cross(dir, toHint); if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dir, Vector3.up); n.Normalize();
            Vector3 elbow = aPos + Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, n) * dir * ab;
            var aRot = Quaternion.FromToRotation(bPos - aPos, elbow - aPos) * a.rotation;
            a.rotation = Quaternion.Slerp(a.rotation, aRot, w);
            bPos = b.position; cPos = c.position;
            Vector3 wrist = aPos + dir * dist;
            var bRot = Quaternion.FromToRotation(cPos - bPos, wrist - bPos) * b.rotation;
            b.rotation = Quaternion.Slerp(b.rotation, bRot, w);
        }
    }
}
