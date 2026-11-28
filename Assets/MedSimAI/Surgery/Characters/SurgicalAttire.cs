using UnityEngine;

namespace MedSimAI.Surgery.Characters
{
    /// <summary>
    /// Adapts an existing licensed human model (e.g. the MIT Rocketbox doctor already in the project) to the operating
    /// room: recolours clothing materials to surgical scrubs and adds a scrub cap and a surgical mask on the head bone.
    /// The procedural cap/mask are simple shapes. A dedicated OR character model can replace this later without any code change.
    /// </summary>
    [ExecuteAlways]
    public class SurgicalAttire : MonoBehaviour
    {
        public Color scrubs = new Color(0.20f, 0.45f, 0.52f);     // teal-green scrubs
        public Color cap = new Color(0.22f, 0.40f, 0.62f);
        public Color mask = new Color(0.55f, 0.75f, 0.85f);
        [Tooltip("Renderer/material name fragments treated as clothing (case-insensitive).")]
        public string[] clothingKeywords = { "shirt", "top", "body", "cloth", "pant", "trouser", "coat", "gown", "uniform", "torso", "leg" };
        public bool addCap = true, addMask = true;
        [Tooltip("Replace printed clothing textures with plain fabric (a patient-gown print must not show on staff).")] public bool plainFabric = true;
        [Tooltip("Hide patient-only items on a reused body model (e.g. a hospital wristband).")] public bool hidePatientItems = true;
        [Tooltip("Mask covers mouth/nose; turn off for speaking close-ups if lip sync must be visible.")] public bool maskUp = true;
        [Range(0.8f, 1.3f)] public float headScale = 1f;
        [Tooltip("Heights above the head bone (rigs differ: a Bip01 head bone sits near the jaw).")] public float capHeight = 0.13f, maskHeight = 0.052f;

        bool applied;

        void Start() { if (Application.isPlaying) Apply(); }

        [ContextMenu("Apply surgical attire")]
        public void Apply()
        {
            if (applied) return; applied = true;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("Surgical_")) continue;
                if (hidePatientItems && r.name.ToLowerInvariant().Contains("wristband")) { r.enabled = false; continue; }
                var mats = Application.isPlaying ? r.materials : r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    var key = (r.name + " " + m.name).ToLowerInvariant();
                    // exclusions as whole words on the material name ("Reyes" contains "eye")
                    if (!System.Array.Exists(clothingKeywords, k => key.Contains(k)) || System.Text.RegularExpressions.Regex.IsMatch(m.name.ToLowerInvariant(), @"(^|[^a-z])(skin|hair|eye|eyes|eyelash|lashes|teeth|head)([^a-z]|$)")) continue;
                    if (!Application.isPlaying) { m = new Material(m); mats[i] = m; }
                    if (plainFabric && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", scrubs);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", scrubs);
                    changed = true;
                }
                if (changed && !Application.isPlaying) r.sharedMaterials = mats;
            }
            var anim = GetComponentInChildren<Animator>();
            var head = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (!head) return;
            if (addCap && !head.Find("Surgical_Cap"))
            {
                var c = Shell("Surgical_Cap", new Vector3(0.114f, 0.08f, 0.127f));
                DestroyCollider(c); c.transform.SetParent(head, false);
                Place(c.transform, head, anim, new Vector3(0, capHeight, -0.025f) * headScale, Vector3.one * headScale);
                Tint(c, cap);
            }
            if (addMask && !head.Find("Surgical_Mask"))
            {
                var m = Shell("Surgical_Mask", new Vector3(0.081f, 0.07f, 0.082f));   // curved: wraps nose, mouth and chin
                DestroyCollider(m); m.transform.SetParent(head, false);
                Place(m.transform, head, anim, new Vector3(0, maskUp ? maskHeight : maskHeight - 0.03f, 0.062f) * headScale, Vector3.one * headScale);
                Tint(m, mask);
            }
        }

        /// <summary>Places a head item using the CHARACTER's axes (x right, y up, z forward), whatever the head bone's orientation.</summary>
        void Place(Transform item, Transform head, Animator anim, Vector3 offsetInCharacterAxes, Vector3 size)
        {
            // world up + the humanoid body's facing (FBX root axes can be rotated)
            var up = Vector3.up; var fwd = Vector3.ProjectOnPlane(transform.forward, up);   // the root faces where the character faces (NavMesh turns it)
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize(); var right = Vector3.Cross(up, fwd);
            var world = head.position + right * offsetInCharacterAxes.x + up * offsetInCharacterAxes.y + fwd * offsetInCharacterAxes.z;
            item.SetPositionAndRotation(world, Quaternion.LookRotation(fwd, up));
            var ls = head.lossyScale; item.localScale = new Vector3(size.x / Mathf.Max(1e-4f, ls.x), size.y / Mathf.Max(1e-4f, ls.y), size.z / Mathf.Max(1e-4f, ls.z));
        }

        /// <summary>A smooth ellipsoid shell (radii in metres) for the cap and mask.</summary>
        static GameObject Shell(string name, Vector3 radii)
        {
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = MedSimAI.Core.MeshKit.Ellipsoid(radii, 28, 18);
            return g;
        }

        static void DestroyCollider(GameObject g) { var col = g.GetComponent<Collider>(); if (col) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); } }
        static void Tint(GameObject g, Color c)
        {
            var r = g.GetComponent<Renderer>();
            var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = "Surgical_Cloth" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            r.sharedMaterial = m;
        }
    }
}
