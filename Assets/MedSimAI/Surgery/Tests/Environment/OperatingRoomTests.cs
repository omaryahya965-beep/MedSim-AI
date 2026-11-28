using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
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
        private const string Room = "Assets/MedSimAI/Surgery/Environment/Prefabs/OperatingRoom_Env.prefab";
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
        public void PrefabUsesMetersAndEveryContractAnchorIsUnique()
        {
            Assert.That(instance.transform.localScale, Is.EqualTo(Vector3.one));
            foreach (string name in new[]
            {
                AnchorIds.OperatingRoomRoot, AnchorIds.PlayerSpawn, AnchorIds.RoomEntry,
                AnchorIds.OperatingTable, AnchorIds.PatientRoot, AnchorIds.InstrumentTable,
                AnchorIds.ImplantTable, AnchorIds.RobotBase, AnchorIds.OverheadMonitor,
                AnchorIds.SurgeonObserve, AnchorIds.AssistantHome, AnchorIds.AssistantAtTable,
                AnchorIds.StudentOperatingPosition
            })
            {
                Assert.That(Transforms.Count(item => item.name == name), Is.EqualTo(1), name);
                Assert.That(Find(name).GetComponent<SurgeryAnchor>().anchorId, Is.EqualTo(name));
            }

            var anchors = instance.GetComponentsInChildren<SurgeryAnchor>();
            Assert.That(anchors.Length, Is.EqualTo(13));
            Assert.That(Find(AnchorIds.OperatingRoomRoot).position, Is.EqualTo(instance.transform.position));
            Assert.That(Find(AnchorIds.StudentOperatingPosition).position.x, Is.LessThan(instance.transform.position.x));
            Assert.That(Find(AnchorIds.SurgeonObserve).position.x, Is.GreaterThan(instance.transform.position.x));
            Assert.That(Find(AnchorIds.PatientRoot).forward, Is.EqualTo(Vector3.forward));

            var floor = Find("Room Floor Collider").GetComponent<BoxCollider>();
            Assert.That(floor.size.x, Is.EqualTo(7.6f).Within(.002f));
            Assert.That(floor.size.z, Is.EqualTo(7.8f).Within(.002f));
            Assert.That(CollisionAt(Find(AnchorIds.PatientRoot).position + Vector3.up * .1f, Vector3.down, .22f),
                Is.EqualTo("Deck Support Collider"));
            float supportTop = Find("Seat Section").GetComponentInChildren<BoxCollider>().bounds.max.y;
            Assert.That(Find(AnchorIds.PatientRoot).position.y, Is.EqualTo(supportTop).Within(.002f));
            Assert.That(Find(AnchorIds.OperatingTable).position, Is.EqualTo(Find(AnchorIds.PatientRoot).position));
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
                    Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"), material.name);
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
                Assert.That(asset.IsKeywordEnabled("_EMISSION"), Is.EqualTo(label != "RoomSign"), label);
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
            Vector3 spawn = Find(AnchorIds.PlayerSpawn).position;
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
            Assert.That(NavMesh.SamplePosition(Find(AnchorIds.PatientRoot).position, out _, .15f, NavMesh.AllAreas),
                Is.False, "The operating table is a navigation obstacle.");
            foreach (string destination in new[] { AnchorIds.SurgeonObserve, AnchorIds.AssistantHome,
                AnchorIds.AssistantAtTable, AnchorIds.StudentOperatingPosition })
            {
                Assert.That(NavMesh.SamplePosition(Find(AnchorIds.PlayerSpawn).position, out var start, .27f, NavMesh.AllAreas));
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
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/MedSimAI/Surgery/Scenes/Env_Test.unity"),
                Is.Not.Null);
            var beams = instance.GetComponentsInChildren<Light>()
                .Where(light => light.name.EndsWith("surgical beam", System.StringComparison.Ordinal))
                .ToArray();
            Assert.That(beams.Length, Is.EqualTo(2));
            Assert.That(beams.All(light => light.type == LightType.Spot));
            Assert.That(beams.Count(light => light.shadows == LightShadows.Soft), Is.EqualTo(1));
            Assert.That(instance.GetComponentInChildren<ReflectionProbe>().customBakedTexture, Is.Not.Null);
            Assert.That(instance.GetComponentsInChildren<Camera>(), Is.Empty, "Prefab must accept the existing rig.");
        }

        [Test]
        public void OnlyBoundPlayerPublishesOneRoomEntryPerSession()
        {
            var entry = Find(AnchorIds.RoomEntry);
            Assert.That(entry.GetComponent<BoxCollider>().isTrigger);
            Assert.That(entry.GetComponent<Rigidbody>().isKinematic);
            var publisher = entry.GetComponent<RoomEntryPublisher>();
            var player = new GameObject("Tracked Player");
            SceneManager.MoveGameObjectToScene(player, sandbox);
            var playerCollider = player.AddComponent<CapsuleCollider>();
            var nonPlayer = Find("Room Floor Collider").GetComponent<Collider>();
            var bus = new SurgeryEventBus();
            publisher.BindPlayer(player.transform);
            publisher.ObservePlayer(playerCollider, bus);
            Assert.That(bus.Log, Is.Empty);

            bus.BeginSession("first");
            publisher.ObservePlayer(nonPlayer, bus);
            Assert.That(bus.Log, Is.Empty);
            publisher.ObservePlayer(playerCollider, bus);
            publisher.ObservePlayer(playerCollider, bus);
            Assert.That(bus.Log.Count, Is.EqualTo(1));
            Assert.That(bus.Log[0].id, Is.EqualTo(SurgeryEventIds.PlayerEnteredRoom));
            Assert.That(bus.Log[0].source, Is.EqualTo(SurgeryEventSources.Environment));

            bus.BeginSession("next");
            publisher.ObservePlayer(playerCollider, bus);
            Assert.That(bus.Log.Count, Is.EqualTo(1));
            Assert.That(bus.Log[0].sessionId, Is.EqualTo("next"));
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
