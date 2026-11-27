using System.Collections.Generic;
using UnityEngine;

namespace MedSimAI.Core
{
    /// <summary>Procedural mesh generators for believable product geometry (bevelled, smooth-shaded) instead of raw primitives.</summary>
    public static class MeshKit
    {
        /// <summary>Box with rounded edges/corners. Size in metres, radius clamped to half the smallest side.</summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int seg = 4, float uvScale = 1f)
        {
            radius = Mathf.Min(radius, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f - 0.0005f);
            radius = Mathf.Max(radius, 0.0005f);
            var inner = size * 0.5f - Vector3.one * radius;
            // Build from a subdivided cube projected onto a rounded shape.
            int n = seg * 2 + 1; var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float Abs(Vector3 a, Vector3 axis) => Mathf.Abs(Vector3.Dot(a, axis));
            float[] Coords(float half, float inr)
            {
                // seg steps through the bevel band on each side, flat centre in between.
                var c = new float[n + 1];
                for (int k = 0; k <= seg; k++) { float t = 1 - Mathf.Sin(k / (float)seg * Mathf.PI * 0.5f); c[k] = -(inr + (half - inr) * t); c[n - k] = -c[k]; }
                return c;
            }
            void Face(Vector3 normal, Vector3 u, Vector3 v)
            {
                int start = verts.Count;
                var cu = Coords(Abs(size * 0.5f, u), Abs(inner, u)); var cv = Coords(Abs(size * 0.5f, v), Abs(inner, v));
                float hn = Abs(size * 0.5f, normal);
                for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++)
                {
                    Vector3 cube = normal * hn + u * cu[i] + v * cv[j];
                    // remap so segments concentrate near edges
                    Vector3 clamped = new Vector3(Mathf.Clamp(cube.x, -inner.x, inner.x), Mathf.Clamp(cube.y, -inner.y, inner.y), Mathf.Clamp(cube.z, -inner.z, inner.z));
                    Vector3 nrm = (cube - clamped); nrm = nrm.sqrMagnitude < 1e-10f ? normal : nrm.normalized;
                    verts.Add(clamped + nrm * radius); norms.Add(nrm);
                    uvs.Add(new Vector2(Vector3.Dot(cube, u.normalized), Vector3.Dot(cube, v.normalized)) * uvScale);
                }
                for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                {
                    int a = start + j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            }
            Face(Vector3.forward, Vector3.left, Vector3.up); Face(Vector3.back, Vector3.right, Vector3.up);
            Face(Vector3.right, Vector3.forward, Vector3.up); Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward); Face(Vector3.down, Vector3.right, Vector3.back);
            return Build("RoundedBox", verts, norms, uvs, tris);
        }

        /// <summary>Surface of revolution around local Y. Profile points are (radius, height).</summary>
        public static Mesh Lathe(IList<Vector2> profile, int sides = 24, bool smooth = true)
        {
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            int rows = profile.Count;
            float total = 0; var dist = new float[rows]; for (int i = 1; i < rows; i++) { total += Vector2.Distance(profile[i], profile[i - 1]); dist[i] = total; }
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2; float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                for (int r = 0; r < rows; r++)
                {
                    var p = profile[r];
                    verts.Add(new Vector3(p.x * c, p.y, p.x * sn));
                    Vector2 t = (profile[Mathf.Min(r + 1, rows - 1)] - profile[Mathf.Max(r - 1, 0)]).normalized;
                    Vector2 n2 = new Vector2(t.y, -t.x);
                    norms.Add(new Vector3(n2.x * c, n2.y, n2.x * sn).normalized);
                    uvs.Add(new Vector2(s / (float)sides, total > 0 ? dist[r] / total : 0));
                }
            }
            for (int s = 0; s < sides; s++) for (int r = 0; r < rows - 1; r++)
            {
                int a = s * rows + r, b = a + 1, c = a + rows, d = c + 1;
                tris.AddRange(new[] { a, b, c, b, d, c });
            }
            return Build("Lathe", verts, norms, uvs, tris);
        }

        /// <summary>Tube along a polyline path (Catmull-Rom smoothed), e.g. bed rails, IV pole, stethoscope tubing.</summary>
        public static Mesh Tube(IList<Vector3> path, float radius, int sides = 12, int subdiv = 6, bool caps = true)
        {
            var pts = Smooth(path, subdiv);
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            Vector3 prevN = Vector3.zero; float len = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 t = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 nrm = i == 0 ? Vector3.Cross(t, Mathf.Abs(t.y) < 0.9f ? Vector3.up : Vector3.right).normalized : Vector3.Cross(Vector3.Cross(t, prevN), t).normalized;
                prevN = nrm; Vector3 b = Vector3.Cross(t, nrm);
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2; Vector3 d = nrm * Mathf.Cos(a) + b * Mathf.Sin(a);
                    verts.Add(pts[i] + d * radius); norms.Add(d); uvs.Add(new Vector2(s / (float)sides, len * 4));
                }
            }
            int ring = sides + 1;
            for (int i = 0; i < pts.Count - 1; i++) for (int s = 0; s < sides; s++)
            {
                int a = i * ring + s, b2 = a + 1, c = a + ring, d = c + 1;
                tris.AddRange(new[] { a, b2, c, b2, d, c });
            }
            if (caps)
            {
                void Cap(int idx, Vector3 n)
                {
                    int centre = verts.Count; verts.Add(pts[idx]); norms.Add(n); uvs.Add(Vector2.one * 0.5f);
                    int first = verts.Count;
                    for (int s = 0; s <= sides; s++) { verts.Add(verts[idx * ring + s]); norms.Add(n); uvs.Add(Vector2.zero); }
                    for (int s = 0; s < sides; s++) { tris.AddRange(new[] { centre, first + s + 1, first + s }); tris.AddRange(new[] { centre, first + s, first + s + 1 }); }
                }
                Cap(0, (pts[0] - pts[1]).normalized); Cap(pts.Count - 1, (pts[pts.Count - 1] - pts[pts.Count - 2]).normalized);
            }
            return Build("Tube", verts, norms, uvs, tris);
        }

        public static List<Vector3> Smooth(IList<Vector3> p, int subdiv)
        {
            var o = new List<Vector3>();
            if (p.Count < 3 || subdiv <= 1) { o.AddRange(p); return o; }
            for (int i = 0; i < p.Count - 1; i++)
            {
                Vector3 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Count - 1)];
                for (int s = 0; s < subdiv; s++)
                {
                    float t = s / (float)subdiv, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
                }
            }
            o.Add(p[p.Count - 1]);
            return o;
        }

        /// <summary>Ellipsoid (UV sphere) with given radii.</summary>
        public static Mesh Ellipsoid(Vector3 radii, int lon = 24, int lat = 16)
        {
            var prof = new List<Vector2>();
            for (int i = 0; i <= lat; i++) { float a = -Mathf.PI / 2 + Mathf.PI * i / lat; prof.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a))); }
            var m = Lathe(prof, lon);
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = Vector3.Scale(v[i], radii); n[i] = new Vector3(n[i].x / radii.x, n[i].y / radii.y, n[i].z / radii.z).normalized; }
            m.vertices = v; m.normals = n; m.RecalculateBounds(); m.RecalculateTangents(); m.name = "Ellipsoid";
            return m;
        }

        /// <summary>Draped cloth: a grid with procedural folds. Used for curtains, blankets, gowns.</summary>
        public static Mesh Cloth(float width, float height, int xs, int ys, System.Func<float, float, Vector3> displace)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j <= ys; j++) for (int i = 0; i <= xs; i++)
            {
                float u = i / (float)xs, v = j / (float)ys;
                verts.Add(new Vector3((u - 0.5f) * width, (v - 0.5f) * height, 0) + displace(u, v));
                uvs.Add(new Vector2(u * width, v * height));
            }
            for (int j = 0; j < ys; j++) for (int i = 0; i < xs; i++)
            {
                int a = j * (xs + 1) + i, b = a + 1, c = a + xs + 1, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
            var m = new Mesh { name = "Cloth", indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat quad facing -Z with 0..1 UVs (screens, printouts). u increases with +X, v with +Y as seen from -Z.</summary>
        public static Mesh Quad(float w, float h)
        {
            var m = new Mesh { name = "Quad" };
            m.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 3, 0, 3, 1 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateTangents(); m.RecalculateBounds(); return m;
        }

        public static Mesh Combine(params (Mesh mesh, Matrix4x4 m)[] parts)
        {
            var ci = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++) ci[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].m };
            var o = new Mesh { name = "Combined", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; o.CombineMeshes(ci, true, true); o.RecalculateBounds(); return o;
        }

        static Mesh Build(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }
    }
}
