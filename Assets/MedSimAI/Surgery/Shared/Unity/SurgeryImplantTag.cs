using UnityEngine;

namespace MedSimAI.Surgery.Shared.Unity
{
    /// <summary>On the ROOT of every implant / trial component prefab (Person 2).</summary>
    [DisallowMultipleComponent]
    public class SurgeryImplantTag : MonoBehaviour
    {
        public string implantId = "";
        [Tooltip("Child transform marking the authored correct pose inside the anatomy prefab: PlacementTarget_<implantId>.")] public Transform placementTarget;
    }
}
