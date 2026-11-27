using MedSimAI.Surgery.Shared;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using static MedSimAI.Surgery.Environment.Editor.RoomGeometry;

namespace MedSimAI.Surgery.Environment.Editor
{
    internal sealed partial class OperatingRoomBuilder
    {
        private void BuildArchitecture(Transform room)
        {
            var building = Node(room, "Architecture");
            var floor = Node(building, "Walkable Floor");
            Bounds(floor, "Room Floor Collider", V(0, -.08f, 0), V(7.6f, .16f, 7.8f));
            Bounds(floor, "Vestibule Floor Collider", V(-1.92f, -.08f, -4.72f), V(2.46f, .16f, 1.64f));
            var vinyl = Node(floor, "Welded Vinyl");
            art.Plate(vinyl, M("Floor"), V(0, 0, 0), 7.6f, 7.8f, V(-90, 0, 0), new Vector2(8, 8));
            art.Plate(vinyl, M("Field"), V(0, .001f, -.08f), 3.88f, 4.64f, V(-90, 0, 0), new Vector2(4, 5));
            art.Plate(vinyl, M("Floor"), V(-1.92f, 0, -4.70f), 2.46f, 1.62f, V(-90, 0, 0), new Vector2(3, 2));
            art.NoShadows(vinyl);

            var lines = Node(floor, "Vinyl Welds and Field Corners");
            for (int i = -3; i <= 3; i++)
            {
                float z = i * 1.10f;
                float width = Mathf.Abs(z) < 2.22f ? 1.86f : 7.60f;
                if (width > 2)
                    art.Plate(lines, M("FloorWeld"), V(0, .0015f, z), width, .006f, V(-90, 0, 0), Vector2.one);
                else
                {
                    art.Plate(lines, M("FloorWeld"), V(-2.87f, .0015f, z), width, .006f, V(-90, 0, 0), Vector2.one);
                    art.Plate(lines, M("FloorWeld"), V(2.87f, .0015f, z), width, .006f, V(-90, 0, 0), Vector2.one);
                }
            }

            foreach (int x in new[] { -1, 1 })
            foreach (int z in new[] { -1, 1 })
            {
                art.Plate(lines, M("Lane"), V(x * 1.79f, .002f, z * 2.23f - .08f), .28f, .024f,
                    V(-90, 0, 0), Vector2.one);
                art.Plate(lines, M("Lane"), V(x * 1.92f, .002f, z * 2.09f - .08f), .024f, .28f,
                    V(-90, 0, 0), Vector2.one);
            }
            art.NoShadows(lines);

            Wall(building, "Headwall", V(0, 0, 3.9f), 7.60f, 180, true);
            Wall(building, "West Wall", V(-3.80f, 0, 0), 7.80f, 90);
            Wall(building, "East Wall", V(3.80f, 0, 0), 7.80f, -90, true);
            Wall(building, "South Wall Left", V(-3.315f, 0, -3.90f), .97f, 0);
            Wall(building, "South Wall Right", V(1.355f, 0, -3.90f), 4.89f, 0);

            var vestibule = Node(building, "Entry Vestibule");
            Wall(vestibule, "V Left", V(-3.15f, 0, -4.72f), 1.64f, 90);
            Wall(vestibule, "V Right", V(-.69f, 0, -4.72f), 1.64f, -90);
            Wall(vestibule, "V End", V(-1.92f, 0, -5.54f), 2.46f, 0);
            art.Box(vestibule, M("Ceiling"), V(-1.92f, 3.12f, -4.72f), V(2.46f, .1f, 1.64f));
            art.Box(vestibule, M("CeilingDiffuser"), V(-1.92f, 3.05f, -4.72f), V(.54f, .04f, .88f), .012f);
            MakeLight(vestibule, "Vestibule fill", V(-1.92f, 2.76f, -4.72f), V(-1.92f, 0, -4.72f),
                LightType.Point, .65f, 3.4f, false);

            BuildEntry(building);
            BuildCeiling(building);
            BuildCasework(room);
            BuildServices(room);
        }

        private void Wall(Transform building, string name, Vector3 position, float width, float yaw, bool accent = false)
        {
            var wall = Node(building, name, position, V(0, yaw, 0));
            const float height = 3.1f;
            art.Box(wall, M("WallSeam"), V(0, height / 2, -.07f), V(width, height, .14f));
            int count = Mathf.Max(1, Mathf.RoundToInt(width / 1.19f));
            float segment = width / count;
            for (int i = 0; i < count; i++)
            {
                float x = -width / 2 + segment * (i + .5f);
                art.Box(wall, M(accent ? "WallBlue" : "Wall"), V(x, .625f, .008f),
                    V(segment - .006f, 1.16f, .015f), .003f);
                art.Box(wall, M("WallPale"), V(x, 2.12f, .008f),
                    V(segment - .006f, 1.80f, .015f), .003f);
                art.Box(wall, M("Ceiling"), V(x, 3.025f, .008f), V(segment - .006f, .15f, .015f), .002f);
            }
            art.Box(wall, M("Cove"), V(0, .045f, .045f), V(width, .09f, .09f), .031f);
            art.Box(wall, M("WallSeam"), V(0, 1.19f, .027f), V(width, .080f, .050f), .021f);
            art.Box(wall, M("ShellSide"), V(0, 1.203f, .054f), V(width, .029f, .021f), .007f);
            Bounds(wall, "Solid Wall", V(0, height / 2, -.046f), V(width, height, .154f));
        }

        private void BuildEntry(Transform building)
        {
            var entry = Node(building, "Sliding Operating Room Entrance", V(-1.92f, 0, -3.90f));
            art.Box(entry, M("WallPale"), V(0, 2.73f, -.06f), V(1.78f, .74f, .145f), .004f);
            art.Box(entry, M("PaintedMetal"), V(0, 2.285f, .063f), V(2.15f, .20f, .15f), .029f);
            art.Box(entry, M("Steel"), V(0, 2.195f, .143f), V(2.08f, .033f, .035f), .009f);
            art.Box(entry, M("Steel"), V(0, .009f, .041f), V(1.78f, .012f, .10f), .003f);
            for (int side = -1; side <= 1; side += 2)
            {
                art.Box(entry, M("Steel"), V(.903f * side, 1.09f, .038f), V(.067f, 2.18f, .135f), .013f);
                var leaf = Node(entry, side < 0 ? "DoorLeafLeft" : "DoorLeafRight", V(side * .446f, 0, .05f));
                var doorArt = Node(leaf, side < 0 ? "Left Sliding Panel" : "Right Sliding Panel");
                art.Box(doorArt, M("Door"), V(0, .6f, 0), V(.881f, 1.18f, .067f), .010f);
                art.Box(doorArt, M("Door"), V(0, 2.016f, 0), V(.881f, .324f, .067f), .010f);
                foreach (int edge in new[] { -1, 1 })
                    art.Box(doorArt, M("Door"), V(edge * .288f, 1.51f, 0),
                        V(.306f, .65f, .067f), .006f);

                var vision = Node(leaf, side < 0 ? "Left Vision Glazing" : "Right Vision Glazing");
                art.Box(vision, M("Glass"), V(0, 1.51f, 0), V(.272f, .644f, .011f), .005f);
                art.NoShadows(vision);
                foreach (int face in new[] { -1, 1 })
                {
                    art.Box(doorArt, M("Gasket"), V(0, 1.836f, face * .039f), V(.311f, .027f, .015f), .004f);
                    art.Box(doorArt, M("Gasket"), V(0, 1.185f, face * .039f), V(.311f, .027f, .015f), .004f);
                    foreach (int edge in new[] { -1, 1 })
                        art.Box(doorArt, M("Gasket"), V(edge * .148f, 1.51f, face * .039f),
                            V(.023f, .641f, .015f), .005f);
                    art.Box(doorArt, M("Steel"), V(0, .199f, face * .036f), V(.86f, .261f, .015f), .006f);
                    art.Box(doorArt, M("ScreenFrame"), V(-side * .327f, 1.03f, face * .039f),
                        V(.047f, .187f, .018f), .008f);
                }

                // The moving obstacle carves the pre-baked open aperture while the leaf is closed.
                var obstacle = leaf.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = V(0, 1.09f, 0);
                obstacle.size = V(.88f, 2.18f, .16f);
                obstacle.carving = true;
                leaf.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
                Bounds(leaf, "Door Panel Collider", V(0, 1.09f, 0), V(.88f, 2.18f, .077f));
            }

            art.Box(entry, M("ScreenFrame"), V(1.09f, 1.28f, .082f), V(.115f, .19f, .071f), .014f);
            art.Box(entry, M("StatusGreen"), V(1.09f, 1.315f, .121f), V(.060f, .024f, .01f), .004f);
            art.Box(entry, M("StatusAmber"), V(.885f, 2.254f, .151f), V(.105f, .031f, .013f), .004f);
            var sign = Node(entry, "Operating Room Sign");
            art.Box(sign, M("ScreenFrame"), V(0, 2.58f, .035f), V(1.62f, .406f, .035f), .009f);
            art.Plate(sign, M("RoomSign"), V(0, 2.58f, .054f), 1.61f, .402f, Vector3.zero, Vector2.one);
        }

        private void BuildCeiling(Transform building)
        {
            var ceiling = Node(building, "Flush Medical Ceiling");
            art.Box(ceiling, M("WallSeam"), V(0, 3.169f, 0), V(7.6f, .10f, 7.8f));
            const int columns = 8, rows = 8;
            float w = 7.60f / columns, d = 7.80f / rows;
            for (int x = 0; x < columns; x++)
            for (int z = 0; z < rows; z++)
                art.Box(ceiling, M("Ceiling"), V((x - 3.5f) * w, 3.131f, (z - 3.5f) * d),
                    V(w - .007f, .064f, d - .007f), .002f);

            var diffusers = Node(ceiling, "Lighting and Air Diffusers");
            var lenses = Node(ceiling, "Recessed LED Panels");
            foreach (float x in new[] { -2.43f, 2.43f })
            foreach (float z in new[] { -2.41f, 0, 2.41f })
            {
                art.Box(diffusers, M("PaintedMetal"), V(x, 3.085f, z), V(.69f, .04f, 1.39f), .014f);
                art.Box(lenses, M("CeilingDiffuser"), V(x, 3.061f, z), V(.62f, .022f, 1.31f), .010f);
            }
            art.NoShadows(lenses);
            foreach (float z in new[] { -1.30f, .21f, 1.73f })
            {
                art.Box(diffusers, M("ShellSide"), V(0, 3.078f, z), V(1.45f, .047f, .46f), .012f);
                for (int i = -8; i <= 8; i++)
                    art.Box(diffusers, M("WallSeam"), V(i * .077f, 3.052f, z), V(.025f, .005f, .37f));
            }
            foreach (float x in new[] { -3.0f, 3.0f })
            foreach (float z in new[] { -3.43f, 3.43f })
                art.Disc(diffusers, M("Shell"), V(x, 3.07f, z), Vector3.up, .054f, .054f, 16);
            Bounds(ceiling, "Ceiling Collision", V(0, 3.16f, 0), V(7.6f, .12f, 7.8f));
        }

        private void BuildCasework(Transform room)
        {
            var group = Node(room, "Casework");
            var cabinet = Node(group, "Glazed Sterile Supply Cabinets", V(-2.23f, 0, 3.50f));
            art.Box(cabinet, M("WallBlue"), V(0, 1.16f, .23f), V(2.48f, 2.16f, .09f), .021f);
            art.Box(cabinet, M("Gasket"), V(0, .056f, .015f), V(2.44f, .112f, .49f), .014f);
            art.Box(cabinet, M("Steel"), V(0, 2.24f, 0), V(2.48f, .060f, .59f), .013f);
            var glass = Node(cabinet, "Cabinet Glass");
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * .81f;
                art.Box(cabinet, M("Shell"), V(x, .393f, -.003f), V(.795f, .566f, .55f), .019f);
                art.Box(cabinet, M("ShellSide"), V(x, .40f, -.289f), V(.753f, .51f, .036f), .011f);
                art.Box(cabinet, M("Steel"), V(x, .565f, -.317f), V(.203f, .024f, .048f), .010f);
                for (int j = 0; j < 4; j++)
                {
                    float y = .72f + j * .49f;
                    art.Box(cabinet, M("Shell"), V(x, y, -.02f), V(.81f, .043f, .51f), .009f);
                    art.Box(cabinet, M("Steel"), V(x, y, -.306f), V(.79f, .029f, .023f), .003f);
                }
                art.Box(cabinet, M("Steel"), V(x - .397f, 1.46f, -.292f), V(.035f, 1.50f, .048f), .009f);
                art.Box(cabinet, M("Steel"), V(x + .397f, 1.46f, -.292f), V(.035f, 1.50f, .048f), .009f);
                art.Box(cabinet, M("Steel"), V(x + .325f, 1.18f, -.330f), V(.024f, .244f, .051f), .009f);
                art.Plate(glass, M("Glass"), V(x, 1.46f, -.316f), .768f, 1.48f, V(0, 180, 0), Vector2.one);
                for (int j = 0; j < 3; j++)
                {
                    float y = .80f + j * .49f;
                    for (int k = 0; k < 3; k++)
                        SupplyBox(cabinet, V(x + (k - 1) * .226f, y, -.06f + ((i + j + k) % 2) * .055f),
                            V(.207f, .119f + .043f * ((i + j + k) % 3), .288f),
                            (i + k + j) % 3 == 0 ? "Teal" : "WallPale", (i + j + k) % 2 == 0 ? "Blue" : "Steel");
                }
            }
            art.NoShadows(glass);
            Bounds(cabinet, "Supply Bank Collider", V(0, 1.13f, 0), V(2.50f, 2.26f, .60f));
            art.Plate(cabinet, M("SupplySign"), V(0, 2.52f, .33f), .47f, .235f, V(0, 180, 0), Vector2.one);

            var drawers = Node(group, "Clean Core Counter", V(-3.43f, 0, 1.94f), V(0, 90, 0));
            art.Box(drawers, M("WallBlue"), V(0, .50f, 0), V(1.56f, 1.0f, .61f), .044f);
            art.Box(drawers, M("Steel"), V(0, 1.03f, .022f), V(1.63f, .077f, .67f), .035f);
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 2; j++)
            {
                float x = (i - 1) * .49f, y = .29f + j * .353f;
                art.Box(drawers, M("Shell"), V(x, y, .323f), V(.454f, .318f, .076f), .016f);
                art.Box(drawers, M("Steel"), V(x, y + .072f, .372f), V(.151f, .027f, .042f), .011f);
            }
            for (int i = -1; i <= 1; i++)
                SupplyBox(drawers, V(i * .37f, 1.145f, .07f), V(.28f, .15f, .40f), "Teal", "WallPale");
            Bounds(drawers, "Counter Collider", V(0, .54f, 0), V(1.64f, 1.09f, .70f));
        }

        private void SupplyBox(Transform group, Vector3 center, Vector3 size, string casing, string lid)
        {
            art.Box(group, M(casing), center, size, .018f);
            art.Box(group, M(lid), center + V(0, size.y * .45f, 0),
                V(size.x * 1.02f, .026f, size.z * 1.02f), .008f);
            art.Box(group, M("Shell"), center + V(0, -.011f, -size.z * .504f),
                V(size.x * .59f, .031f, .005f), .002f);
        }

        private void BuildServices(Transform room)
        {
            var services = Node(room, "Building Services");
            var utilities = Node(services, "Medical Gas and Electrical Headwall", V(.62f, 1.42f, 3.83f));
            art.Box(utilities, M("Steel"), V(0, 0, 0), V(2.54f, .42f, .085f), .014f);
            art.Box(utilities, M("Shell"), V(0, 0, -.046f), V(2.48f, .365f, .040f), .013f);
            art.Plate(utilities, M("UtilitySign"), V(-.92f, .01f, -.069f), .458f, .229f, V(0, 180, 0), Vector2.one);
            foreach (var outlet in new[] { (-.48f, "Teal"), (-.19f, "Yellow"), (.10f, "ShellSide") })
            {
                art.Disc(utilities, M("Steel"), V(outlet.Item1, 0, -.075f), Vector3.forward, .084f, .025f);
                art.Disc(utilities, M(outlet.Item2), V(outlet.Item1, 0, -.091f), Vector3.forward, .067f, .019f);
                art.Disc(utilities, M("Gasket"), V(outlet.Item1, 0, -.104f), Vector3.forward, .030f, .012f);
            }
            foreach (float x in new[] { .43f, .71f, 1.02f })
            {
                art.Box(utilities, M(x > .9f ? "Red" : "PaintedMetal"), V(x, 0, -.071f),
                    V(.195f, .139f, .029f), .012f);
                foreach (int j in new[] { -1, 1 })
                {
                    art.Box(utilities, M("Shell"), V(x + j * .047f, 0, -.090f), V(.066f, .09f, .015f), .005f);
                    art.Box(utilities, M("Gasket"), V(x + j * .047f, .013f, -.101f), V(.025f, .014f, .009f), .002f);
                    art.Box(utilities, M("Gasket"), V(x + j * .047f, -.014f, -.101f), V(.025f, .014f, .009f), .002f);
                }
            }

            var panel = Node(services, "Wall Status Display", V(3.72f, 1.95f, 1.61f), V(0, -90, 0));
            Screen(panel, Vector3.zero, 1.36f, .85f, "WallStatus");
            Anchor(panel, AnchorIds.OverheadMonitor, V(0, 0, .056f));
            var returns = Node(services, "Low Return Grilles");
            foreach (float x in new[] { -.63f, 2.75f })
            {
                art.Box(returns, M("ShellSide"), V(x, .33f, 3.863f), V(.85f, .39f, .051f), .018f);
                for (int i = -4; i <= 4; i++)
                    art.Box(returns, M("Gasket"), V(x, .33f + i * .034f, 3.831f), V(.735f, .012f, .021f), .003f);
            }
        }

        private void BuildLighting(Transform room)
        {
            var lights = Node(room, "Operating Field and Room Lighting");
            var installation = Node(lights, "Dual Articulated Suspension", V(.12f, 3.05f, .25f));
            art.Disc(installation, M("Shell"), V(0, .016f, 0), Vector3.up, .227f, .088f);
            art.Disc(installation, M("Steel"), V(0, -.077f, 0), Vector3.up, .112f, .174f);
            for (int side = -1; side <= 1; side += 2)
            {
                var arm = Node(installation, side < 0 ? "Primary Light Arm" : "Secondary Light Arm",
                    V(side * .073f, -.17f, side * .08f));
                Vector3 elbow = V(side * .95f, -.018f, side * .19f);
                Vector3 end = V(side * .71f, -.157f, side * .86f);
                art.Rod(arm, M("Shell"), Vector3.zero, elbow, .058f, 20);
                art.Disc(arm, M("ShellSide"), elbow, Vector3.up, .105f, .114f);
                art.Rod(arm, M("Shell"), elbow, end, .051f, 20);
                art.Disc(arm, M("Steel"), end, Vector3.up, .071f, .12f);
                Vector3 head = end + V(-side * .106f, -.245f, side * .140f);
                art.Rod(arm, M("PaintedMetal"), end, head + V(0, .083f, 0), .035f);
                LightHead(arm, side < 0 ? "Primary Surgical Head" : "Secondary Surgical Head",
                    head, side < 0 ? .365f : .315f, V(side * 7, 0, side * -11));

                Vector3 emitter = arm.TransformPoint(head + V(0, -.095f, 0));
                MakeLight(lights, side < 0 ? "Primary surgical beam" : "Secondary surgical beam", emitter,
                    V(-.08f, .89f, -.32f), LightType.Spot, side < 0 ? 4.6f : 2.35f, 4.15f, side < 0);
            }

            foreach (var p in new[] { V(-2.15f, 2.88f, -2.05f), V(2.15f, 2.88f, -2.05f),
                V(-2.18f, 2.88f, 2.03f), V(2.18f, 2.88f, 2.03f) })
            {
                var fill = MakeLight(lights, "LED ambient fill", p, p + Vector3.down,
                    LightType.Spot, 3.8f, 5.05f, false);
                fill.color = Rgb("#f1f8fc");
                fill.spotAngle = 143;
                fill.innerSpotAngle = 105;
            }
        }

        private void LightHead(Transform arm, string name, Vector3 position, float radius, Vector3 euler)
        {
            var head = Node(arm, name, position, euler);
            art.Lathe(head, M("Shell"), Vector3.zero, Vector3.up, 48,
                new Vector2(0, -.055f), new Vector2(radius * .86f, -.055f), new Vector2(radius, -.031f),
                new Vector2(radius, .012f), new Vector2(radius * .87f, .061f), new Vector2(radius * .49f, .079f),
                new Vector2(0, .088f));
            art.Disc(head, M("ShellSide"), V(0, -.058f, 0), Vector3.up, radius * .93f, .016f, 48);
            art.Disc(head, M("BrightSteel"), V(0, -.102f, 0), Vector3.up, .031f, .10f, 20);
            art.Disc(head, M("Pad"), V(0, -.158f, 0), Vector3.up, .027f, .116f, 20);
            var lenses = Node(head, name + " Optical Modules");
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector3 offset = V(Mathf.Cos(angle) * radius * .61f, -.069f,
                    Mathf.Sin(angle) * radius * .61f);
                art.Disc(head, M("Steel"), offset, Vector3.up, .058f, .021f, 24);
                art.Disc(lenses, M("LightLens"), offset + V(0, -.012f, 0), Vector3.up, .047f, .018f, 24);
            }
            art.NoShadows(lenses);
        }

        private static Light MakeLight(Transform parent, string name, Vector3 position, Vector3 target,
            LightType type, float intensity, float range, bool shadows)
        {
            var light = Facing(parent, name, position, target).gameObject.AddComponent<Light>();
            light.type = type;
            light.color = type == LightType.Spot ? Rgb("#fff5e5") : Rgb("#ddebfa");
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .69f;
            light.shadowBias = .06f;
            light.shadowNormalBias = .21f;
            if (type != LightType.Spot)
                return light;

            light.spotAngle = 61;
            light.innerSpotAngle = 39;
            return light;
        }
    }
}
