using System.Collections.Generic;
using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Composes facial blend shapes (ARKit face units + Microsoft visemes from MPFB2) across all face meshes
    /// (body, eyebrows, eyelashes, hair...). Behaviours call Add(name, weight01) each frame; FaceRig applies the sum.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class FaceRig : MonoBehaviour
    {
        readonly Dictionary<string, List<(SkinnedMeshRenderer r, int i)>> map = new Dictionary<string, List<(SkinnedMeshRenderer, int)>>();
        readonly Dictionary<string, float> frame = new Dictionary<string, float>();
        readonly Dictionary<string, float> smoothed = new Dictionary<string, float>();
        readonly HashSet<string> touched = new HashSet<string>();
        [Range(1, 40)] public float smoothing = 22f;
        public int ShapeCount => map.Count;

        public static readonly string[] Visemes = { "sil_00", "aa_ah_ax_01", "aa_02", "ao_03", "ey_eh_uh_04", "er_05", "y_iy_ih_ix_06", "w_uw_07", "ow_08", "aw_09", "oy_10", "ay_11", "h_12", "r_13", "l_14", "s_z_15", "sh_ch_jh_zh_16", "th_dh_17", "f_v_18", "d_t_n_19", "k_g_ng_20", "p_b_m_21" };

        void Awake() { Rebuild(); }

        public void Rebuild()
        {
            map.Clear();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = r.sharedMesh; if (!m) continue;
                for (int i = 0; i < m.blendShapeCount; i++)
                {
                    var n = m.GetBlendShapeName(i);
                    int dot = n.LastIndexOf('.'); if (dot >= 0) n = n.Substring(dot + 1);
                    if (!map.TryGetValue(n, out var l)) map[n] = l = new List<(SkinnedMeshRenderer, int)>();
                    l.Add((r, i));
                }
            }
        }

        public bool Has(string name) => map.ContainsKey(name);

        public void Add(string name, float w01)
        {
            if (!map.ContainsKey(name)) return;
            frame.TryGetValue(name, out var cur); frame[name] = cur + w01;
        }

        void LateUpdate()
        {
            touched.Clear();
            foreach (var kv in frame) touched.Add(kv.Key);
            foreach (var kv in smoothed) touched.Add(kv.Key);
            float k = 1 - Mathf.Exp(-smoothing * Time.deltaTime);
            foreach (var name in touched)
            {
                frame.TryGetValue(name, out var target); smoothed.TryGetValue(name, out var cur);
                // blinks must be crisp; everything else is smoothed
                float v = name.StartsWith("eyeBlink") ? target : Mathf.Lerp(cur, target, k);
                smoothed[name] = v;
                float w = Mathf.Clamp01(v) * 100f;
                foreach (var (r, i) in map[name]) r.SetBlendShapeWeight(i, w);
            }
            frame.Clear();
        }

        /// <summary>Maps a written character to an approximate Microsoft viseme (text-timed lip sync).</summary>
        public static string VisemeFor(char c, char next)
        {
            c = char.ToLowerInvariant(c); next = char.ToLowerInvariant(next);
            switch (c)
            {
                case 'a': return next == 'w' ? "aw_09" : next == 'y' || next == 'i' ? "ay_11" : "aa_ah_ax_01";
                case 'e': return "ey_eh_uh_04";
                case 'i': case 'y': return "y_iy_ih_ix_06";
                case 'o': return next == 'w' ? "ow_08" : next == 'y' || next == 'i' ? "oy_10" : "ao_03";
                case 'u': case 'w': return "w_uw_07";
                case 'r': return "r_13";
                case 'l': return "l_14";
                case 's': case 'z': case 'c': return next == 'h' ? "sh_ch_jh_zh_16" : "s_z_15";
                case 'j': return "sh_ch_jh_zh_16";
                case 't': return next == 'h' ? "th_dh_17" : "d_t_n_19";
                case 'd': case 'n': return "d_t_n_19";
                case 'f': case 'v': return "f_v_18";
                case 'k': case 'g': case 'q': case 'x': return "k_g_ng_20";
                case 'p': case 'b': case 'm': return "p_b_m_21";
                case 'h': return "h_12";
                default: return "sil_00";
            }
        }
    }
}
