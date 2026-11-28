using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MedSimAI.Surgery.Environment.Editor
{
    internal sealed class RoomGeometry
    {
        private sealed class Assembly
        {
            public readonly List<Vector3> Points = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Vector2> UV = new();
            public readonly Dictionary<Material, List<int>> Indices = new();
            public bool CastShadows = true;
        }

        private readonly Dictionary<Transform, Assembly> assemblies = new();

        public static Transform Node(Transform parent, string name, Vector3 position = default,
            Vector3 euler = default)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = position;
            node.localEulerAngles = euler;
            return node;
        }

        public static BoxCollider Bounds(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var collider = Node(parent, name, center).gameObject.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private Assembly For(Transform parent)
        {
            if (!assemblies.TryGetValue(parent, out var entry))
                assemblies.Add(parent, entry = new Assembly());
            return entry;
        }

        public void NoShadows(Transform group) => For(group).CastShadows = false;

        private void Surface(Transform parent, Material mat, int width, int height,
            Func<int, int, (Vector3, Vector3, Vector2)> sample)
        {
            var data = For(parent);
            if (!data.Indices.TryGetValue(mat, out var indices))
                data.Indices.Add(mat, indices = new List<int>());
            int first = data.Points.Count;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (point, normal, uv) = sample(x, y);
                data.Points.Add(point);
                data.Normals.Add(normal);
                data.UV.Add(uv);
            }

            for (int y = 0; y < height - 1; y++)
            for (int x = 0; x < width - 1; x++)
            {
                int a = first + y * width + x;
                indices.Add(a);
                indices.Add(a + 1);
                indices.Add(a + width);
                indices.Add(a + 1);
                indices.Add(a + width + 1);
                indices.Add(a + width);
            }
        }

        private static float HalfExtent(Vector3 extent, Vector3 axis) =>
            Mathf.Abs(Vector3.Dot(extent, axis));

        private static float[] FaceSamples(float h, float r)
        {
            if (r <= 0)
                return new[] { -h, h };
            if (r <= .02f)
                return new[] { -h, -h + .40f * r, -h + r, h - r, h - .40f * r, h };
            return new[] { -h, -h + .22f * r, -h + .56f * r, -h + r,
                h - r, h - .56f * r, h - .22f * r, h };
        }

        public void Box(Transform group, Material mat, Vector3 center, Vector3 size, float radius = 0,
            Vector3 euler = default)
        {
            var half = size / 2;
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y, half.z) * .99f);
            var inner = half - Vector3.one * radius;
            var rotation = Quaternion.Euler(euler);
            Face(Vector3.right, Vector3.up, Vector3.forward);
            Face(Vector3.left, Vector3.up, Vector3.back);
            Face(Vector3.forward, Vector3.up, Vector3.left);
            Face(Vector3.back, Vector3.up, Vector3.right);
            Face(Vector3.up, Vector3.forward, Vector3.right);
            Face(Vector3.down, Vector3.back, Vector3.right);

            void Face(Vector3 normal, Vector3 u, Vector3 v)
            {
                float hu = HalfExtent(half, u);
                float hv = HalfExtent(half, v);
                var across = FaceSamples(hu, radius);
                var vertical = FaceSamples(hv, radius);
                Surface(group, mat, across.Length, vertical.Length, (x, y) =>
                {
                    var p = normal * HalfExtent(half, normal) + u * across[x] + v * vertical[y];
                    var n = normal;
                    if (radius > 0)
                    {
                        var core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x),
                            Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        n = (p - core).normalized;
                        p = core + n * radius;
                    }

                    return (center + rotation * p, rotation * n,
                        new Vector2(.5f + across[x] / (2 * hu), .5f + vertical[y] / (2 * hv)));
                });
            }
        }

        public void Plate(Transform group, Material mat, Vector3 position, float width, float height,
            Vector3 euler, Vector2 tiles)
        {
            var rotation = Quaternion.Euler(euler);
            Surface(group, mat, 2, 2, (x, y) =>
            {
                var local = new Vector3((x - .5f) * width, (y - .5f) * height);
                return (position + rotation * local, rotation * Vector3.forward,
                    new Vector2((1 - x) * tiles.x, y * tiles.y));
            });
        }

        public void Lathe(Transform group, Material mat, Vector3 origin, Vector3 axis, int slices,
            params Vector2[] profile)
        {
            // Profile proceeds from the bottom cap through the exterior to the top cap.
            var frame = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            Surface(group, mat, slices + 1, profile.Length, (x, y) =>
            {
                float angle = -2 * Mathf.PI * x / slices;
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var line = profile[Math.Min(y + 1, profile.Length - 1)] -
                           profile[Math.Max(y - 1, 0)];
                var normal = (radial * line.y - Vector3.up * line.x).normalized;
                var point = radial * profile[y].x + Vector3.up * profile[y].y;
                return (origin + frame * point, frame * normal,
                    new Vector2((float)x / slices, (float)y / (profile.Length - 1)));
            });
        }

        public void Disc(Transform group, Material mat, Vector3 center, Vector3 axis,
            float radius, float length, int slices = 24)
        {
            float h = length / 2;
            float bevel = Mathf.Min(radius * .16f, length * .22f);
            Lathe(group, mat, center, axis, slices, new Vector2(0, -h),
                new Vector2(radius - bevel, -h), new Vector2(radius, -h + bevel),
                new Vector2(radius, h - bevel), new Vector2(radius - bevel, h), new Vector2(0, h));
        }

        public void Rod(Transform group, Material mat, Vector3 from, Vector3 to, float radius, int slices = 12) =>
            Disc(group, mat, (from + to) / 2, to - from, radius, (to - from).magnitude, slices);

        public void Hose(Transform group, Material mat, float radius, params Vector3[] waypoints)
        {
            const int steps = 5;
            int samples = (waypoints.Length - 1) * steps + 1;
            var points = new Vector3[samples];
            for (int sample = 0; sample < samples; sample++)
            {
                int segment = Mathf.Min(sample / steps, waypoints.Length - 2);
                float t = (float)(sample - segment * steps) / steps;
                Vector3 a = waypoints[Math.Max(0, segment - 1)];
                Vector3 b = waypoints[segment];
                Vector3 c = waypoints[segment + 1];
                Vector3 d = waypoints[Math.Min(waypoints.Length - 1, segment + 2)];
                points[sample] = .5f * ((2 * b) + (-a + c) * t +
                    (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
            }

            const int sides = 8;
            Surface(group, mat, sides + 1, samples, (x, y) =>
            {
                Vector3 axis = (points[Math.Min(y + 1, samples - 1)] -
                    points[Math.Max(y - 1, 0)]).normalized;
                Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) > .96f ?
                    Vector3.right : Vector3.forward;
                Vector3 u = Vector3.Cross(axis, reference).normalized;
                Vector3 v = Vector3.Cross(axis, u);
                float angle = 2 * Mathf.PI * x / sides;
                Vector3 n = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                return (points[y] + n * radius, n, new Vector2((float)x / sides, (float)y / steps));
            });
        }

        public void Cloth(Transform group, Material mat, Vector3 center, float width, float depth)
        {
            const int steps = 19;
            Surface(group, mat, steps, steps, (x, y) =>
            {
                float u = (float)x / (steps - 1);
                float v = (float)y / (steps - 1);
                var p = Sample(u, v);
                Vector3 across = (Sample(u + .002f, v) - p) / .002f;
                Vector3 along = (Sample(u, v + .002f) - p) / .002f;
                Vector3 n = Vector3.Cross(across, along).normalized;
                return (center + p, n, new Vector2(u, v));
            });

            Vector3 Sample(float u, float v)
            {
                float edge = Mathf.Pow(Mathf.Abs(2 * u - 1), 9);
                float folds = Mathf.Sin(45 * v + 6 * u) * .008f +
                    Mathf.Sin(22 * u - 3 * v) * .003f;
                return new Vector3((u - .5f) * width, -.065f * edge + folds * (.2f + edge),
                    (.5f - v) * depth);
            }
        }

        public void Save(string basePath, bool markStatic)
        {
            foreach (var pair in assemblies)
            {
                var group = pair.Key;
                var geometry = pair.Value;
                if (geometry.Points.Count == 0)
                    continue;

                var mesh = new Mesh { name = group.name };
                if (geometry.Points.Count > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(geometry.Points);
                mesh.SetNormals(geometry.Normals);
                mesh.SetUVs(0, geometry.UV);
                mesh.subMeshCount = geometry.Indices.Count;
                var materials = new Material[mesh.subMeshCount];
                int index = 0;
                foreach (var submesh in geometry.Indices)
                {
                    materials[index] = submesh.Key;
                    mesh.SetTriangles(submesh.Value, index++);
                }

                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                string file = group.name;
                for (Transform parent = group.parent; parent != null; parent = parent.parent)
                    file = parent.name + "_" + file;
                string path = basePath + "/" + file.Replace(' ', '_') + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(mesh, path);
                }

                group.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = group.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = geometry.CastShadows
                    ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (markStatic && !group.GetComponentInParent<UnityEngine.AI.NavMeshObstacle>())
                    GameObjectUtility.SetStaticEditorFlags(group.gameObject,
                        StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                        StaticEditorFlags.OccludeeStatic);
            }

            assemblies.Clear();
        }
    }
}
