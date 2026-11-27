using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static MedSimAI.Surgery.Environment.Editor.RoomGeometry;

namespace MedSimAI.Surgery.Environment.Editor
{
    internal sealed partial class OperatingRoomBuilder
    {
        private void BuildRobotPrefab()
        {
            var robot = new GameObject("RobotBase").transform;
            var cart = Node(robot, "Braked Mobile Cart");
            art.Box(cart, M("Rubber"), V(0, .147f, 0), V(.86f, .136f, .90f), .065f);
            art.Box(cart, M("ShellSide"), V(0, .231f, 0), V(.82f, .104f, .85f), .049f);
            art.Box(cart, M("Shell"), V(.033f, .358f, .031f), V(.699f, .211f, .701f), .090f);
            art.Box(cart, M("ScreenFrame"), V(.021f, .267f, -.425f), V(.51f, .090f, .027f), .013f);
            foreach (int side in new[] { -1, 1 })
            foreach (int end in new[] { -1, 1 })
                Caster(cart, V(side * .31f, 0, end * .337f), .078f);
            art.Box(cart, M("Rubber"), V(-.188f, .121f, -.463f), V(.144f, .057f, .129f),
                .017f, V(12, 0, 0));
            art.Box(cart, M("PaintedMetal"), V(.020f, .145f, -.476f), V(.116f, .033f, .113f),
                .012f, V(12, 0, 0));
            Bounds(cart, "Robot Cart Collider", V(0, .222f, 0), V(.86f, .444f, .91f));

            var column = Node(robot, "Drive Pillar");
            art.Box(column, M("PaintedMetal"), V(.041f, .560f, .04f), V(.526f, .36f, .491f), .079f);
            art.Box(column, M("Shell"), V(.035f, .755f, .048f), V(.459f, .373f, .429f), .079f);
            art.Box(column, M("Teal"), V(-.198f, .736f, .052f), V(.030f, .25f, .279f), .014f);
            art.Box(column, M("Steel"), V(.037f, .934f, .047f), V(.421f, .093f, .395f), .037f);
            art.Disc(column, M("PaintedMetal"), V(.043f, .975f, .046f), Vector3.up, .158f, .095f);
            art.Disc(column, M("Rubber"), V(.043f, 1.018f, .046f), Vector3.up, .136f, .043f);
            Bounds(column, "Drive Pillar Collider", V(.041f, .688f, .048f), V(.50f, .62f, .47f));

            var detail = Node(cart, "Wheel and Drive Service Details");
            foreach (int side in new[] { -1, 1 })
            {
                art.Box(detail, M("Teal"), V(.404f * side, .226f, 0), V(.022f, .033f, .56f), .010f);
                for (int i = 0; i < 9; i++)
                    art.Box(detail, M("WallSeam"), V(.069f + .024f * i, .526f, -.201f),
                        V(.014f, .136f, .007f), .003f);
            }
            art.Hose(detail, M("Rubber"), .012f, V(.217f, .806f, .202f), V(.312f, .58f, .204f),
                V(.302f, .376f, .222f), V(.269f, .211f, .168f));

            var display = Node(robot, "Control Display Support");
            art.Rod(display, M("PaintedMetal"), V(.28f, .385f, -.227f), V(.35f, .936f, -.277f), .043f);
            art.Rod(display, M("Steel"), V(.35f, .908f, -.277f), V(.409f, 1.238f, -.303f), .029f);
            var screen = Node(display, "Robot Control Display", V(.39f, 1.275f, -.368f), V(-13, -142, 0));
            Screen(screen, Vector3.zero, .48f, .322f, "Robot");
            art.Box(display, M("Yellow"), V(.308f, .872f, -.302f), V(.115f, .067f, .092f), .021f);
            art.Disc(display, M("Red"), V(.308f, .917f, -.302f), Vector3.up, .031f, .045f);
            Bounds(screen, "Robot Display Collider", Vector3.zero, V(.48f, .32f, .10f));

            var yaw = Node(robot, "J1_BaseYaw", V(.043f, 1.034f, .046f));
            art.Disc(yaw, M("Steel"), V(0, .017f, 0), Vector3.up, .132f, .080f, 32);
            art.Disc(yaw, M("Teal"), V(0, .056f, 0), Vector3.up, .134f, .034f, 32);
            art.Box(yaw, M("Shell"), V(-.065f, .156f, 0), V(.285f, .233f, .259f), .066f);
            Bounds(yaw, "Yaw Housing Collider", V(-.058f, .149f, 0), V(.277f, .24f, .24f));

            var shoulder = Node(yaw, "J2_ShoulderPitch", V(-.101f, .206f, 0));
            var elbowOffset = V(-.331f, .475f, .034f);
            Joint(shoulder, .122f, .188f, Vector3.forward);
            ArmLink(shoulder, elbowOffset, .184f, .157f);
            var shoulderDetail = Node(shoulder, "Shoulder Fasteners");
            Fasteners(shoulderDetail, .122f, -.109f);
            art.Hose(shoulder, M("Rubber"), .015f, V(-.062f, .072f, -.09f), V(-.156f, .173f, -.132f),
                V(-.253f, .343f, -.099f), elbowOffset + V(.005f, -.028f, -.098f));

            var elbow = Node(shoulder, "J3_ElbowPitch", elbowOffset);
            var rollOffset = V(-.399f, -.099f, .022f);
            Joint(elbow, .105f, .164f, Vector3.forward);
            ArmLink(elbow, rollOffset, .144f, .133f);
            var elbowDetail = Node(elbow, "Elbow Fasteners");
            Fasteners(elbowDetail, .105f, -.089f);

            var roll = Node(elbow, "J4_ForearmRoll", rollOffset);
            Joint(roll, .078f, .110f, Vector3.right);
            var pitchOffset = V(-.198f, -.032f, .02f);
            ArmLink(roll, pitchOffset, .121f, .096f);

            var wrist = Node(roll, "J5_WristPitch", pitchOffset);
            Joint(wrist, .064f, .112f, Vector3.forward);
            var flangeOffset = V(-.117f, -.118f, 0);
            ArmLink(wrist, flangeOffset, .084f, .074f);

            var flange = Facing(wrist, "J6_ToolRoll", flangeOffset, flangeOffset + V(-.74f, -.64f, .18f));
            art.Disc(flange, M("ScreenFrame"), V(0, 0, .017f), Vector3.forward, .066f, .078f, 24);
            art.Disc(flange, M("Steel"), V(0, 0, .058f), Vector3.forward, .062f, .021f, 24);
            art.Disc(flange, M("Teal"), V(0, 0, .073f), Vector3.forward, .046f, .012f, 24);
            art.Disc(flange, M("Gasket"), V(0, 0, .081f), Vector3.forward, .025f, .006f, 20);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI / 2;
                art.Disc(flange, M("BrightSteel"), V(.049f * Mathf.Cos(angle), .049f * Mathf.Sin(angle), .070f),
                    Vector3.forward, .0065f, .007f, 10);
            }
            Bounds(flange, "Tool Flange Collider", V(0, 0, .032f), V(.131f, .131f, .106f));
            Node(flange, "RobotToolMount", V(0, 0, .085f));

            art.Save(Root + "/Models", false);
            SetRobotLod(robot, detail, shoulderDetail, elbowDetail);
            PrefabUtility.SaveAsPrefabAsset(robot.gameObject, RobotPath);
            Object.DestroyImmediate(robot.gameObject);
        }

        private void ArmLink(Transform joint, Vector3 next, float width, float depth)
        {
            float length = next.magnitude;
            Vector3 center = next * .5f;
            Vector3 euler = Quaternion.FromToRotation(Vector3.up, next).eulerAngles;
            art.Box(joint, M("PaintedMetal"), center, V(width, length * .93f, depth), width * .27f, euler);
            art.Box(joint, M("Shell"), center + V(0, 0, -depth * .19f),
                V(width * .91f, length * .77f, depth * .76f), width * .30f, euler);
            art.Box(joint, M("Teal"), center + V(0, 0, -depth * .58f),
                V(width * .32f, length * .53f, .015f), .007f, euler);
            var collision = Bounds(joint, "Link Collider", center, V(width, length * .84f, depth));
            collision.transform.localEulerAngles = euler;
        }

        private void Joint(Transform joint, float radius, float depth, Vector3 axis)
        {
            art.Disc(joint, M("PaintedMetal"), Vector3.zero, axis, radius, depth, 32);
            art.Disc(joint, M("Rubber"), Vector3.zero, axis, radius * 1.01f, depth * .18f, 32);
            foreach (int side in new[] { -1, 1 })
            {
                art.Disc(joint, M("Steel"), axis * (side * depth * .5f), axis, radius * .78f, .023f, 28);
                art.Disc(joint, M("ScreenFrame"), axis * (side * (depth * .5f + .014f)), axis,
                    radius * .51f, .013f, 24);
            }

            var collision = Node(joint, "Joint Housing Collider").gameObject.AddComponent<CapsuleCollider>();
            collision.direction = axis == Vector3.right ? 0 : 2;
            collision.radius = radius;
            collision.height = Mathf.Max(depth, radius * 2);
        }

        private void Fasteners(Transform details, float radius, float z)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3;
                art.Disc(details, M("Gasket"), V(Mathf.Cos(a) * radius * .65f, Mathf.Sin(a) * radius * .65f, z),
                    Vector3.forward, .009f, .010f, 10);
            }
        }

        private static void SetRobotLod(Transform robot, params Transform[] detailGroups)
        {
            var all = robot.GetComponentsInChildren<Renderer>();
            var reduced = new List<Renderer>(all);
            foreach (var group in detailGroups)
                reduced.Remove(group.GetComponent<Renderer>());

            var lod = robot.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.33f, all), new LOD(0, reduced.ToArray()) });
            lod.RecalculateBounds();
        }
    }
}
