using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Instruments
{
    /// <summary>
    /// Serialized carrier for the contract's <see cref="SurgeryInstrumentTag"/>.
    /// WHY: Shared/Unity/SurgerySceneTags.cs declares five MonoBehaviours in one file; Unity serializes only the class the
    /// file maps to (SurgeryAnchor), so a SurgeryInstrumentTag saved in a prefab becomes a "missing script". Until Person 4
    /// moves each tag class to its own file, this bridge (which does serialize) adds the real tag very early at runtime
    /// (execution order −1000, before any mechanics/AI code) with the same values. Remove it once the shared fix lands.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class InstrumentTagBridge : MonoBehaviour
    {
        public string instrumentId = "";
        public Transform tip;
        public string displayNameEn = "", displayNameAr = "";

        void Awake() => EnsureTag();

        /// <summary>Adds (or refreshes) the shared SurgeryInstrumentTag on this GameObject and returns it.</summary>
        public SurgeryInstrumentTag EnsureTag()
        {
            var t = GetComponent<SurgeryInstrumentTag>(); if (!t) t = gameObject.AddComponent<SurgeryInstrumentTag>();
            t.instrumentId = instrumentId; t.tip = tip; t.displayNameEn = displayNameEn; t.displayNameAr = displayNameAr;
            return t;
        }
    }
}
