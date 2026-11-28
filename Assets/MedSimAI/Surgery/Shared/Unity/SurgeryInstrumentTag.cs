using UnityEngine;

namespace MedSimAI.Surgery.Shared.Unity
{
    /// <summary>On the ROOT of every instrument prefab (Person 2). Pivot = grip point, +Z = working tip direction.</summary>
    [DisallowMultipleComponent]
    public class SurgeryInstrumentTag : MonoBehaviour
    {
        public string instrumentId = "";
        [Tooltip("Child transform at the working tip/blade (used for target contact and hand-over).")] public Transform tip;
        [Tooltip("Localisation key or display name, e.g. 'Scalpel' / 'مشرط'.")] public string displayNameEn = "", displayNameAr = "";
    }
}
