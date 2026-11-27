using UnityEngine;
using static MedSimAI.Surgery.Environment.Editor.RoomGeometry;

namespace MedSimAI.Surgery.Environment.Editor
{
    internal sealed partial class OperatingRoomBuilder
    {
        private void BuildEquipment(Transform room)
        {
            var equipment = Node(room, "Clinical Equipment");
            OperatingTable(equipment);
            AnesthesiaCart(equipment);
            MonitorCart(equipment);
            InstrumentCart(equipment);
            MayoStand(equipment);
            EquipmentCart(equipment);
            InfusionStand(equipment, V(-1.94f, 0, 2.77f));
            Waste(equipment);
        }

        private void OperatingTable(Transform equipment)
        {
            var table = Node(equipment, "Orthopedic Operating Table");
            var baseGroup = Node(table, "Braked Mobile Base and Lift Column");
            art.Box(baseGroup, M("Rubber"), V(0, .085f, .13f), V(.645f, .139f, 1.30f), .066f);
            art.Box(baseGroup, M("ShellSide"), V(0, .162f, .13f), V(.613f, .097f, 1.204f), .047f);
            art.Box(baseGroup, M("Shell"), V(0, .229f, .13f), V(.484f, .132f, .875f), .06f);
            foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { -.33f, .56f })
            {
                art.Disc(baseGroup, M("Rubber"), V(.253f * side, .074f, z), Vector3.right, .074f, .081f, 20);
                art.Disc(baseGroup, M("Steel"), V(.296f * side, .074f, z), Vector3.right, .035f, .012f, 18);
            }

            art.Box(baseGroup, M("PaintedMetal"), V(0, .336f, .16f), V(.386f, .238f, .538f), .056f);
            art.Box(baseGroup, M("Steel"), V(0, .466f, .16f), V(.319f, .226f, .445f), .050f);
            art.Box(baseGroup, M("Gasket"), V(0, .565f, .16f), V(.312f, .041f, .424f), .018f);
            art.Box(baseGroup, M("Steel"), V(0, .634f, .16f), V(.260f, .173f, .366f), .043f);
            art.Disc(baseGroup, M("PaintedMetal"), V(0, .730f, .17f), Vector3.right, .126f, .325f, 32);
            art.Disc(baseGroup, M("Steel"), V(-.182f, .730f, .17f), Vector3.right, .078f, .046f);
            art.Disc(baseGroup, M("Steel"), V(.182f, .730f, .17f), Vector3.right, .078f, .046f);
            art.Box(baseGroup, M("Rubber"), V(.05f, .137f, -.51f), V(.13f, .045f, .129f), .016f, V(12, 0, 0));
            Bounds(baseGroup, "Table Base Collider", V(0, .415f, .13f), V(.64f, .83f, 1.30f));

            var top = Node(table, "Articulated Table Deck");
            foreach (var section in new[] { (Name: "Head", Z: .911f, Length: .269f, Width: .455f),
                (Name: "Back", Z: .504f, Length: .526f, Width: .510f),
                (Name: "Seat", Z: .014f, Length: .436f, Width: .510f),
                (Name: "Leg", Z: -.497f, Length: .572f, Width: .501f),
                (Name: "Foot", Z: -.916f, Length: .254f, Width: .470f) })
            {
                var pad = Node(top, section.Name + " Section", V(0, 0, section.Z));
                art.Box(pad, M("PaintedMetal"), V(0, .795f, 0), V(section.Width * .93f, .058f, section.Length), .020f);
                art.Box(pad, M("Rubber"), V(0, .831f, 0), V(section.Width - .007f, .024f, section.Length - .007f), .011f);
                art.Box(pad, M("Pad"), V(0, .876f, 0),
                    V(section.Width, .088f, section.Length - .011f), .035f);
                Bounds(pad, "Deck Support Collider", V(0, .851f, 0),
                    V(section.Width, .138f, section.Length));
            }

            var rails = Node(top, "Accessory Side Rails");
            foreach (int side in new[] { -1, 1 })
            {
                float x = .302f * side;
                art.Box(rails, M("BrightSteel"), V(x, .797f, .044f), V(.030f, .044f, 1.46f), .010f);
                foreach (float z in new[] { -.58f, -.19f, .27f, .68f })
                {
                    art.Box(rails, M("Steel"), V(.268f * side, .799f, z), V(.082f, .026f, .037f), .007f);
                    art.Disc(rails, M("Rubber"), V((.301f + .021f) * side, .796f, z),
                        Vector3.right, .011f, .010f, 12);
                }
            }

            var linen = Node(top, "Folded Surgical Linen");
            art.Cloth(linen, M("Drape"), V(0, .932f, .70f), .642f, .24f);
            art.Cloth(linen, M("Fabric"), V(.02f, .937f, .718f), .495f, .139f);
            art.NoShadows(linen);
        }

        private void AnesthesiaCart(Transform equipment)
        {
            var station = Node(equipment, "Anesthesia Workstation", V(.48f, 0, 2.73f), V(0, 180, 0));
            art.Box(station, M("Rubber"), V(0, .164f, 0), V(.75f, .104f, .61f), .043f);
            art.Box(station, M("ShellSide"), V(0, .571f, 0), V(.67f, .761f, .534f), .062f);
            art.Box(station, M("Shell"), V(0, .950f, .05f), V(.76f, .104f, .646f), .037f);
            art.Box(station, M("Steel"), V(0, 1.008f, .10f), V(.62f, .035f, .41f), .015f);
            for (int j = 0; j < 3; j++)
            {
                float y = .324f + j * .185f;
                art.Box(station, M("Shell"), V(-.056f, y, .28f), V(.47f, .155f, .064f), .018f);
                art.Box(station, M("PaintedMetal"), V(-.056f, y + .033f, .322f), V(.246f, .026f, .031f), .009f);
            }

            art.Box(station, M("ScreenFrame"), V(.263f, .641f, .289f), V(.102f, .524f, .045f), .012f);
            for (int i = 0; i < 7; i++)
                art.Box(station, M("WallBlue"), V(.26f, .437f + i * .047f, .317f), V(.066f, .010f, .008f), .003f);

            foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { -.22f, .22f })
                Caster(station, V(side * .286f, 0, z), .067f);

            art.Rod(station, M("PaintedMetal"), V(-.14f, .96f, -.185f), V(-.14f, 1.514f, -.185f), .043f);
            art.Rod(station, M("PaintedMetal"), V(-.14f, 1.495f, -.185f), V(-.14f, 1.495f, .004f), .038f);
            var screen = Node(station, "Ventilator Screen", V(-.133f, 1.497f, .027f), V(-8, 0, 0));
            Screen(screen, Vector3.zero, .465f, .30f, "Anesthesia");
            art.Box(station, M("ShellSide"), V(.23f, 1.118f, .105f), V(.242f, .167f, .218f), .034f);
            art.Disc(station, M("Blue"), V(.272f, 1.114f, .221f), Vector3.forward, .035f, .041f);
            art.Disc(station, M("StatusGreen"), V(.20f, 1.153f, .222f), Vector3.forward, .009f, .008f, 12);
            art.Disc(station, M("VisionGlass"), V(.40f, .75f, .066f), Vector3.up, .073f, .245f);
            art.Disc(station, M("Shell"), V(.40f, .89f, .066f), Vector3.up, .078f, .055f);
            art.Hose(station, M("Rubber"), .015f, V(.414f, .893f, .065f), V(.494f, .893f, .112f),
                V(.548f, .982f, .188f), V(.550f, 1.096f, .290f), V(.424f, 1.150f, .345f),
                V(.320f, 1.108f, .240f));
            art.Hose(station, M("Teal"), .015f, V(.385f, .195f, -.170f), V(.459f, .354f, -.241f),
                V(.398f, .597f, -.369f), V(.415f, .975f, -.559f), V(.533f, 1.138f, -.834f));
            Bounds(station, "Workstation Footprint", V(.044f, .50f, 0), V(.86f, 1.04f, .66f));
            Bounds(screen, "Anesthesia Screen Collider", Vector3.zero, V(.46f, .30f, .092f));
        }

        private void MonitorCart(Transform equipment)
        {
            var cart = Node(equipment, "Patient Monitor Cart", V(1.71f, 0, 2.34f), V(0, 217, 0));
            art.Box(cart, M("Rubber"), V(0, .133f, 0), V(.57f, .073f, .49f), .035f);
            art.Box(cart, M("Shell"), V(0, .169f, 0), V(.535f, .059f, .46f), .029f);
            foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { -.166f, .166f })
                Caster(cart, V(side * .212f, 0, z), .056f);
            art.Disc(cart, M("PaintedMetal"), V(0, .399f, 0), Vector3.up, .083f, .488f);
            art.Disc(cart, M("Steel"), V(0, .877f, 0), Vector3.up, .042f, .943f);
            art.Box(cart, M("ShellSide"), V(0, .928f, .092f), V(.343f, .078f, .29f), .024f);
            art.Box(cart, M("Shell"), V(0, 1.005f, .092f), V(.345f, .085f, .26f), .021f);
            art.Rod(cart, M("PaintedMetal"), V(0, 1.303f, 0), V(0, 1.462f, .104f), .039f);
            var screen = Node(cart, "Patient Monitor Display", V(0, 1.475f, .13f), V(-7, 0, 0));
            Screen(screen, Vector3.zero, .59f, .385f, "Vitals");
            Node(screen, "MonitorPosition", V(0, 0, .056f));
            art.Hose(cart, M("Rubber"), .012f, V(.19f, 1.385f, .07f), V(.28f, 1.127f, .022f),
                V(.17f, .858f, -.125f), V(.06f, .382f, -.144f), V(.01f, .219f, -.105f));
            Bounds(cart, "Mobile Monitor Footprint", V(0, .19f, 0), V(.58f, .38f, .49f));
            Bounds(screen, "Patient Monitor Screen Collider", Vector3.zero, V(.59f, .385f, .093f));
        }

        private void InstrumentCart(Transform equipment)
        {
            var cart = Node(equipment, "Sterile Instrument Back Table", V(-2.53f, 0, .12f), V(0, 91, 0));
            const float width = 1.16f, depth = .56f;
            OpenCart(cart, width, depth, .935f);
            Node(cart, "InstrumentTable", V(0, .985f, 0));
            var linen = Node(cart, "Sterile Table Cover");
            art.Cloth(linen, M("Fabric"), V(0, .982f, .018f), width + .077f, depth + .059f);
            foreach (float x in new[] { -.27f, .28f })
            {
                art.Box(cart, M("Steel"), V(x, 1.005f, -.034f), V(.39f, .031f, .295f), .013f);
                art.Box(cart, M("ShellSide"), V(x, 1.027f, -.034f), V(.343f, .013f, .253f), .008f);
                foreach (int side in new[] { -1, 1 })
                    art.Box(cart, M("Steel"), V(x + .180f * side, 1.040f, -.034f),
                        V(.023f, .033f, .282f), .008f);
            }
            foreach (float x in new[] { -.29f, .29f })
                SupplyBox(cart, V(x, .405f, .03f), V(.39f, .16f, .37f), "WallPale", "Teal");
        }

        private void MayoStand(Transform equipment)
        {
            var stand = Node(equipment, "Mayo Instrument Stand", V(-.86f, 0, -1.57f), V(0, -13, 0));
            art.Box(stand, M("Steel"), V(0, .106f, 0), V(.47f, .054f, .071f), .023f);
            foreach (int side in new[] { -1, 1 })
            {
                art.Box(stand, M("Steel"), V(.212f * side, .106f, .147f), V(.062f, .054f, .342f), .023f);
                Caster(stand, V(.212f * side, 0, .276f), .044f);
                Caster(stand, V(.212f * side, 0, -.011f), .044f);
            }
            art.Disc(stand, M("PaintedMetal"), V(0, .458f, -.013f), Vector3.up, .042f, .741f);
            art.Disc(stand, M("Steel"), V(0, .868f, -.013f), Vector3.up, .027f, .39f);
            art.Disc(stand, M("Rubber"), V(.04f, .796f, -.013f), Vector3.right, .034f, .033f, 16);
            art.Box(stand, M("Steel"), V(0, 1.068f, .128f), V(.503f, .038f, .395f), .015f);
            art.Box(stand, M("Fabric"), V(0, 1.089f, .128f), V(.454f, .008f, .342f), .003f);
            art.Box(stand, M("Steel"), V(0, 1.117f, .306f), V(.484f, .048f, .021f), .007f);
            Bounds(stand, "Mayo Stand Footprint", V(0, .54f, .125f), V(.49f, 1.12f, .423f));
        }

        private void EquipmentCart(Transform equipment)
        {
            var cart = Node(equipment, "Mobile Supply Cart", V(2.97f, 0, 1.13f), V(0, -90, 0));
            OpenCart(cart, .88f, .58f, .866f);
            art.Box(cart, M("Blue"), V(0, .585f, .015f), V(.769f, .426f, .492f), .030f);
            for (int row = 0; row < 3; row++)
            {
                float y = .438f + row * .136f;
                art.Box(cart, M("Shell"), V(0, y, .274f), V(.696f, .121f, .034f), .012f);
                art.Box(cart, M("Steel"), V(0, y + .016f, .302f), V(.238f, .023f, .038f), .009f);
            }
            SupplyBox(cart, V(-.17f, .973f, -.03f), V(.37f, .163f, .344f), "WallPale", "Teal");
            SupplyBox(cart, V(.20f, .939f, .03f), V(.29f, .109f, .221f), "Teal", "WallPale");
            art.Hose(cart, M("Steel"), .018f, V(.446f, .883f, -.24f), V(.52f, .938f, -.224f),
                V(.55f, .946f, .055f), V(.518f, .937f, .222f), V(.444f, .885f, .245f));
        }

        private void OpenCart(Transform cart, float width, float depth, float top)
        {
            foreach (int side in new[] { -1, 1 })
            foreach (int end in new[] { -1, 1 })
            {
                Vector3 leg = V(side * (width / 2 - .063f), .130f, end * (depth / 2 - .062f));
                art.Rod(cart, M("Steel"), leg, V(leg.x, top, leg.z), .023f, 12);
                Caster(cart, V(leg.x, 0, leg.z), .059f);
            }

            foreach (float y in new[] { .29f, top })
            {
                art.Box(cart, M("Steel"), V(0, y, 0), V(width, .051f, depth), .022f);
                art.Box(cart, M("ShellSide"), V(0, y + .027f, 0), V(width - .040f, .012f, depth - .040f), .005f);
                foreach (int side in new[] { -1, 1 })
                    art.Box(cart, M("BrightSteel"), V(side * (width / 2 - .018f), y + .022f, 0),
                        V(.025f, .035f, depth - .037f), .010f);
            }

            Bounds(cart, "Cart Bumper Footprint", V(0, .160f, 0), V(width, .32f, depth));
            Bounds(cart, "Cart Tray Collider", V(0, top + .005f, 0), V(width, .076f, depth));
        }

        private void InfusionStand(Transform equipment, Vector3 position)
        {
            var stand = Node(equipment, "IV Stand", position);
            art.Disc(stand, M("PaintedMetal"), V(0, .13f, 0), Vector3.up, .088f, .19f);
            art.Disc(stand, M("Steel"), V(0, .79f, 0), Vector3.up, .027f, 1.32f);
            art.Disc(stand, M("Steel"), V(0, 1.66f, 0), Vector3.up, .017f, .66f);
            art.Disc(stand, M("Rubber"), V(.04f, 1.419f, 0), Vector3.right, .031f, .045f, 16);
            for (int i = 0; i < 5; i++)
            {
                float angle = i * Mathf.PI * .4f;
                Vector3 foot = V(.277f * Mathf.Cos(angle), 0, .277f * Mathf.Sin(angle));
                art.Rod(stand, M("PaintedMetal"), V(0, .15f, 0), foot + V(0, .087f, 0), .026f);
                Caster(stand, foot, .039f);
            }

            foreach (int side in new[] { -1, 1 })
            {
                art.Hose(stand, M("Steel"), .010f, V(0, 1.989f, 0), V(.16f * side, 1.989f, 0),
                    V(.201f * side, 1.949f, 0), V(.194f * side, 1.921f, 0), V(.173f * side, 1.932f, 0));
            }
            Bounds(stand, "IV Base Collider", V(0, .104f, 0), V(.55f, .20f, .55f));
            Bounds(stand, "IV Pole Collider", V(0, 1.05f, 0), V(.055f, 1.9f, .055f));
        }

        private void Waste(Transform equipment)
        {
            var group = Node(equipment, "Clinical Waste Station", V(3.31f, 0, -2.69f), V(0, -90, 0));
            for (int i = 0; i < 2; i++)
            {
                float x = (i - .5f) * .52f;
                string color = i == 0 ? "Red" : "ShellSide";
                art.Box(group, M("Rubber"), V(x, .069f, 0), V(.384f, .102f, .36f), .048f);
                art.Box(group, M(color), V(x, .332f, 0), V(.418f, .563f, .389f), .065f);
                art.Box(group, M(color), V(x, .620f, 0), V(.442f, .081f, .416f), .039f);
                art.Box(group, M("Rubber"), V(x, .084f, .221f), V(.133f, .034f, .090f), .015f);
                art.Plate(group, M("WasteSign"), V(x, .463f, .196f), .25f, .125f, Vector3.zero, Vector2.one);
                Bounds(group, "Waste Container Collider " + i, V(x, .333f, 0), V(.445f, .667f, .428f));
            }
        }

        private void Screen(Transform group, Vector3 origin, float width, float height, string texture)
        {
            art.Box(group, M("ShellSide"), origin + V(0, 0, -.021f), V(width, height, .102f), .036f);
            art.Box(group, M("ScreenFrame"), origin + V(0, 0, .032f), V(width * .982f, height * .965f, .041f), .018f);
            var display = Node(group, texture + " Screen Image", origin);
            art.Plate(display, M(texture), V(0, .012f, .053f), width * .901f, height * .821f,
                Vector3.zero, Vector2.one);
            art.NoShadows(display);
            art.Disc(group, M("StatusGreen"), origin + V(width * .365f, -height * .449f, .053f),
                Vector3.forward, .0055f, .008f, 10);
            art.Disc(group, M("Gasket"), origin + V(width * .408f, -height * .449f, .054f),
                Vector3.forward, .008f, .008f, 10);
        }

        private void Caster(Transform group, Vector3 floor, float radius)
        {
            Vector3 pivot = floor + V(0, radius, 0);
            art.Box(group, M("PaintedMetal"), pivot + V(0, radius * .68f, 0),
                V(radius * .91f, radius * .99f, radius * 1.45f), radius * .18f);
            art.Disc(group, M("Rubber"), pivot, Vector3.right, radius, radius * .68f, 16);
            foreach (int side in new[] { -1, 1 })
                art.Disc(group, M("Steel"), pivot + V(side * radius * .352f, 0, 0),
                    Vector3.right, radius * .51f, radius * .067f, 12);
        }
    }
}
