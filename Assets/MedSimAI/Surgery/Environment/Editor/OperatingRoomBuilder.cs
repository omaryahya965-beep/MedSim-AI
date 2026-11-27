using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static MedSimAI.Surgery.Environment.Editor.RoomGeometry;

namespace MedSimAI.Surgery.Environment.Editor
{
    internal sealed partial class OperatingRoomBuilder
    {
        internal const string Root = "Assets/MedSimAI/Surgery/Environment";
        internal const string ScenePath = "Assets/MedSimAI/Surgery/Scenes/OperatingRoom_Environment.unity";
        private const string RoomPath = Root + "/Prefabs/OperatingRoom.prefab";
        private const string RobotPath = Root + "/Prefabs/OrthopedicAssistRobot.prefab";
        private readonly RoomGeometry art = new();
        private readonly Dictionary<string, Material> materials = new();

        private static Vector3 V(float x, float y, float z) => new(x, y, z);
        private Material M(string name) => materials[name];

        [MenuItem("MedSim AI/Surgery/Rebuild Operating Room Prototype")]
        public static void Rebuild()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var builder = new OperatingRoomBuilder();
            builder.Build();
        }

        private void Build()
        {
            foreach (string path in new[] { "Materials", "Models", "Prefabs", "Lighting" })
                Directory.CreateDirectory(Root + "/" + path);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            AssetDatabase.Refresh();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureDemoLighting();
            ConfigureTextures();
            CreatePalette();
            AssetDatabase.SaveAssets();
            BuildRobotPrefab();

            Transform room = new GameObject("OperatingRoom").transform;
            BuildArchitecture(room);
            BuildEquipment(room);
            BuildLighting(room);

            var robot = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath), scene);
            robot.name = "RobotBase";
            robot.transform.SetParent(room, false);
            robot.transform.localPosition = V(1.99f, 0, -.94f);

            Transform anchors = Node(room, "IntegrationAnchors");
            Facing(anchors, "PlayerSpawn", V(-1.93f, 0, -2.79f), V(-.1f, 0, -.65f));
            Node(anchors, "PatientRoot", V(0, .92f, 0));
            Node(anchors, "SurgicalSite", V(-.14f, 1.04f, -.51f));
            Facing(anchors, "SurgeonPosition", V(-1.16f, 0, -.56f), V(-.14f, 0, -.51f));
            Facing(anchors, "AssistantPosition", V(-.84f, 0, .87f), V(-.14f, 0, -.51f));
            Facing(anchors, "RoomExit", V(-1.92f, 0, -4.72f), V(-1.92f, 0, -5.52f));

            AddReflection(room);
            art.Save(Root + "/Models", true);
            BakeNavigation(room);

            PrefabUtility.SaveAsPrefabAssetAndConnect(room.gameObject, RoomPath, InteractionMode.AutomatedAction);
            var views = new GameObject("DemoViewpoints").transform;
            Camera camera = Node(views, "Environment Camera").gameObject.AddComponent<Camera>();
            camera.gameObject.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>();
            camera.nearClipPlane = .045f;
            camera.farClipPlane = 30;
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            var markers = new Transform[Viewpoints.Length];
            var fieldsOfView = new float[Viewpoints.Length];
            for (int i = 0; i < Viewpoints.Length; i++)
            {
                var view = Viewpoints[i];
                markers[i] = Facing(views, view.Name, view.Position, view.Target);
                fieldsOfView[i] = view.Fov;
            }
            camera.gameObject.AddComponent<OperatingRoomTour>().Configure(markers, fieldsOfView);
            SetView(camera, 0);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Operating Room] Saved demo scene, room prefab, nested robot prefab, and baked navigation.");
        }

        internal static readonly (string Name, Vector3 Position, Vector3 Target, float Fov)[] Viewpoints =
        {
            ("01-overview", V(-3.25f, 2.12f, -3.44f), V(.19f, 1.28f, .47f), 66),
            ("02-surgeon", V(-1.50f, 1.64f, -.72f), V(.31f, 1.10f, .05f), 62),
            ("03-table", V(.97f, 1.57f, -2.14f), V(.01f, .88f, .14f), 52),
            ("04-equipment", V(-1.29f, 1.65f, 1.19f), V(.60f, 1.40f, 2.68f), 59),
            ("05-robot", V(.63f, 1.57f, -2.88f), V(1.70f, 1.10f, -.79f), 50),
            ("06-entry", V(-1.98f, 1.66f, -1.33f), V(-1.92f, 1.45f, -3.90f), 61)
        };

        internal static void SetView(Camera camera, int index)
        {
            var view = Viewpoints[index];
            camera.transform.SetPositionAndRotation(view.Position, Quaternion.LookRotation(view.Target - view.Position));
            camera.fieldOfView = view.Fov;
        }

        private static Transform Facing(Transform parent, string name, Vector3 position, Vector3 target)
        {
            var node = Node(parent, name, position);
            node.localRotation = Quaternion.LookRotation(target - position);
            return node;
        }

        private static void ConfigureDemoLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Rgb("#b3c6cb");
            RenderSettings.ambientEquatorColor = Rgb("#93a5a9");
            RenderSettings.ambientGroundColor = Rgb("#617473");
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = .75f;
            RenderSettings.fog = false;
        }

        private static void BakeNavigation(Transform room)
        {
            Transform walkable = room.Find("Architecture/Walkable Floor");
            int nonWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
            foreach (Collider collider in room.GetComponentsInChildren<Collider>())
            {
                if (collider.transform.IsChildOf(walkable) ||
                    collider.GetComponentInParent<NavMeshObstacle>())
                    continue;
                var modifier = collider.gameObject.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = nonWalkableArea;
            }

            var surface = room.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;
            surface.voxelSize = .07f;
            surface.BuildNavMesh();
            NavMeshData generated = surface.navMeshData;
            if (!generated)
                throw new InvalidOperationException("The operating room NavMesh bake returned no data.");

            const string path = Root + "/Lighting/OperatingRoomNavigation.asset";
            var saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            if (saved)
            {
                surface.RemoveData();
                EditorUtility.CopySerialized(generated, saved);
                EditorUtility.SetDirty(saved);
                UnityEngine.Object.DestroyImmediate(generated);
                surface.navMeshData = saved;
                surface.AddData();
            }
            else
            {
                generated.name = "Operating Room Walkable Surface";
                AssetDatabase.CreateAsset(generated, path);
            }
        }

        private static Color Rgb(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out Color color))
                throw new ArgumentException("Invalid palette color", nameof(hex));
            return color;
        }

        private void CreatePalette()
        {
            Lit("Wall", "#bdcdd0", .24f);
            Lit("WallPale", "#d1d8d5", .29f);
            Lit("WallBlue", "#739ca1", .32f);
            Lit("WallSeam", "#7a9296", .18f);
            Lit("Door", "#6e9298", .33f);
            Lit("Ceiling", "#e1e5e0", .25f);
            Lit("Gasket", "#344a51", .29f);
            Lit("Cove", "#728b8d", .35f);
            Lit("Floor", "#a0afab", .30f, 0, "Floor");
            Lit("Field", "#91a9ab", .29f, 0, "Floor");
            Lit("FloorWeld", "#839794", .28f);
            Lit("Lane", "#d7e1d7", .27f);
            Lit("Shell", "#e7ebea", .43f);
            Lit("ShellSide", "#b7c7ca", .43f);
            Lit("PaintedMetal", "#a5b6ba", .46f, .20f);
            Lit("Steel", "#b9c6cb", .50f, .72f, "Steel");
            Lit("BrightSteel", "#d0dadd", .58f, .73f);
            Lit("Rubber", "#263538", .19f);
            Lit("Pad", "#294649", .29f, 0, "Fabric");
            Lit("Fabric", "#42898e", .22f, 0, "Fabric");
            Lit("Drape", "#377080", .18f, 0, "Fabric");
            Lit("Blue", "#3e7186", .43f);
            Lit("Teal", "#338b8b", .40f);
            Lit("Yellow", "#d9b856", .38f);
            Lit("Red", "#aa4641", .38f);
            Lit("ScreenFrame", "#25353d", .40f);
            Lit("VisionGlass", "#5f939d", .78f, .12f);
            Glass();
            Glow("CeilingDiffuser", "#eefbff", 1.16f);
            Glow("LightLens", "#f2f6e7", 1.42f);
            Glow("StatusGreen", "#9ce2b5", 1.12f);
            Glow("StatusAmber", "#f3c078", 1.08f);
            foreach (string texture in new[] { "Vitals", "Anesthesia", "Robot", "WallStatus" })
                Glow(texture, "#ffffff", 1f, texture);
            foreach (string texture in new[] { "RoomSign", "SupplySign", "UtilitySign", "WasteSign" })
                Lit(texture, "#ffffff", .30f, 0, texture: texture);
            foreach (var material in materials.Values)
                EditorUtility.SetDirty(material);
        }

        private void Lit(string name, string tint, float smoothness, float metallic = 0,
            string maps = null, string texture = null)
        {
            var mat = GetMaterial(name, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", Rgb(tint));
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (maps == null)
            {
                if (texture != null)
                    mat.SetTexture("_BaseMap", Texture(texture));
                return;
            }

            mat.SetTexture("_BaseMap", Texture(maps + "Color"));
            mat.SetTexture("_BumpMap", Texture(maps + "Normal"));
            mat.SetFloat("_BumpScale", maps == "Floor" ? .50f : .7f);
            mat.EnableKeyword("_NORMALMAP");
            if (maps == "Fabric")
                mat.SetTextureScale("_BaseMap", new Vector2(2, 2));
        }

        private void Glow(string name, string tint, float intensity, string texture = null)
        {
            var mat = GetMaterial(name, "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", Rgb(tint) * intensity);
            if (texture != null)
                mat.SetTexture("_BaseMap", Texture(texture));
        }

        private void Glass()
        {
            var mat = GetMaterial("Glass", "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", new Color(.48f, .68f, .72f, .19f));
            mat.SetFloat("_Smoothness", .82f);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_BlendModePreserveSpecular", 1);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        private Material GetMaterial(string name, string shaderName)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (!shader)
                throw new InvalidOperationException("Missing project shader " + shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material)
                material.shader = shader;
            else
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            materials.Add(name, material);
            return material;
        }

        private static void ConfigureTextures()
        {
            foreach (string prefix in new[] { "Floor", "Fabric", "Steel" })
            {
                ConfigureTexture(prefix + "Color", true, false);
                ConfigureTexture(prefix + "Normal", true, true);
            }
            foreach (string name in new[] {"Vitals", "Anesthesia", "Robot", "WallStatus",
                         "RoomSign", "SupplySign", "UtilitySign", "WasteSign" })
                ConfigureTexture(name, false, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static Texture2D Texture(string name)
        {
            string path = Root + "/Textures/" + name + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture)
                throw new FileNotFoundException($"Could not load the imported 2D texture {name}.", path);
            return texture;
        }

        private static void ConfigureTexture(string name, bool repeat, bool normal)
        {
            string path = Root + "/Textures/" + name + ".png";
            var import = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!import)
                throw new FileNotFoundException("Missing operating room source texture", path);

            var textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (import.textureType != textureType || import.textureShape != TextureImporterShape.Texture2D ||
                import.wrapMode != wrap || import.maxTextureSize != 1024 ||
                import.npotScale != TextureImporterNPOTScale.None || import.anisoLevel != 4)
            {
                import.textureType = textureType;
                import.textureShape = TextureImporterShape.Texture2D;
                import.wrapMode = wrap;
                import.maxTextureSize = 1024;
                import.npotScale = TextureImporterNPOTScale.None;
                import.alphaSource = TextureImporterAlphaSource.None;
                import.textureCompression = TextureImporterCompression.CompressedHQ;
                import.anisoLevel = 4;
                import.SaveAndReimport();
            }
        }

        private static void AddReflection(Transform room)
        {
            const string path = Root + "/Lighting/OperatingRoomReflection.asset";
            var map = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (!map)
            {
                const int size = 32;
                map = new Cubemap(size, TextureFormat.RGBA32, true) { name = "Operating Room Neutral Reflection" };
                foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX,
                    CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                {
                    Color upper = Rgb("#b4c8ce");
                    Color lower = Rgb("#667c80");
                    var colors = new Color[size * size];
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float height = face == CubemapFace.PositiveY ? 1 :
                            face == CubemapFace.NegativeY ? 0 : (float)y / (size - 1);
                        Color tint = Color.Lerp(lower, upper, height);
                        if (face == CubemapFace.PositiveY && (x is > 4 and < 12 || x is > 21 and < 28) &&
                            y is > 5 and < 27)
                            tint = Rgb("#fbffff");
                        colors[y * size + x] = tint;
                    }
                    map.SetPixels(colors, face);
                }
                map.Apply();
                AssetDatabase.CreateAsset(map, path);
            }

            var probe = Node(room, "Room Reflection", V(0, 1.4f, 0)).gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = map;
            probe.size = V(7.6f, 3.1f, 7.8f);
            probe.blendDistance = .5f;
            probe.boxProjection = true;
            probe.intensity = 1;
        }
    }
}
