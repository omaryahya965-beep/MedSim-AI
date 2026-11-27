using System.Collections.Generic;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Core;
using MedSimAI.Instruments;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// Places the MPFB2 characters in the ER scene: poses (reclining patient, standing physician), behaviour components,
    /// ID badge, and examination zones fitted to the actual skin surface by raycasting the posed (baked) mesh.
    /// </summary>
    public static class MedSimRealCharacters
    {
        const string PatientPrefab = "Assets/MedSimAI/Characters/AdamReyes/AdamReyes.prefab";
        const string DoctorPrefab = "Assets/MedSimAI/Characters/DrSaraHassan/DrSaraHassan.prefab";
        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>(PatientPrefab) && AssetDatabase.LoadAssetAtPath<GameObject>(DoctorPrefab);

        static void Aim(Transform bone, Transform child, Vector3 target)
        {
            var cur = (child.position - bone.position).normalized; var want = (target - bone.position).normalized;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        /// <summary>Natural relaxed hand: progressive finger flexion (proximal → distal), thumb slightly in.</summary>
        static void RelaxHands(Animator a, float curlDeg)
        {
            foreach (var left in new[] { true, false })
            {
                var idx = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                var lit = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                var thumb = a.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
                if (!idx || !lit || !thumb) continue;
                var axis = (idx.position - lit.position).normalized;
                var fingers = left
                    ? new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal }
                    : new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal };
                for (int f = 0; f < fingers.Length; f++)
                {
                    var prox = a.GetBoneTransform(fingers[f]); if (!prox) continue;
                    var inter = prox.childCount > 0 ? prox.GetChild(0) : null; var dist = inter && inter.childCount > 0 ? inter.GetChild(0) : null;
                    Transform tipOf(Transform b) => b.childCount > 0 ? b.GetChild(0) : b;
                    // choose flexion sign that brings the fingertip toward the thumb base (palm side)
                    var tip = dist ? dist : (inter ? inter : prox);
                    var before = Vector3.Distance(tipOf(tip).position, thumb.position);
                    var q = Quaternion.AngleAxis(curlDeg, axis);
                    var test = q * (tipOf(tip).position - prox.position) + prox.position;
                    float sign = Vector3.Distance(test, thumb.position) < before ? 1 : -1;
                    float spread = 1f + f * 0.12f; // little finger curls slightly more
                    prox.rotation = Quaternion.AngleAxis(sign * curlDeg * 0.8f * spread, axis) * prox.rotation;
                    if (inter) inter.rotation = Quaternion.AngleAxis(sign * curlDeg * 1.0f * spread, axis) * inter.rotation;
                    if (dist) dist.rotation = Quaternion.AngleAxis(sign * curlDeg * 0.7f * spread, axis) * dist.rotation;
                }
            }
        }

        static (CharacterSpeechController sp, HumanoidLifeController life, FaceRig face) Behaviour(GameObject go, Animator a, Persona persona, string display)
        {
            var face = go.AddComponent<FaceRig>();
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            var v = new GameObject("Voice"); v.transform.SetParent(head, false);
            v.transform.position = head.position + go.transform.forward * 0.09f + go.transform.up * 0.02f;
            v.AddComponent<AudioSource>();
            var sp = v.AddComponent<CharacterSpeechController>(); sp.persona = persona; sp.displayName = display; sp.face = face;
            var life = go.AddComponent<HumanoidLifeController>(); life.speech = sp; life.faceRig = face;
            life.rightUpperArm = a.GetBoneTransform(HumanBodyBones.RightUpperArm); life.rightLowerArm = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            FitBounds(go);
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.shadowCastingMode = r.name.Contains("eyelash") || r.name.Contains("eyebrow") || r.name.Contains("teeth") || r.name.Contains("tongue") || r.name.Contains("low-poly") ? ShadowCastingMode.Off : ShadowCastingMode.On; }
            return (sp, life, face);
        }

        /// <summary>
        /// Recomputes each skinned mesh's local bounds from the posed mesh (+ margin for gaze/breathing/gestures).
        /// Without this, small face meshes (eyes, lashes) keep bind-pose bounds and get frustum-culled once reposed.
        /// </summary>
        static void FitBounds(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh(); r.BakeMesh(m, true);
                var root = r.rootBone ? r.rootBone : r.transform;
                var v = m.vertices; if (v.Length == 0) continue;
                var b = new Bounds(root.InverseTransformPoint(r.transform.TransformPoint(v[0])), Vector3.zero);
                foreach (var p in v) b.Encapsulate(root.InverseTransformPoint(r.transform.TransformPoint(p)));
                b.Expand(0.3f);
                r.localBounds = b; r.updateWhenOffscreen = false;
                Object.DestroyImmediate(m);
            }
        }

        public static MedicalInstructorController BuildInstructor(Transform parent, TMP_FontAsset font)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DoctorPrefab), parent);
            go.name = "Instructor - Dr Sara Hassan";
            go.transform.position = new Vector3(-0.85f, 0, -0.5f);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(1.6f, 0, 0.95f));
            var a = go.GetComponent<Animator>();
            var fwd = go.transform.forward; var up = Vector3.up;
            foreach (var side in new[] { -1, 1 })
            {
                var ua = a.GetBoneTransform(side < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var la = a.GetBoneTransform(side < 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hand = a.GetBoneTransform(side < 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var outward = go.transform.right * side;
                Aim(ua, la, ua.position + (-up * 0.95f + outward * 0.14f + fwd * 0.06f).normalized);
                // relaxed elbow: forearm slightly forward, hands loosely in front of the thighs
                Aim(la, hand, la.position + (-up * 0.85f + fwd * 0.42f - outward * 0.08f).normalized);
            }
            RelaxHands(a, 18f);
            var (sp, life, face) = Behaviour(go, a, Persona.Instructor, "Dr. Hassan (supervisor)");
            life.mood = CharacterMood.Attentive; life.maxHeadYaw = 60;
            var ic = go.AddComponent<MedicalInstructorController>(); ic.speech = sp; ic.life = life; ic.animator = null;
            AddBadge(a, go.transform, font);
            return ic;
        }

        static void AddBadge(Animator a, Transform root, TMP_FontAsset font)
        {
            var chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
            var badge = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(badge.GetComponent<Collider>());
            badge.name = "ID Badge"; badge.transform.SetParent(chest, true);
            badge.transform.position = chest.position + root.forward * 0.165f - root.right * 0.085f + Vector3.up * 0.07f;
            badge.transform.rotation = Quaternion.LookRotation(-root.forward, Vector3.up) * Quaternion.Euler(-8, 0, 0);
            badge.transform.localScale = new Vector3(0.055f, 0.08f, 1) ;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.7f);
            AssetDatabase.CreateAsset(m, "Assets/MedSimAI/Generated/Materials/ID_Badge.mat"); badge.GetComponent<MeshRenderer>().sharedMaterial = m;
            var strip = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(strip.GetComponent<Collider>()); strip.transform.SetParent(badge.transform, false);
            strip.transform.localPosition = new Vector3(0, 0.38f, -0.001f); strip.transform.localScale = new Vector3(1, 0.24f, 1);
            var sm = new Material(m); sm.SetColor("_BaseColor", new Color(0f, 0.55f, 0.52f)); AssetDatabase.CreateAsset(sm, "Assets/MedSimAI/Generated/Materials/ID_Badge_Strip.mat"); strip.GetComponent<MeshRenderer>().sharedMaterial = sm;
            var tgo = new GameObject("Badge Text"); tgo.transform.SetParent(badge.transform, false); tgo.transform.localPosition = new Vector3(0, -0.08f, -0.002f);
            tgo.transform.localScale = new Vector3(1f / 0.055f, 1f / 0.08f, 1) * 0.001f;
            var t = tgo.AddComponent<TextMeshPro>(); if (font) t.font = font;
            t.text = "<b>Dr S. HASSAN</b>\n<size=70%>Emergency Medicine\nSimulation Faculty</size>"; t.fontSize = 60; t.color = new Color(0.04f, 0.1f, 0.18f); t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = new Vector2(52, 50);
        }

        public static PatientInteractionController BuildPatient(Transform parent, Material zoneMat)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PatientPrefab), parent);
            go.name = "Patient - Mr Adam Reyes";
            go.transform.rotation = Quaternion.Euler(0, 180, 0); // faces -Z, toward the foot of the bed
            var a = go.GetComponent<Animator>();
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            var hips = B(HumanBodyBones.Hips);
            // 1. Seat the pelvis on the mattress just in front of the head-section hinge.
            go.transform.position += new Vector3(0.3f, 0.80f, 1.03f) - hips.position;
            // 2. Recline the torso to ~35° above horizontal (55° from vertical), back toward +Z.
            var upper = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            var torsoNow = (upper.position - hips.position).normalized;
            var torsoWant = new Vector3(0, Mathf.Cos(55 * Mathf.Deg2Rad), Mathf.Sin(55 * Mathf.Deg2Rad));
            hips.rotation = Quaternion.FromToRotation(torsoNow, torsoWant) * hips.rotation;
            // 3. Legs extended along the bed toward -Z, resting on the mattress.
            foreach (var side in new[] { -1, 1 })
            {
                var ul = B(side < 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var ll = B(side < 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                var ft = B(side < 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var toes = B(side < 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                float lat = (ul.position.x - hips.position.x) * 1.05f;
                Aim(ul, ll, ul.position + new Vector3(lat * 0.15f, -0.13f, -1f).normalized);
                Aim(ll, ft, ll.position + new Vector3(lat * 0.1f, -0.03f, -1f).normalized);
                if (toes) Aim(ft, toes, ft.position + new Vector3(0, 0.55f, -0.8f).normalized);
            }
            // 4. Arms resting: elbows by the sides, hands on the lap.
            var spineUp = torsoWant; var fwd = go.transform.forward;
            foreach (var side in new[] { -1, 1 })
            {
                var ua = B(side < 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var la = B(side < 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hand = B(side < 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var outward = (ua.position - upper.position); outward.y = 0; outward.z = 0; outward.Normalize();
                Aim(ua, la, ua.position + (-spineUp * 0.85f + outward * 0.2f + Vector3.down * 0.2f).normalized);
                var lap = hips.position + fwd * 0.26f + Vector3.up * 0.07f + outward * 0.1f;
                Aim(la, hand, lap);
            }
            // Slight neck flexion so he looks along the bed rather than at the ceiling.
            var neck = B(HumanBodyBones.Neck);
            neck.rotation = Quaternion.AngleAxis(-22, go.transform.right * -1f) * neck.rotation;

            RelaxHands(a, 24f);
            var (sp, life, face) = Behaviour(go, a, Persona.Patient, "Mr. Reyes (patient)");
            life.lyingDown = true; life.mood = CharacterMood.Discomfort; life.maxHeadYaw = 45; life.maxHeadPitch = 20;
            life.rightUpperArm = null; life.rightLowerArm = null; // no gesturing from the bed
            var pc = go.AddComponent<PatientInteractionController>(); pc.speech = sp; pc.life = life; pc.animator = null;

            FitZones(go, a, zoneMat);
            return pc;
        }

        /// <summary>Creates examination zones on the posed body surface (raycast against baked skinned meshes).</summary>
        static void FitZones(GameObject go, Animator a, Material zoneMat)
        {
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            var temp = new List<GameObject>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!(smr.name == "Human" || smr.name.Contains("shift_dress"))) continue;
                var m = new Mesh(); smr.BakeMesh(m, true);
                var t = new GameObject("TEMP_COLLIDER_" + smr.name); t.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                t.AddComponent<MeshCollider>().sharedMesh = m; temp.Add(t);
            }
            Physics.SyncTransforms();
            var upper = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            var hips = B(HumanBodyBones.Hips);
            var lUA = B(HumanBodyBones.LeftUpperArm); var rUA = B(HumanBodyBones.RightUpperArm);
            Vector3 up = (B(HumanBodyBones.Neck).position - hips.position).normalized;
            Vector3 left = (lUA.position - rUA.position).normalized;            // toward the patient's LEFT
            Vector3 ant = Vector3.Cross(left, up).normalized; if (Vector3.Dot(ant, go.transform.forward) < 0) ant = -ant;
            Vector3 shoulderMid = (lUA.position + rUA.position) * 0.5f;

            void Zone(string id, string display, Transform bone, Vector3 pos, Quaternion rot, Vector3 size, string[] accepts, Transform snap, float maxAngle)
            {
                var z = new GameObject("Zone " + id); z.transform.SetParent(bone, true); z.transform.SetPositionAndRotation(pos, rot);
                var bc = z.AddComponent<BoxCollider>(); bc.size = size; bc.isTrigger = true;
                var pz = z.AddComponent<PlacementZone>(); pz.zoneId = id; pz.displayName = display; pz.acceptedInstruments = accepts; pz.maxAngle = maxAngle; pz.snapPoint = snap;
                var hl = new GameObject("Highlight"); hl.transform.SetParent(z.transform, false);
                hl.AddComponent<MeshFilter>().sharedMesh = SaveMesh(MeshKit.Ellipsoid(new Vector3(size.x, size.y, Mathf.Min(size.z, 0.012f)) * 0.5f, 18, 10), "ZoneHL_" + id);
                var mr = hl.AddComponent<MeshRenderer>(); mr.sharedMaterial = zoneMat; mr.shadowCastingMode = ShadowCastingMode.Off;
                pz.highlight = mr;
            }
            Vector3 Surface(Vector3 p, out Vector3 normal)
            {
                var origin = p + ant * 0.35f;
                foreach (var h in Physics.RaycastAll(origin, -ant, 0.6f))
                    if (h.collider.name.StartsWith("TEMP_COLLIDER")) { normal = h.normal; return h.point + h.normal * 0.004f; }
                normal = ant; return p + ant * 0.1f;
            }
            string[] st = { "stethoscope" };
            var chestZones = new (string id, string name, float down, float lat, Vector3 size)[]
            {
                ("CHEST_AORTIC", "aortic area (R 2nd ICS)", 0.075f, -0.032f, new Vector3(0.05f, 0.05f, 0.06f)),
                ("CHEST_PULMONARY", "pulmonary area (L 2nd ICS)", 0.075f, 0.032f, new Vector3(0.05f, 0.05f, 0.06f)),
                ("CHEST_TRICUSPID", "tricuspid area (L lower sternal edge)", 0.165f, 0.022f, new Vector3(0.05f, 0.05f, 0.06f)),
                ("CHEST_MITRAL", "mitral area (apex)", 0.195f, 0.085f, new Vector3(0.06f, 0.06f, 0.06f)),
                ("CHEST_LUNG_R", "right lung field", 0.12f, -0.105f, new Vector3(0.07f, 0.09f, 0.06f)),
                ("CHEST_LUNG_L", "left lung field", 0.12f, 0.115f, new Vector3(0.06f, 0.09f, 0.06f)),
            };
            foreach (var c in chestZones)
            {
                var p = shoulderMid - up * c.down + left * c.lat;
                var s = Surface(p, out var n);
                Zone(c.id, c.name, upper, s, Quaternion.LookRotation(n, up), c.size, st, null, 75);
            }
            foreach (var (uaB, laB, handB, idxB, id, disp) in new[] {
                (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexDistal, "L", "left"),
                (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightIndexDistal, "R", "right") })
            {
                var ua = B(uaB); var la = B(laB);
                var armDir = (la.position - ua.position).normalized;
                var mid = Vector3.Lerp(ua.position, la.position, 0.45f);
                var rot = Quaternion.FromToRotation(Vector3.up, armDir);
                var snap = new GameObject("Cuff Snap").transform; snap.SetParent(ua, true); snap.SetPositionAndRotation(mid, rot);
                Zone("ARM_UPPER_" + id, disp + " upper arm", ua, mid, rot, new Vector3(0.15f, 0.2f, 0.15f), new[] { "bp_cuff" }, snap, 180);
                var d = B(idxB); var inter = d.parent;
                var fdir = (d.position - inter.position).normalized;
                var tip = d.position + fdir * 0.008f;
                var frot = Quaternion.LookRotation(fdir, ant);
                var fsnap = new GameObject("Oximeter Snap").transform; fsnap.SetParent(d, true); fsnap.SetPositionAndRotation(tip, frot);
                Zone("FINGER_" + id, disp + " index finger", d, tip, frot, new Vector3(0.06f, 0.06f, 0.07f), new[] { "pulse_oximeter" }, fsnap, 180);
            }
            foreach (var t in temp) Object.DestroyImmediate(t);
        }

        static int meshN;
        static Mesh SaveMesh(Mesh m, string name)
        {
            m.name = name; AssetDatabase.CreateAsset(m, $"Assets/MedSimAI/Generated/Meshes/char_{meshN++:000}_{name}.asset"); return m;
        }
    }
}
