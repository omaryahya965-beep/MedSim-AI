using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedSimAI.EditorTools
{
    /// <summary>
    /// v0.3 visual polish for the bedside patient:
    /// 1. "Bedridden" body mesh — the legs (jeans + shoes) are removed: they are always under the blanket, and with the
    ///    breathing/idle motion they used to poke through it (dark patches near the feet).
    /// 2. A draped blanket generated from the patient's actual pose (smooth tenting over knees and feet, side and foot
    ///    drapes, folded white top sheet) replacing the hand-fitted blanket with blocky shoe bumps.
    /// </summary>
    public static class MedSimPolish
    {
        const string PatDir = "Assets/MedSimAI/Characters/v2/Patient/";
        const string MatDir = "Assets/MedSimAI/Characters/v2/Materials/";
        const string PatientRoot = "Patient - Mr Adam Reyes";
        static readonly string[] LegBones = { "Thigh", "Calf", "Foot", "Toe" };

        static Mesh OriginalBody()
        {
            return AssetDatabase.LoadAllAssetsAtPath(MedSimCharactersV2.PatientFbx).OfType<Mesh>().First(m => m.name == "AdamReyes_Body");
        }

        [MenuItem("MedSim AI/Polish/1. Bedridden patient body (legs removed under the blanket)")]
        /// <summary>Blanket top edge (world z): the lap, just below the hips; the gown covers the steep belly above.</summary>
        static float BlanketTopZ(Animator an) => an.GetBoneTransform(HumanBodyBones.Hips).position.z - 0.03f;
        static readonly string[] ArmBones = { "Clavicle", "UpperArm", "Forearm", "Hand", "Finger" };

        public static string BedriddenBody()
        {
            var pat = GameObject.Find(PatientRoot); var an = pat.GetComponent<Animator>(); float zTop = BlanketTopZ(an);
            var log = new System.Text.StringBuilder();
            foreach (var name in new[] { "AdamReyes_Body", "AdamReyes_Gown" })
            {
                var smr = pat.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == name);
                var src = AssetDatabase.LoadAllAssetsAtPath(MedSimCharactersV2.PatientFbx).OfType<Mesh>().First(m => m.name == name);
                var legIdx = new HashSet<int>(smr.bones.Select((b, i) => (b, i)).Where(x => LegBones.Any(n => x.b.name.Contains(n))).Select(x => x.i));
                var armIdx = new HashSet<int>(smr.bones.Select((b, i) => (b, i)).Where(x => ArmBones.Any(n => x.b.name.Contains(n))).Select(x => x.i));
                var bw = src.boneWeights;
                float W(int v, HashSet<int> set) { var w = bw[v]; float s = 0; if (set.Contains(w.boneIndex0)) s += w.weight0; if (set.Contains(w.boneIndex1)) s += w.weight1; if (set.Contains(w.boneIndex2)) s += w.weight2; if (set.Contains(w.boneIndex3)) s += w.weight3; return s; }
                // pose-space positions: in the fixed reclined pose, anything entirely under the blanket is never visible
                var keep = smr.sharedMesh; smr.sharedMesh = src; var baked = new Mesh(); smr.BakeMesh(baked, true); smr.sharedMesh = keep;
                var wp = baked.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray(); Object.DestroyImmediate(baked);
                bool Under(int v) => wp[v].z < zTop - 0.015f && Mathf.Abs(wp[v].x - 0.30f) < 0.46f && W(v, armIdx) < 0.3f;
                var mesh = Object.Instantiate(src); mesh.name = name + "_Bedridden";
                int legs = 0, under = 0, kept = 0;
                for (int sm = 0; sm < src.subMeshCount; sm++)
                {
                    var tris = src.GetTriangles(sm); var outT = new List<int>(tris.Length);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        int legVerts = (W(a, legIdx) > 0.5f ? 1 : 0) + (W(b, legIdx) > 0.5f ? 1 : 0) + (W(c, legIdx) > 0.5f ? 1 : 0);
                        if (legVerts >= 2) { legs++; continue; }
                        if (Under(a) && Under(b) && Under(c)) { under++; continue; }
                        outT.Add(a); outT.Add(b); outT.Add(c); kept++;
                    }
                    mesh.SetTriangles(outT, sm);
                }
                var path = PatDir + name + "_Bedridden.asset";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
                smr.sharedMesh = mesh; EditorUtility.SetDirty(smr);
                log.AppendLine($"{name}: removed {legs} leg + {under} under-blanket triangles, kept {kept}");
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pat.scene);
            return log.ToString();
        }

        [MenuItem("MedSim AI/Polish/2. Draped blanket from the patient's pose")]
        public static string DrapedBlanket()
        {
            var log = new System.Text.StringBuilder();
            var pat = GameObject.Find(PatientRoot); var an = pat.GetComponent<Animator>();
            var body = pat.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == "AdamReyes_Body");
            var gown = pat.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == "AdamReyes_Gown");
            // --- sample the body volume (with legs) in the reclined pose; arms/hands are excluded (they rest ON the blanket)
            var pts = new List<Vector3>();
            void Collect(SkinnedMeshRenderer r, Mesh m, System.Func<int, bool> include)
            {
                var keep = r.sharedMesh; r.sharedMesh = m; var baked = new Mesh(); r.BakeMesh(baked, true); r.sharedMesh = keep;
                var v = baked.vertices; for (int i = 0; i < v.Length; i++) if (include(i)) pts.Add(r.transform.TransformPoint(v[i]));
                Object.DestroyImmediate(baked);
            }
            var lowerBones = new HashSet<int>(body.bones.Select((b, i) => (b, i)).Where(x => x.b.name == "Bip01 Pelvis" || x.b.name == "Bip01 Spine" || LegBones.Any(n => x.b.name.Contains(n))).Select(x => x.i));
            var ob = OriginalBody(); var obw = ob.boneWeights;
            Collect(body, ob, i => lowerBones.Contains(obw[i].boneIndex0) && obw[i].weight0 > 0.5f);
            var gbw = gown.sharedMesh.boneWeights; var gLower = new HashSet<int>(gown.bones.Select((b, i) => (b, i)).Where(x => x.b.name == "Bip01 Pelvis" || LegBones.Any(n => x.b.name.Contains(n))).Select(x => x.i));
            Collect(gown, gown.sharedMesh, i => gLower.Contains(gbw[i].boneIndex0));
            var hips = an.GetBoneTransform(HumanBodyBones.Hips).position;

            // --- bed geometry (Hospital Bed: foot mattress flat, head section inclined 35° from the hinge)
            const float cx = 0.30f, half = 0.44f, top = 0.675f, hingeZ = 1.075f, cell = 0.02f;
            float incline = Mathf.Tan(35f * Mathf.Deg2Rad);
            float zFoot = -0.07f, zTop = hips.z - 0.03f; // lap: the gown covers the steep belly above
            float Mattress(float z) => z <= hingeZ ? top : top + (z - hingeZ) * incline;
            int nx = Mathf.RoundToInt(2 * half / cell), nz = Mathf.RoundToInt((zTop - zFoot) / cell);
            var h = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) h[i, j] = float.NegativeInfinity;
            foreach (var p in pts)
            {
                int i = Mathf.RoundToInt((p.x - (cx - half)) / cell), j = Mathf.RoundToInt((p.z - zFoot) / cell);
                if (i < 0 || i > nx || j < 0 || j > nz) continue;
                if (p.y > h[i, j]) h[i, j] = p.y;
            }
            // fill empty cells with the mattress, dilate (cloth spans gaps between knees and feet), then blur (drape)
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) { float m = Mattress(zFoot + j * cell); if (h[i, j] < m) h[i, j] = m; }
            float[,] Dilate(float[,] a, int r) { var o = (float[,])a.Clone(); for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) for (int di = -r; di <= r; di++) for (int dj = -r; dj <= r; dj++) { int ii = i + di, jj = j + dj; if (ii < 0 || ii > nx || jj < 0 || jj > nz || di * di + dj * dj > r * r) continue; float fall = Mathf.Sqrt(di * di + dj * dj) * cell * 0.45f; o[i, j] = Mathf.Max(o[i, j], a[ii, jj] - fall); } return o; }
            float[,] Blur(float[,] a, int r) { var o = new float[nx + 1, nz + 1]; for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) { float s = 0, w = 0; for (int di = -r; di <= r; di++) for (int dj = -r; dj <= r; dj++) { int ii = Mathf.Clamp(i + di, 0, nx), jj = Mathf.Clamp(j + dj, 0, nz); float k = Mathf.Exp(-(di * di + dj * dj) / (0.5f * r * r)); s += a[ii, jj] * k; w += k; } o[i, j] = s / w; } return o; }
            var dil = Dilate(h, 3);
            var d = Blur(dil, 3);
            // the top edge must be a clean line: extra smoothing across the last rows
            for (int j = Mathf.Max(0, nz - 8); j <= nz; j++) { var row = new float[nx + 1]; for (int i = 0; i <= nx; i++) { float s2 = 0, w2 = 0; for (int k = -4; k <= 4; k++) { int ii = Mathf.Clamp(i + k, 0, nx); float wk = Mathf.Exp(-k * k / 8f); s2 += d[ii, j] * wk; w2 += wk; } row[i] = s2 / w2; } for (int i = 0; i <= nx; i++) d[i, j] = row[i]; }
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++)
            {
                float m = Mattress(zFoot + j * cell) + 0.012f;
                d[i, j] = Mathf.Max(d[i, j] + 0.02f, m);
                // gentle tuck towards the mattress edges (blanket is pulled down at the sides)
                float ex = Mathf.Abs((cx - half + i * cell) - cx) / half; if (ex > 0.85f) d[i, j] = Mathf.Lerp(d[i, j], m + 0.01f, Mathf.SmoothStep(0, 1, (ex - 0.85f) / 0.15f) * 0.6f);
            }

            // --- mesh: core grid + side/foot drapes (D rows hanging down) ; top edge folded sheet cuff
            const int D = 7; const float drop = 0.23f;
            int W = nx + 1 + 2 * D, H = nz + 1 + D;
            Vector3 P(int gi, int gj)
            {
                int i = gi - D, j = gj - D; int ci = Mathf.Clamp(i, 0, nx), cj = Mathf.Clamp(j, 0, nz);
                float x = cx - half + ci * cell, z = zFoot + cj * cell, y = d[ci, cj];
                float ox = i < 0 ? -i : i > nx ? i - nx : 0, oz = j < 0 ? -j : 0; float t = Mathf.Max(ox, oz) / D;
                if (t > 0)
                {
                    float wave = 0.006f * Mathf.Sin(z * 23f + x * 17f) * t;
                    x += Mathf.Sign(i < 0 ? -1 : i > nx ? 1 : 0) * (0.012f + 0.03f * t + wave) * (ox > 0 ? 1 : 0);
                    z -= (0.012f + 0.03f * t + wave) * (oz > 0 ? 1 : 0);
                    y -= drop * Mathf.Pow(t, 1.15f);
                }
                return new Vector3(x, y, z);
            }
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int gj = 0; gj < H; gj++) for (int gi = 0; gi < W; gi++) { verts.Add(P(gi, gj)); uvs.Add(new Vector2(gi * cell / 0.45f, gj * cell / 0.45f)); }
            for (int gj = 0; gj < H - 1; gj++) for (int gi = 0; gi < W - 1; gi++)
            {
                int a = gj * W + gi, b = a + 1, c = a + W, e = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, e });
            }
            var mesh = new Mesh { name = "Patient_Blanket_Draped", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            // fix winding so normals face up
            if (mesh.normals[(H / 2) * W + W / 2].y < 0) { tris.Reverse(); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); }

            // folded top sheet: last 6 core rows (+ drapes), lifted slightly
            var cv = new List<Vector3>(); var cuv = new List<Vector2>(); var ct = new List<int>(); int rows = 6;
            var nrm = mesh.normals;
            for (int r = 0; r <= rows; r++) for (int gi = 0; gi < W; gi++) { int gj = H - 1 - rows + r; int idx = gj * W + gi; cv.Add(verts[idx] + nrm[idx] * (0.005f + 0.002f * r)); cuv.Add(new Vector2(gi * cell / 0.3f, r * cell / 0.3f)); }
            // rolled edge: one extra row tucked back under at the top
            for (int gi = 0; gi < W; gi++) { int idx = (H - 1) * W + gi; cv.Add(verts[idx] + Vector3.forward * 0.012f - nrm[idx] * 0.004f); cuv.Add(new Vector2(gi * cell / 0.3f, (rows + 1) * cell / 0.3f)); }
            for (int r = 0; r < rows + 1; r++) for (int gi = 0; gi < W - 1; gi++) { int a = r * W + gi, b = a + 1, c = a + W, e = c + 1; ct.AddRange(new[] { a, c, b, b, c, e }); }
            var cuff = new Mesh { name = "Patient_TopSheet_Fold" }; cuff.SetVertices(cv); cuff.SetUVs(0, cuv); cuff.SetTriangles(ct, 0); cuff.RecalculateNormals(); cuff.RecalculateBounds();
            if (cuff.normals[W / 2].y < 0) { ct.Reverse(); cuff.SetTriangles(ct, 0); cuff.RecalculateNormals(); }

            AssetDatabase.DeleteAsset(PatDir + "Patient_Blanket_Draped.asset"); AssetDatabase.CreateAsset(mesh, PatDir + "Patient_Blanket_Draped.asset");
            AssetDatabase.DeleteAsset(PatDir + "Patient_TopSheet_Fold.asset"); AssetDatabase.CreateAsset(cuff, PatDir + "Patient_TopSheet_Fold.asset");

            // --- materials: blanket two-sided (drapes are seen from both sides); white cotton sheet
            var bl = GameObject.Find("Patient Blanket").transform; var old = bl.Find("Blanket Mesh");
            var baseMat = old ? old.GetComponentInChildren<MeshRenderer>(true).sharedMaterial : AssetDatabase.LoadAssetAtPath<Material>(MatDir + "Blanket_Cotton.mat");
            var two = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "Blanket_Draped.mat");
            if (!two) { two = new Material(baseMat) { name = "Blanket_Draped" }; AssetDatabase.CreateAsset(two, MatDir + "Blanket_Draped.mat"); }
            else two.CopyPropertiesFromMaterial(baseMat);
            two.SetFloat("_Cull", 0f); two.doubleSidedGI = true; EditorUtility.SetDirty(two);
            var sheetSrc = GameObject.Find("Sheet_Head"); var sheetMat = sheetSrc ? sheetSrc.GetComponent<Renderer>().sharedMaterial : null;
            var sheet = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "TopSheet_Cotton.mat");
            if (!sheet) { sheet = sheetMat ? new Material(sheetMat) : new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(sheet, MatDir + "TopSheet_Cotton.mat"); }
            sheet.SetFloat("_Cull", 0f); sheet.SetColor("_BaseColor", new Color(0.93f, 0.94f, 0.93f)); sheet.SetFloat("_Smoothness", 0.12f); EditorUtility.SetDirty(sheet);

            if (old) old.gameObject.SetActive(false);
            foreach (var n in new[] { "Blanket Draped", "Top Sheet Fold" }) { var t = bl.Find(n); if (t) Object.DestroyImmediate(t.gameObject); }
            GameObject Make(string n, Mesh m, Material mat)
            {
                var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(bl, false);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
                go.GetComponent<MeshFilter>().sharedMesh = m; var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                GameObjectUtility.SetStaticEditorFlags(go, 0); return go;
            }
            Make("Blanket Draped", mesh, two); Make("Top Sheet Fold", cuff, sheet);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bl.gameObject.scene);
            log.AppendLine($"draped blanket: {pts.Count} body samples, grid {nx}x{nz} (+{D} drape rows), top edge z={zTop:F2}, verts {verts.Count}; top sheet fold {cv.Count} verts");
            return log.ToString();
        }
    }
}
