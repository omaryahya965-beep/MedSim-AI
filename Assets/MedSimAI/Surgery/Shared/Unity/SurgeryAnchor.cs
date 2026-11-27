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
            // Contract 1.1: accept the alias names (e.g. Person 1's IntegrationAnchors) and tag the transform so later lookups are fast.
            var aliases = AnchorAliases.For(id);
            if (aliases.Length == 0) return null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (System.Array.IndexOf(aliases, t.name) >= 0)
                {
                    var tagged = t.gameObject.AddComponent<SurgeryAnchor>(); tagged.anchorId = id; registry[id] = tagged;
                    return t;
                }
            return null;
        }
#if UNITY_EDITOR
        void OnDrawGizmos() { Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.8f); Gizmos.DrawWireSphere(transform.position, 0.06f); Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.25f); }
#endif
    }
}
