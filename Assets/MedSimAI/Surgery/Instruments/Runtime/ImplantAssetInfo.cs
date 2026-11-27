using UnityEngine;

namespace MedSimAI.Surgery.Instruments
{
    /// <summary>Metadata on every implant / trial prefab root (next to <c>SurgeryImplantTag</c>). Generic educational geometry.</summary>
    [DisallowMultipleComponent]
    public class ImplantAssetInfo : MonoBehaviour
    {
        public bool isTrial;
        [Tooltip("Anatomy target the component seats on (knee_r.*).")] public string targetId = "";
        public Transform gripPoint, placementAnchor, resetAnchor;
        [Tooltip("Size (mm) in the implant frame: X medio-lateral, Y proximal-distal, Z antero-posterior.")] public Vector3 sizeMm;
        [TextArea(1, 3)] public string notes = "Generic educational geometry derived from the virtual bone cuts. Not a medical-device design; not for manufacturing or planning.";
    }
}
