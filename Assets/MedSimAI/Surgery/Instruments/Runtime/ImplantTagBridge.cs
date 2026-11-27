using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Instruments
{
    /// <summary>
    /// Serialized carrier for the contract's <see cref="SurgeryImplantTag"/> (see <see cref="InstrumentTagBridge"/> for why).
    /// <see cref="placementTarget"/> is normally left empty in the prefab: mechanics/integration resolve
    /// KneeAnatomy_R/ImplantAnchors/PlacementTarget_&lt;implantId&gt; at runtime (or call <see cref="EnsureTag"/> after setting it).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class ImplantTagBridge : MonoBehaviour
    {
        public string implantId = "";
        public Transform placementTarget;

        void Awake() => EnsureTag();

        public SurgeryImplantTag EnsureTag()
        {
            var t = GetComponent<SurgeryImplantTag>(); if (!t) t = gameObject.AddComponent<SurgeryImplantTag>();
            t.implantId = implantId; if (placementTarget) t.placementTarget = placementTarget;
            return t;
        }
    }
}
