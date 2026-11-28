using UnityEngine;

namespace MedSimAI.XR
{
    /// <summary>
    /// A procedural nitrile-glove hand (no external assets): palm, wrist cuff, four three-segment fingers and a thumb whose
    /// joints curl from a relaxed pose (0) to a closed grip around a handle (1). Used to give the student visible hands:
    /// in VR in place of the generic controller models (curl follows the grip trigger and closes while holding something),
    /// and in Desktop Mode around the instrument being held, so a tool reads as held in a hand instead of floating.
    /// Fingers point along +Z, the palm faces −Y in the hand's own space.
    /// </summary>
    public class GloveHand : MonoBehaviour
    {
        public bool right = true;
        [Range(0, 1)] public float curl, targetCurl;
        public float curlSpeed = 5f;
        Transform[][] fingers; Transform[] thumb;
        static Material glove;
        static readonly float[] FingerX = { 0.028f, 0.009f, -0.010f, -0.028f };      // index … little (right hand; mirrored for left)
        static readonly float[] FingerLen = { 1f, 1.08f, 1.0f, 0.82f };
        static readonly float[] Seg = { 0.026f, 0.020f, 0.017f };
        static readonly float[] Bend = { 62f, 78f, 52f };

        public static GloveHand Create(Transform parent, bool right, string name = null)
        {
            var go = new GameObject(name ?? (right ? "Glove R" : "Glove L"));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GloveHand>(); g.right = right; g.Build();
            return g;
        }

        static Material Mat()
        {
            if (glove) return glove;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            glove = new Material(sh) { name = "Nitrile Glove (procedural)", color = new Color(0.36f, 0.56f, 0.86f) };
            if (glove.HasProperty("_Smoothness")) glove.SetFloat("_Smoothness", 0.62f);
            return glove;
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Vector3 pos)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = pos;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        void Build()
        {
            float s = right ? 1f : -1f;
            Part(transform, "Palm", MedSimAI.Core.MeshKit.Ellipsoid(new Vector3(0.041f, 0.013f, 0.05f), 18, 12), new Vector3(0, 0, 0.0f));
            Part(transform, "Cuff", MedSimAI.Core.MeshKit.Ellipsoid(new Vector3(0.027f, 0.019f, 0.06f), 16, 10), new Vector3(0, 0.003f, -0.08f));
            fingers = new Transform[4][];
            for (int f = 0; f < 4; f++)
            {
                fingers[f] = new Transform[3];
                Transform parent = transform; Vector3 at = new Vector3(FingerX[f] * s, 0.0f, 0.046f);
                for (int k = 0; k < 3; k++)
                {
                    float len = Seg[k] * FingerLen[f]; float r = 0.0076f - k * 0.0008f;
                    var j = new GameObject($"Finger{f}_{k}").transform; j.SetParent(parent, false); j.localPosition = at;
                    Part(j, "seg", MedSimAI.Core.MeshKit.Ellipsoid(new Vector3(r, r * 0.9f, len * 0.58f), 12, 8), new Vector3(0, 0, len * 0.5f));
                    fingers[f][k] = j; parent = j; at = new Vector3(0, 0, len);
                }
            }
            thumb = new Transform[2];
            var tb = new GameObject("ThumbBase").transform; tb.SetParent(transform, false);
            tb.localPosition = new Vector3(0.038f * s, -0.006f, -0.004f); tb.localRotation = Quaternion.Euler(10f, 42f * s, -30f * s);
            Transform tp = tb; Vector3 tat = Vector3.zero;
            for (int k = 0; k < 2; k++)
            {
                float len = k == 0 ? 0.03f : 0.026f; float r = 0.0098f - k * 0.0008f;
                var j = new GameObject($"Thumb_{k}").transform; j.SetParent(tp, false); j.localPosition = tat;
                Part(j, "seg", MedSimAI.Core.MeshKit.Ellipsoid(new Vector3(r, r, len * 0.62f), 12, 8), new Vector3(0, 0, len * 0.5f));
                thumb[k] = j; tp = j; tat = new Vector3(0, 0, len);
            }
            Apply();
        }

        void Update()
        {
            curl = Mathf.MoveTowards(curl, targetCurl, Time.deltaTime * curlSpeed);
            Apply();
        }

        void Apply()
        {
            if (fingers == null) return;
            float s = right ? 1f : -1f;
            for (int f = 0; f < 4; f++)
                for (int k = 0; k < 3; k++)
                    fingers[f][k].localRotation = Quaternion.Euler((8f + Bend[k] * curl) * (k == 0 ? 1f : 1f), 0, 0);   // bend toward the palm (−Y)
            if (thumb != null) { thumb[0].localRotation = Quaternion.Euler(18f * curl, -22f * curl * s, 0); thumb[1].localRotation = Quaternion.Euler(30f * curl, 0, 0); }
        }
    }
}
