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
        [Tooltip("Mask covers mouth/nose; turn off for speaking close-ups if lip sync must be visible.")] public bool maskUp = true;
        [Range(0.8f, 1.3f)] public float headScale = 1f;

        bool applied;

        void Start() { if (Application.isPlaying) Apply(); }

        [ContextMenu("Apply surgical attire")]
        public void Apply()
        {
            if (applied) return; applied = true;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("Surgical_")) continue;
                var mats = Application.isPlaying ? r.materials : r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    var key = (r.name + " " + m.name).ToLowerInvariant();
                    if (!System.Array.Exists(clothingKeywords, k => key.Contains(k)) || key.Contains("skin") || key.Contains("hair") || key.Contains("eye") || key.Contains("teeth") || key.Contains("head")) continue;
                    if (!Application.isPlaying) { m = new Material(m); mats[i] = m; }
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
                var c = GameObject.CreatePrimitive(PrimitiveType.Sphere); c.name = "Surgical_Cap";
                DestroyCollider(c); c.transform.SetParent(head, false);
                c.transform.localPosition = new Vector3(0, 0.085f, -0.01f) * headScale; c.transform.localScale = new Vector3(0.205f, 0.13f, 0.235f) * headScale;
                Tint(c, cap);
            }
            if (addMask && !head.Find("Surgical_Mask"))
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Cube); m.name = "Surgical_Mask";
                DestroyCollider(m); m.transform.SetParent(head, false);
                m.transform.localPosition = new Vector3(0, maskUp ? 0.0f : -0.05f, 0.095f) * headScale; m.transform.localScale = new Vector3(0.15f, 0.085f, 0.03f) * headScale;
                Tint(m, mask);
            }
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
