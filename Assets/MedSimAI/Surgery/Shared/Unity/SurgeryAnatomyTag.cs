using UnityEngine;

namespace MedSimAI.Surgery.Shared.Unity
{
    /// <summary>On each anatomy interaction collider (Person 2).</summary>
    [DisallowMultipleComponent]
    public class SurgeryAnatomyTag : MonoBehaviour { public string targetId = ""; }
}
