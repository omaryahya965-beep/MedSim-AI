using System.Linq;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MedSimAI.Surgery.Environment.Tests
{
    public sealed class OperatingRoomTests
    {
        private const string Room = "Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom.prefab";
        private const string Demo = "Assets/MedSimAI/Surgery/Scenes/OperatingRoom_Environment.unity";
        private GameObject instance;
        private Scene sandbox;
        private NavMeshDataInstance navigation;

        [SetUp]
        public void SetUp()
        {
            sandbox = EditorSceneManager.NewPreviewScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Room);
            Assert.That(prefab, Is.Not.Null, "Rebuild the operating room if the prefab is absent.");
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sandbox);
            // Leave the active project's existing scene out of the physics and navigation queries.
            instance.transform.position = new Vector3(100, 0, 100);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (navigation.valid)
                navigation.Remove();
            if (sandbox.IsValid())
                EditorSceneManager.ClosePreviewScene(sandbox);
        }

        [Test]
        public void PrefabUsesMetersAndEveryProvisionalAnchorIsUnique()
        {
            Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));
            foreach (string name in new[]
            {
                "PlayerSpawn", "PatientRoot", "SurgicalSite", "SurgeonPosition",
                "AssistantPosition", "InstrumentTable", "RobotBase", "RobotToolMount",
                "MonitorPosition", "RoomExit"
            })
                Assert.That(Transforms.Count(item => item.name == name), Is.EqualTo(1), name);

            var floor = Find("Room Floor Collider").GetComponent<BoxCollider>();
            Assert.That(floor.size.x, Is.EqualTo(7.6f).Within(.002f));
            Assert.That(floor.size.z, Is.EqualTo(7.8f).Within(.002f));
            Assert.That(CollisionAt(Find("PatientRoot").position + Vector3.up * .1f, Vector3.down, .22f),
                Is.EqualTo("Deck Support Collider"));
            float supportTop = Find("Seat Section").GetComponentInChildren<BoxCollider>().bounds.max.y;
            Assert.That(Find("PatientRoot").position.y, Is.EqualTo(supportTop).Within(.002f));
            Assert.That(Find("SurgicalSite").position.z, Is.LessThan(Find("PatientRoot").position.z));
        }

        [Test]
        public void ModelMeshesAndMaterialsAreResolvable()
        {
            var models = instance.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(models.Length, Is.GreaterThan(35));
            foreach (var renderer in models)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh && mesh.vertexCount > 0, renderer.name);
                Assert.That(mesh.subMeshCount, Is.EqualTo(renderer.sharedMaterials.Length), renderer.name);
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.That(material && material.shader && material.shader.isSupported, renderer.name);
                    Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"), material.name);
                }
            }

            foreach (var transform in Transforms)
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),
                    Is.Zero, transform.name);

            var screenMesh = Find("WallStatus Screen Image").GetComponent<MeshFilter>().sharedMesh;
            Vector2[] uv = screenMesh.uv;
            Vector3[] vertices = screenMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                Assert.That(uv[i].x, Is.EqualTo(vertices[i].x > 0 ? 0 : 1),
                    "Screen lettering faces the viewer.");
            var fabricMesh = Find("Folded Surgical Linen").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(fabricMesh.normals.All(normal => normal.y > 0), "Linen has usable lighting normals.");

            foreach (string label in new[] { "Vitals", "Anesthesia", "Robot", "WallStatus", "RoomSign" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<Material>(
                    $"Assets/MedSimAI/Surgery/Environment/Materials/{label}.mat");
                Assert.That(asset.GetTexture("_BaseMap"), Is.Not.Null, label);
            }

            foreach (string surface in new[] { "Floor", "Pad", "Fabric", "Steel" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<Material>(
                    $"Assets/MedSimAI/Surgery/Environment/Materials/{surface}.mat");
                Assert.That(asset.GetTexture("_BaseMap"), Is.Not.Null, surface);
                Assert.That(asset.GetTexture("_BumpMap"), Is.Not.Null, surface);
            }
        }

        [Test]
        public void FloorSupportsPlayerAndDoorPanelsBlockOnlyWhenClosed()
        {
            Vector3 spawn = Find("PlayerSpawn").position;
            Assert.That(CollisionAt(spawn + Vector3.up * .8f, Vector3.down, 1.1f), Is.EqualTo("Room Floor Collider"));
            Assert.That(CollisionAt(RoomPoint(-1.72f, 1.45f, -3.28f), Vector3.back, 1f),
                Is.EqualTo("Door Panel Collider"));
            Assert.That(CollisionAt(RoomPoint(2.90f, 1.60f, -.25f), Vector3.right, 1.15f),
                Is.EqualTo("Solid Wall"));
            Assert.That(CollisionAt(RoomPoint(1.0f, .88f, -.52f), Vector3.left, 1.15f),
                Is.EqualTo("Deck Support Collider"));

            var left = Find("DoorLeafLeft");
            var right = Find("DoorLeafRight");
            left.localPosition += Vector3.left * .91f;
            right.localPosition += Vector3.right * .91f;
            Physics.SyncTransforms();
            Assert.That(CollisionAt(RoomPoint(-1.72f, 1.45f, -3.28f), Vector3.back, 1f), Is.Null);

            var probe = new GameObject("Desktop controller collision probe");
            SceneManager.MoveGameObjectToScene(probe, sandbox);
            var controller = probe.AddComponent<CharacterController>();
            controller.radius = .22f;
            controller.height = 1.7f;
            controller.center = new Vector3(0, .85f, 0);
            controller.stepOffset = .30f;
            probe.transform.position = RoomPoint(-1.72f, .035f, -3.28f);
            Physics.SyncTransforms();
            controller.Move(Vector3.back * .96f);
            Assert.That(instance.transform.InverseTransformPoint(controller.transform.position).z, Is.LessThan(-3.92f));
        }

        [Test]
        public void PrebakedNavigationConnectsSpawnAndBothStaffPositions()
        {
            var surface = instance.GetComponent<NavMeshSurface>();
            Assert.That(surface.navMeshData, Is.Not.Null);
            navigation = NavMesh.AddNavMeshData(surface.navMeshData, instance.transform.position,
                instance.transform.rotation);
            Assert.That(navigation.valid);
            Assert.That(NavMesh.SamplePosition(Find("PatientRoot").position, out _, .15f, NavMesh.AllAreas),
                Is.False, "The operating table is a navigation obstacle.");
            foreach (string destination in new[] { "SurgeonPosition", "AssistantPosition" })
            {
                Assert.That(NavMesh.SamplePosition(Find("PlayerSpawn").position, out var start, .27f, NavMesh.AllAreas));
                Assert.That(NavMesh.SamplePosition(Find(destination).position, out var end, .30f, NavMesh.AllAreas),
                    "No nearby agent navigation at " + destination);
                var route = new NavMeshPath();
                Assert.That(NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, route), destination);
                Assert.That(route.status, Is.EqualTo(NavMeshPathStatus.PathComplete), destination);
            }
        }

        [Test]
        public void RobotFlangeFollowsSixNestedPivots()
        {
            Transform root = Find("RobotBase");
            Assert.That(root.GetComponent<LODGroup>(), Is.Not.Null);
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root.gameObject);
            Assert.That(AssetDatabase.GetAssetPath(source), Does.EndWith("/OrthopedicAssistRobot.prefab"));
            string[] names = { "J1_BaseYaw", "J2_ShoulderPitch", "J3_ElbowPitch",
                "J4_ForearmRoll", "J5_WristPitch", "J6_ToolRoll", "RobotToolMount" };
            foreach (string name in names)
            {
                root = root.Find(name);
                Assert.That(root, Is.Not.Null, name);
            }

            Vector3 initial = root.position;
            Transform elbow = Find("J3_ElbowPitch");
            elbow.Rotate(0, 0, 15, Space.Self);
            Assert.That(Vector3.Distance(initial, root.position), Is.GreaterThan(.1f));
            Assert.That(elbow.GetComponentsInChildren<Collider>().Length, Is.GreaterThan(6));
        }

        [Test]
        public void DemoSceneIsPresentAndHasAReadableAsset()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(Demo), Is.Not.Null);
            var beams = instance.GetComponentsInChildren<Light>()
                .Where(light => light.name.EndsWith("surgical beam", System.StringComparison.Ordinal))
                .ToArray();
            Assert.That(beams.Length, Is.EqualTo(2));
            Assert.That(beams.All(light => light.type == LightType.Spot));
            Assert.That(beams.Count(light => light.shadows == LightShadows.Soft), Is.EqualTo(1));
            Assert.That(instance.GetComponentInChildren<ReflectionProbe>().customBakedTexture, Is.Not.Null);
            Assert.That(instance.GetComponentsInChildren<Camera>(), Is.Empty, "Prefab must accept the existing rig.");
        }

        private Transform[] Transforms => instance.GetComponentsInChildren<Transform>(true);
        private Transform Find(string name) => Transforms.Single(item => item.name == name);
        private Vector3 RoomPoint(float x, float y, float z) =>
            instance.transform.TransformPoint(new Vector3(x, y, z));

        private string CollisionAt(Vector3 origin, Vector3 direction, float distance) =>
            sandbox.GetPhysicsScene().Raycast(origin, direction, out var hit, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.collider.name : null;
    }
}
