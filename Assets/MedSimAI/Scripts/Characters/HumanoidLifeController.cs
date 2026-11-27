using UnityEngine;

namespace MedSimAI.Characters
{
    public enum CharacterMood { Neutral, Discomfort, Attentive, Explaining, Approving }

    /// <summary>
    /// Procedural "life" layer for humanoid characters (rig-agnostic: works out bone axes from the posed skeleton):
    /// breathing, eye blinks, eye + head gaze with saccades, nodding while speaking, listening tilt,
    /// facial expression by mood (FaceRig blend shapes), restrained explain / point gestures.
    /// Everything is applied relative to the authored rest pose, so no drift and no bone scaling.
    /// </summary>
    public class HumanoidLifeController : MonoBehaviour
    {
        [Header("Bones (auto-filled from humanoid Animator)")]
        public Transform head, neck, chest, spine, rightUpperArm, rightLowerArm;
        public Transform[] eyelids; // legacy simple rigs
        public SkinnedMeshRenderer face; public int blinkBlendShape = -1; // legacy
        public FaceRig faceRig;
        [Header("Behaviour")]
        public Transform lookTarget;
        public float maxHeadYaw = 55f, maxHeadPitch = 25f, headTurnSpeed = 2.5f;
        [Range(0, 1)] public float gazeWeight = 1f;
        public bool useEyeLookShapes = true;
        public float breathsPerMinute = 14f;
        [Tooltip("Scales the discomfort expression (authored patient condition).")] public float painIntensity = 1f;
        public CharacterMood mood = CharacterMood.Neutral;
        public bool lyingDown;
        public bool listening;
        public CharacterSpeechController speech;

        Quaternion headRest, neckRest, chestRest, spineRest, uaRest, laRest; Vector3[] lidRest;
        Vector3 faceFwdLocal; // face-forward direction in head-local space
        float blinkTimer, blinkPhase = -1, saccadeTimer; Vector3 saccadeOffset; Quaternion currentHeadOffset = Quaternion.identity;
        Transform pointTarget; float pointWeight, pointTimer; float painPulse, painTimer;
        Transform root;

        void Start()
        {
            root = transform;
            var anim = GetComponentInChildren<Animator>();
            if (anim && anim.isHuman)
            {
                if (!head) head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (!neck) neck = anim.GetBoneTransform(HumanBodyBones.Neck);
                if (!chest) chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
                if (!spine) spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            }
            if (!faceRig) faceRig = GetComponentInChildren<FaceRig>();
            if (head) { headRest = head.localRotation; faceFwdLocal = Quaternion.Inverse(head.rotation) * root.forward; }
            if (neck) neckRest = neck.localRotation;
            if (chest) chestRest = chest.localRotation;
            if (spine) spineRest = spine.localRotation;
            if (rightUpperArm) uaRest = rightUpperArm.localRotation;
            if (rightLowerArm) laRest = rightLowerArm.localRotation;
            lidRest = new Vector3[eyelids?.Length ?? 0];
            for (int i = 0; i < lidRest.Length; i++) lidRest[i] = eyelids[i].localScale;
            blinkTimer = Random.Range(1.5f, 4f);
            if (!speech) speech = GetComponentInChildren<CharacterSpeechController>();
        }

        public void PointAt(Transform target, float seconds = 2.5f) { pointTarget = target; pointTimer = seconds; }

        void LateUpdate()
        {
            float t = Time.time, dt = Time.deltaTime;
            bool pain = mood == CharacterMood.Discomfort;
            bool talking = speech && speech.IsSpeaking;

            // ---- Breathing: small rotations of spine/chest around the body's lateral axis (no scaling) ----
            float bpm = pain ? breathsPerMinute * 1.35f : breathsPerMinute;
            float breath = Mathf.Sin(t * bpm / 60f * Mathf.PI * 2f);
            Vector3 lateral = root.right;
            if (chest) chest.localRotation = Quaternion.Inverse(chest.parent.rotation) * Quaternion.AngleAxis(-breath * (lyingDown ? 0.9f : 0.7f), lateral) * chest.parent.rotation * chestRest;
            if (spine && !lyingDown)
            {
                var sway = Quaternion.AngleAxis(Mathf.Sin(t * 0.33f) * 0.7f, lateral) * Quaternion.AngleAxis(Mathf.Sin(t * 0.21f) * 0.9f, root.up);
                spine.localRotation = Quaternion.Inverse(spine.parent.rotation) * sway * spine.parent.rotation * spineRest;
            }

            // ---- Head gaze (clamped, with saccades, nodding while speaking, listening tilt) ----
            if (head)
            {
                saccadeTimer -= dt;
                if (saccadeTimer <= 0) { saccadeTimer = Random.Range(0.9f, 2.8f); saccadeOffset = Random.insideUnitSphere * 0.05f; }
                var target = pointTarget && pointWeight > 0.5f ? pointTarget : lookTarget;
                Quaternion offset = Quaternion.identity;
                Vector3 restFwd = head.parent.rotation * headRest * faceFwdLocal;
                if (target && gazeWeight > 0)
                {
                    var tp = target.position; if (target == lookTarget) tp.y = Mathf.Clamp(tp.y, 1.15f, 2.1f); // ignore implausible head heights (e.g. idle headset on the floor)
                    Vector3 dir = (tp + saccadeOffset - head.position).normalized;
                    var full = Quaternion.FromToRotation(restFwd, dir);
                    full.ToAngleAxis(out float ang, out Vector3 axis); if (ang > 180) ang -= 360;
                    offset = Quaternion.AngleAxis(Mathf.Clamp(ang, -maxHeadYaw, maxHeadYaw) * gazeWeight * 0.8f, axis);
                    // Eyes take the remainder of the gaze
                    if (faceRig && useEyeLookShapes)
                    {
                        Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(restFwd, root.up)) * dir;
                        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, pitch = Mathf.Asin(Mathf.Clamp(local.y, -1, 1)) * Mathf.Rad2Deg;
                        float ey = Mathf.Clamp((yaw - Mathf.Clamp(yaw, -maxHeadYaw, maxHeadYaw) * 0.8f) / 25f, -1, 1);
                        float ep = Mathf.Clamp((pitch * 0.4f) / 20f, -1, 1);
                        // character's right = +x; eyeLookOut for right eye when looking right
                        faceRig.Add(ey > 0 ? "eyeLookOutRight" : "eyeLookInRight", Mathf.Abs(ey) * 0.7f);
                        faceRig.Add(ey > 0 ? "eyeLookInLeft" : "eyeLookOutLeft", Mathf.Abs(ey) * 0.7f);
                        faceRig.Add(ep > 0 ? "eyeLookUpLeft" : "eyeLookDownLeft", Mathf.Abs(ep) * 0.6f);
                        faceRig.Add(ep > 0 ? "eyeLookUpRight" : "eyeLookDownRight", Mathf.Abs(ep) * 0.6f);
                    }
                }
                currentHeadOffset = Quaternion.Slerp(currentHeadOffset, offset, dt * headTurnSpeed);
                float nod = talking ? Mathf.Sin(t * 4.6f) * 1.6f + Mathf.Sin(t * 1.3f) * 1.2f : 0f;
                float tilt = listening ? 5f : (pain ? Mathf.Sin(t * 0.5f) * 2.5f : 0f);
                var right = Vector3.Cross(root.up, restFwd).normalized;
                var expressive = Quaternion.AngleAxis(nod, right) * Quaternion.AngleAxis(tilt, restFwd);
                head.rotation = currentHeadOffset * expressive * head.parent.rotation * headRest;
            }

            // ---- Blink ----
            blinkTimer -= dt;
            if (blinkTimer <= 0 && blinkPhase < 0) { blinkPhase = 0; blinkTimer = Random.Range(pain ? 1.3f : 2.4f, pain ? 3.5f : 6f); }
            float lid = 0;
            if (blinkPhase >= 0) { blinkPhase += dt / 0.15f; lid = Mathf.Sin(Mathf.Clamp01(blinkPhase) * Mathf.PI); if (blinkPhase >= 1) blinkPhase = -1; }
            if (faceRig) { faceRig.Add("eyeBlinkLeft", lid); faceRig.Add("eyeBlinkRight", lid); }
            for (int i = 0; i < lidRest.Length; i++) eyelids[i].localScale = new Vector3(lidRest[i].x, lidRest[i].y * (1 - lid * 0.95f), lidRest[i].z);
            if (face && blinkBlendShape >= 0) face.SetBlendShapeWeight(blinkBlendShape, lid * 100f);

            // ---- Facial expression by mood ----
            if (faceRig)
            {
                switch (mood)
                {
                    case CharacterMood.Discomfort:
                        painTimer -= dt; if (painTimer <= 0) { painTimer = Random.Range(4f, 9f); painPulse = 1f; }
                        painPulse = Mathf.MoveTowards(painPulse, 0, dt * 0.6f);
                        float p = (0.55f + 0.45f * painPulse) * painIntensity;
                        faceRig.Add("browInnerUp", 0.45f * p); faceRig.Add("browDownLeft", 0.1f * p); faceRig.Add("browDownRight", 0.1f * p);
                        faceRig.Add("eyeSquintLeft", 0.08f * p); faceRig.Add("eyeSquintRight", 0.08f * p);
                        if (!talking) { faceRig.Add("mouthFrownLeft", 0.3f * p); faceRig.Add("mouthFrownRight", 0.3f * p); faceRig.Add("mouthStretchLeft", 0.12f * painPulse); faceRig.Add("mouthStretchRight", 0.12f * painPulse); }
                        faceRig.Add("noseSneerLeft", 0.08f * painPulse); faceRig.Add("noseSneerRight", 0.08f * painPulse);
                        break;
                    case CharacterMood.Attentive:
                        faceRig.Add("browInnerUp", 0.12f); faceRig.Add("eyeWideLeft", 0.08f); faceRig.Add("eyeWideRight", 0.08f);
                        if (!talking) { faceRig.Add("mouthSmileLeft", 0.1f); faceRig.Add("mouthSmileRight", 0.1f); }
                        break;
                    case CharacterMood.Explaining:
                        faceRig.Add("browOuterUpLeft", 0.12f + 0.1f * Mathf.Max(0, Mathf.Sin(t * 1.1f))); faceRig.Add("browOuterUpRight", 0.12f + 0.1f * Mathf.Max(0, Mathf.Sin(t * 1.1f)));
                        break;
                    case CharacterMood.Approving:
                        faceRig.Add("mouthSmileLeft", talking ? 0.15f : 0.32f); faceRig.Add("mouthSmileRight", talking ? 0.15f : 0.32f); faceRig.Add("cheekSquintLeft", 0.12f); faceRig.Add("cheekSquintRight", 0.12f);
                        break;
                    default:
                        if (!talking) { faceRig.Add("mouthSmileLeft", 0.05f); faceRig.Add("mouthSmileRight", 0.05f); }
                        break;
                }
            }

            // ---- Restrained gestures: point toward equipment / gentle explaining hand ----
            pointTimer -= dt;
            float wantPoint = pointTarget && pointTimer > 0 ? 1 : 0;
            pointWeight = Mathf.MoveTowards(pointWeight, wantPoint, dt * 1.6f);
            float explain = talking && (mood == CharacterMood.Explaining || mood == CharacterMood.Attentive) ? (0.5f + 0.5f * Mathf.Sin(t * 1.4f)) : 0;
            if (rightUpperArm && rightLowerArm)
            {
                rightUpperArm.localRotation = uaRest; rightLowerArm.localRotation = laRest;
                Vector3 elbowAxis = Vector3.Cross((rightLowerArm.position - rightUpperArm.position).normalized, root.up).normalized;
                if (elbowAxis.sqrMagnitude < 0.01f) elbowAxis = root.right;
                if (explain > 0 && pointWeight < 0.05f)
                {
                    // forearm lifts slightly forward (elbow flexion) as a natural speaking gesture
                    rightLowerArm.rotation = Quaternion.AngleAxis(-explain * 28f, elbowAxis) * rightLowerArm.rotation;
                }
                if (pointWeight > 0.001f && pointTarget)
                {
                    var hand = rightLowerArm.childCount > 0 ? rightLowerArm.GetChild(0) : rightLowerArm;
                    Vector3 cur = (rightLowerArm.position - rightUpperArm.position).normalized;
                    Vector3 want = Vector3.Slerp(cur, (pointTarget.position - rightUpperArm.position).normalized, 0.55f);
                    var q = Quaternion.FromToRotation(cur, want);
                    rightUpperArm.rotation = Quaternion.Slerp(Quaternion.identity, q, pointWeight) * rightUpperArm.rotation;
                    Vector3 curF = (hand.position - rightLowerArm.position).normalized;
                    Vector3 wantF = (pointTarget.position - rightLowerArm.position).normalized;
                    rightLowerArm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(curF, wantF), pointWeight * 0.9f) * rightLowerArm.rotation;
                }
            }
            if (pointTarget && pointTimer <= 0 && pointWeight <= 0) pointTarget = null;
        }
    }
}
