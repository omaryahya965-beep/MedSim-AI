using System.Collections.Generic;
using UnityEngine;

namespace MedSimAI.Surgery.Shared.Unity
{
    /// <summary>Named integration anchor. The GameObject name SHOULD equal anchorId; lookup uses anchorId.</summary>
    [DisallowMultipleComponent]
    public class SurgeryAnchor : MonoBehaviour
    {
        public string anchorId = "";
        static readonly Dictionary<string, SurgeryAnchor> registry = new Dictionary<string, SurgeryAnchor>();
        void OnEnable() { if (!string.IsNullOrEmpty(anchorId)) registry[anchorId] = this; }
        void OnDisable() { if (registry.TryGetValue(anchorId, out var a) && a == this) registry.Remove(anchorId); }
        void Reset() { anchorId = name; }
        /// <summary>Finds an active anchor by id (registry first, then a scene search for anchors enabled before this domain).</summary>
        public static Transform Find(string id)
        {
            if (registry.TryGetValue(id, out var a) && a) return a.transform;
            foreach (var x in Object.FindObjectsByType<SurgeryAnchor>(FindObjectsSortMode.None)) if (x.anchorId == id) { registry[id] = x; return x.transform; }
            return null;
        }
#if UNITY_EDITOR
        void OnDrawGizmos() { Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.8f); Gizmos.DrawWireSphere(transform.position, 0.06f); Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.25f); }
#endif
    }

    /// <summary>On the ROOT of every instrument prefab (Person 2). Pivot = grip point, +Z = working tip direction.</summary>
    [DisallowMultipleComponent]
    public class SurgeryInstrumentTag : MonoBehaviour
    {
        public string instrumentId = "";
        [Tooltip("Child transform at the working tip/blade (used for target contact and hand-over).")] public Transform tip;
        [Tooltip("Localisation key or display name, e.g. 'Scalpel' / 'مشرط'.")] public string displayNameEn = "", displayNameAr = "";
    }

    /// <summary>On each anatomy interaction collider (Person 2).</summary>
    public class SurgeryAnatomyTag : MonoBehaviour { public string targetId = ""; }

    /// <summary>On the ROOT of every implant / trial component prefab (Person 2).</summary>
    [DisallowMultipleComponent]
    public class SurgeryImplantTag : MonoBehaviour
    {
        public string implantId = "";
        [Tooltip("Child transform marking the authored correct pose inside the anatomy prefab: PlacementTarget_<implantId>.")] public Transform placementTarget;
    }

    /// <summary>
    /// Scene host for the session's SurgeryEventBus (one per surgical scene, placed by the integration lead; each test
    /// scene adds its own). Mechanics publish with SurgeryEventBusHost.Publish(...). The bus clock is Unity time.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class SurgeryEventBusHost : MonoBehaviour
    {
        public static SurgeryEventBusHost Instance { get; private set; }
        public bool logToConsole = true;
        public SurgeryEventBus Bus { get; } = new SurgeryEventBus();

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(this); return; }
            Instance = this;
            Bus.Clock = () => Time.timeAsDouble;
            Bus.OnSubscriberError = msg => Debug.LogWarning("[Surgery] event subscriber error: " + msg);
            Bus.EventPublished += e => { if (logToConsole) Debug.Log("[Surgery] " + e); };
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void Publish(SurgeryEvent e)
        {
            if (Instance) Instance.Bus.Publish(e);
            else Debug.LogWarning("[Surgery] No SurgeryEventBusHost in scene; event dropped: " + e?.id);
        }
    }
}
