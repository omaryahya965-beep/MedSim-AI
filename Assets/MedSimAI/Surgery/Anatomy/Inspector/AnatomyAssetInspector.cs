using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Anatomy;
using MedSimAI.Surgery.Instruments;
using MedSimAI.Surgery.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimAI.Surgery.AssetsInspector
{
    /// <summary>
    /// Asset-verification tool for Anatomy_Test.unity (NOT surgical gameplay): switch the neutral anatomy test states,
    /// toggle structure categories, show anchors, explode the knee, identify structures, preset / orbit cameras.
    /// </summary>
    public class AnatomyAssetInspector : MonoBehaviour
    {
        public KneeAnatomyStateController knee;
        public SurgicalPatientInfo patient;
        public Transform explodedKnee;
        public Camera cam;
        public Transform[] cameraPoses = new Transform[0];
        public bool showPanel = true;
        [Range(0, 1)] public float explode;
        public bool showAnchors;

        Vector3 orbitTarget; float yaw, pitch, dist = 1.2f; string picked = "";
        readonly Dictionary<AnatomyCategory, bool> catOn = new Dictionary<AnatomyCategory, bool>();
        readonly Dictionary<Transform, Vector3> explodeRest = new Dictionary<Transform, Vector3>();
        readonly List<GameObject> anchorMarkers = new List<GameObject>(); bool anchorsBuilt;
        Vector2 scroll;

        public static readonly (string name, (string id, string st)[] states)[] Presets =
        {
            ("Intact", new (string, string)[0]),
            ("Marked", new[] { (AnatomyTargetIds.SiteMarking, AnatomyStates.Marked) }),
            ("Incised", new[] { (AnatomyTargetIds.SiteMarking, AnatomyStates.Marked), (AnatomyTargetIds.IncisionLine, AnatomyStates.Incised) }),
            ("Exposed", new[] { (AnatomyTargetIds.SiteMarking, AnatomyStates.Marked), (AnatomyTargetIds.IncisionLine, AnatomyStates.Incised), (AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed) }),
            ("Guides placed", new[] { (AnatomyTargetIds.IncisionLine, AnatomyStates.Incised), (AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed), (AnatomyTargetIds.DistalFemur, AnatomyStates.GuidePlaced), (AnatomyTargetIds.ProximalTibia, AnatomyStates.GuidePlaced) }),
            ("Resected", new[] { (AnatomyTargetIds.IncisionLine, AnatomyStates.Incised), (AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed), (AnatomyTargetIds.DistalFemur, AnatomyStates.Resected), (AnatomyTargetIds.ProximalTibia, AnatomyStates.Resected) }),
            ("Trial fitted", new[] { (AnatomyTargetIds.IncisionLine, AnatomyStates.Incised), (AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed), (AnatomyTargetIds.DistalFemur, AnatomyStates.TrialFitted), (AnatomyTargetIds.ProximalTibia, AnatomyStates.TrialFitted) }),
            ("Implanted", new[] { (AnatomyTargetIds.IncisionLine, AnatomyStates.Incised), (AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed), (AnatomyTargetIds.DistalFemur, AnatomyStates.Implanted), (AnatomyTargetIds.ProximalTibia, AnatomyStates.Implanted) }),
            ("Closed", new[] { (AnatomyTargetIds.IncisionLine, AnatomyStates.Incised), (AnatomyTargetIds.JointCapsule, AnatomyStates.Exposed), (AnatomyTargetIds.DistalFemur, AnatomyStates.Implanted), (AnatomyTargetIds.ProximalTibia, AnatomyStates.Implanted), (AnatomyTargetIds.Wound, AnatomyStates.Closed) }),
        };

        /// <summary>Applies a named preset (used by the UI and by the evidence capture).</summary>
        public void ApplyPreset(string name)
        {
            if (!knee) return;
            knee.ResetAll();
            foreach (var (id, st) in Presets.First(p => p.name == name).states) knee.SetTargetState(id, st);
        }

        void Start()
        {
            if (!cam) cam = Camera.main;
            foreach (AnatomyCategory c in System.Enum.GetValues(typeof(AnatomyCategory))) catOn[c] = true;
            if (cameraPoses.Length > 0) GoTo(0);
        }

        public void GoTo(int i)
        {
            if (!cam || i >= cameraPoses.Length || !cameraPoses[i]) return;
            cam.transform.SetPositionAndRotation(cameraPoses[i].position, cameraPoses[i].rotation);
            orbitTarget = cam.transform.position + cam.transform.forward * 0.9f; dist = 0.9f;
            var e = cam.transform.rotation.eulerAngles; yaw = e.y; pitch = e.x;
        }

        void Update()
        {
            var m = Mouse.current;
            if (m != null && cam)
            {
                if (m.rightButton.isPressed) { var d = m.delta.ReadValue(); yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.25f, -85, 85); ApplyOrbit(); }
                if (m.middleButton.isPressed) { var d = m.delta.ReadValue(); orbitTarget += (-cam.transform.right * d.x + -cam.transform.up * d.y) * 0.0015f * dist; ApplyOrbit(); }
                float s = m.scroll.ReadValue().y; if (Mathf.Abs(s) > 0.01f) { dist = Mathf.Clamp(dist * (s > 0 ? 0.9f : 1.1f), 0.08f, 6f); ApplyOrbit(); }
                if (m.leftButton.wasPressedThisFrame && !(showPanel && m.position.ReadValue().x < 330)) Pick(m.position.ReadValue());
            }
            ApplyExplode();
        }
        void ApplyOrbit() { var r = Quaternion.Euler(pitch, yaw, 0); cam.transform.SetPositionAndRotation(orbitTarget - r * Vector3.forward * dist, r); }

        void Pick(Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(screen); float best = float.MaxValue; picked = "";
            foreach (var s in FindObjectsByType<AnatomyStructure>(FindObjectsSortMode.None))
            {
                var r = s.GetComponent<Renderer>(); if (!r || !r.enabled || !s.gameObject.activeInHierarchy) continue;
                if (r.bounds.IntersectRay(ray, out float d) && d < best) { best = d; picked = $"{s.structureId}  [{s.category}]  {s.displayNameEn}\n{s.source}" + (s.educationalPlaceholder ? "\nEDUCATIONAL PLACEHOLDER" : ""); }
            }
            foreach (var i in FindObjectsByType<InstrumentAssetInfo>(FindObjectsSortMode.None))
                foreach (var r in i.GetComponentsInChildren<Renderer>())
                    if (r.bounds.IntersectRay(ray, out float d) && d < best) { best = d; picked = $"{i.name}  [{i.category}]  {i.catalogName}"; }
        }

        public void SetCategory(AnatomyCategory c, bool on)
        {
            catOn[c] = on;
            foreach (var s in FindObjectsByType<AnatomyStructure>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s.category == c) foreach (var r in s.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var k in FindObjectsByType<KneeAnatomyStateController>(FindObjectsSortMode.None)) k.ApplyEnclosureCulling();
        }

        public void SetAnchors(bool on)
        {
            showAnchors = on;
            if (!anchorsBuilt) BuildAnchorMarkers();
            foreach (var g in anchorMarkers) if (g) g.SetActive(on);
        }
        static bool IsAnchorName(string n) => n.StartsWith("ANCHOR_") || n.StartsWith("PlacementTarget_") || n.StartsWith("GuideTarget_") || n.EndsWith("Root") ||
            n == "GripPoint" || n == "Tip" || n == "InteractionPoint" || n == "AttachmentPoint" || n == "ResetAnchor" || n == "SeatPoint" || n == "PlacementAnchor";
        void BuildAnchorMarkers()
        {
            anchorsBuilt = true; var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => IsAnchorName(t.name)).ToArray())
            {
                var root = new GameObject("marker_" + t.name); root.transform.SetParent(t, false); anchorMarkers.Add(root);
                var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(sph.GetComponent<Collider>()); sph.transform.SetParent(root.transform, false); sph.transform.localScale = Vector3.one * 0.008f;
                var m = new Material(mat) { color = t.name.StartsWith("ANCHOR_") ? Color.cyan : t.name == "Tip" ? Color.red : t.name == "GripPoint" ? Color.green : Color.yellow }; sph.GetComponent<Renderer>().sharedMaterial = m;
                foreach (var (dir, col) in new[] { (Vector3.right, Color.red), (Vector3.up, Color.green), (Vector3.forward, Color.blue) })
                {
                    var ax = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(ax.GetComponent<Collider>()); ax.transform.SetParent(root.transform, false);
                    ax.transform.localPosition = dir * 0.015f; ax.transform.localScale = dir * 0.03f + (Vector3.one - dir) * 0.0015f; ax.GetComponent<Renderer>().sharedMaterial = new Material(mat) { color = col };
                }
            }
        }

        void ApplyExplode()
        {
            if (!explodedKnee) return;
            var center = explodedKnee.position;
            foreach (var s in explodedKnee.GetComponentsInChildren<AnatomyStructure>(true))
            {
                var t = s.transform; if (!explodeRest.ContainsKey(t)) explodeRest[t] = t.localPosition;
                var r = s.GetComponent<Renderer>(); var c = r ? r.bounds.center : t.position; var dir = (c - center); dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
                float k = s.category == AnatomyCategory.Bone ? 0.9f : s.category == AnatomyCategory.Ligament ? 1.4f : 1.2f;
                t.localPosition = explodeRest[t] + t.parent.InverseTransformDirection(dir) * (explode * 0.09f * k);
            }
        }

        void OnGUI()
        {
            if (!showPanel) { if (GUI.Button(new Rect(8, 8, 90, 24), "Panel")) showPanel = true; return; }
            GUILayout.BeginArea(new Rect(8, 8, 320, Screen.height - 16), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<b>MedSim AI — surgical asset inspector</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label(SurgeryContract.VALIDATION_BANNER);
            if (GUILayout.Button("Hide panel")) showPanel = false;
            GUILayout.Label("Neutral test states (educational placeholders):");
            foreach (var p in Presets) if (GUILayout.Button(p.name)) ApplyPreset(p.name);
            if (knee)
            {
                bool cv = GUILayout.Toggle(knee.showComponentVisuals, "Show built-in trial/implant visuals"); if (cv != knee.showComponentVisuals) { knee.showComponentVisuals = cv; knee.Refresh(true); }
                foreach (var kv in knee.Targets)
                {
                    GUILayout.Label($"{kv.Key}: <b>{kv.Value.CurrentState}</b>", new GUIStyle(GUI.skin.label) { richText = true });
                    GUILayout.BeginHorizontal(); foreach (var st in kv.Value.SupportedStates) if (GUILayout.Button(st, GUILayout.ExpandWidth(false))) kv.Value.SetState(st); GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label("Visibility:");
            foreach (AnatomyCategory c in System.Enum.GetValues(typeof(AnatomyCategory)))
            {
                bool on = catOn.TryGetValue(c, out var v) ? v : true; bool n = GUILayout.Toggle(on, c.ToString()); if (n != on) SetCategory(c, n);
            }
            bool a = GUILayout.Toggle(showAnchors, "Show anchors (grip=green, tip=red, ANCHOR_=cyan)"); if (a != showAnchors) SetAnchors(a);
            if (explodedKnee) { GUILayout.Label($"Exploded view: {explode:0.00}"); explode = GUILayout.HorizontalSlider(explode, 0, 1); }
            GUILayout.Label("Cameras:");
            for (int i = 0; i < cameraPoses.Length; i++) if (cameraPoses[i] && GUILayout.Button(cameraPoses[i].name)) GoTo(i);
            GUILayout.Label("Right-drag orbit · middle-drag pan · wheel zoom · left-click identify");
            if (!string.IsNullOrEmpty(picked)) GUILayout.Label(picked);
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
