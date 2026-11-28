using System.Collections.Generic;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Rolls a gown sleeve up to the shoulder so the upper arm is bare for a blood-pressure cuff (manual testing: the sleeve
    /// covered the cuff site and the cuff clipped through the fabric). Built once from the gown's own skinned mesh: sleeve
    /// vertices (weighted to that side's upper arm / forearm) are pulled toward the shoulder along the arm axis in bind space
    /// and slightly widened, like fabric pushed up and bunched. The rolled variants keep the skinning, so they move with the
    /// arm. Visual only; <see cref="SetRolled"/> swaps the mesh (cheap, no per-frame cost).
    /// </summary>
    public class GownSleeveRoll : MonoBehaviour
    {
        public SkinnedMeshRenderer gown;
        [Range(0.05f, 1f)] public float keepFraction = 0.22f;   // how much of the sleeve length remains below the shoulder
        public float bunch = 1.14f;                               // bunched fabric is a little wider
        Mesh original; readonly Dictionary<int, Mesh> variants = new Dictionary<int, Mesh>();
        public bool LeftRolled { get; private set; }
        public bool RightRolled { get; private set; }

        void Awake()
        {
            if (!gown) foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name.ToLowerInvariant().Contains("gown")) { gown = r; break; }
            if (gown) original = gown.sharedMesh;
        }

        /// <summary>Rolls (or lowers) the sleeves. Returns false when the gown or its arm bones are not found.</summary>
        public bool SetRolled(bool left, bool right)
        {
            if (!gown || !original) return false;
            LeftRolled = left; RightRolled = right;
            int key = (left ? 1 : 0) | (right ? 2 : 0);
            if (key == 0) { gown.sharedMesh = original; return true; }
            if (!variants.TryGetValue(key, out var m)) { m = Build(left, right); if (!m) return false; variants[key] = m; }
            gown.sharedMesh = m;
            return true;
        }
        public bool Roll(string side) => SetRolled(LeftRolled || side == "left", RightRolled || side == "right");

        Mesh Build(bool left, bool right)
        {
            var bones = gown.bones; var bind = original.bindposes;
            int Find(string part) { for (int i = 0; i < bones.Length; i++) if (bones[i] && bones[i].name.Replace(" ", "").ToLowerInvariant().EndsWith(part)) return i; return -1; }
            var m = Instantiate(original); m.name = original.name + (left ? "_rolledL" : "") + (right ? "_rolledR" : "");
            var v = m.vertices; var bw = original.boneWeights;
            if (bw == null || bw.Length != v.Length) return null;
            foreach (var (on, u, f) in new[] { (left, Find("lupperarm"), Find("lforearm")), (right, Find("rupperarm"), Find("rforearm")) })
            {
                if (!on || u < 0 || f < 0) continue;
                Vector3 S = bind[u].inverse.MultiplyPoint3x4(Vector3.zero), E = bind[f].inverse.MultiplyPoint3x4(Vector3.zero);
                var axis = E - S; float len2 = axis.sqrMagnitude; if (len2 < 1e-8f) continue;
                for (int i = 0; i < v.Length; i++)
                {
                    float w = W(bw[i], u) + W(bw[i], f);
                    if (w < 0.6f) continue;                                   // torso / armpit fabric stays where it is
                    float t = Vector3.Dot(v[i] - S, axis) / len2; if (t <= 0f) continue;
                    var radial = (v[i] - S) - axis * t;
                    float t2 = Mathf.Min(t, 1f) * keepFraction;
                    v[i] = S + axis * t2 + radial * Mathf.Lerp(1f, bunch, Mathf.Clamp01(t / 0.5f));
                }
            }
            m.vertices = v; m.RecalculateBounds();
            return m;
        }
        static float W(BoneWeight b, int i) => (b.boneIndex0 == i ? b.weight0 : 0) + (b.boneIndex1 == i ? b.weight1 : 0) + (b.boneIndex2 == i ? b.weight2 : 0) + (b.boneIndex3 == i ? b.weight3 : 0);

        void OnDestroy() { foreach (var m in variants.Values) if (m) Destroy(m); }
    }
}
