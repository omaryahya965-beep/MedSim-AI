using UnityEngine;

namespace MedSimAI.Surgery.Anatomy
{
    /// <summary>Kind of anatomical / state asset (used by the asset inspector, validation and the catalog).</summary>
    public enum AnatomyCategory { Bone, Cartilage, Meniscus, Ligament, Tendon, SoftTissue, Muscle, Skin, SkinOverlay, StateVisual, ComponentVisual, Patient, Drape }

    /// <summary>
    /// Metadata on every independently accessible structure of the surgical patient / knee prefabs.
    /// Pure data: no behaviour. Person 3 finds structures through <see cref="structureId"/> (stable) rather than mesh names.
    /// </summary>
    [DisallowMultipleComponent]
    public class AnatomyStructure : MonoBehaviour
    {
        [Tooltip("Stable identifier, e.g. knee_r.bone.distal_femur. See Docs/Surgery/Anatomy/PREFAB_CONTRACT.md.")]
        public string structureId = "";
        public AnatomyCategory category;
        public string displayNameEn = "", displayNameAr = "";
        [Tooltip("Asset source and licence (see Docs/Surgery/Anatomy/ASSET_LICENSES.md).")]
        public string source = "";
        [Tooltip("True for educational placeholder geometry (e.g. illustrative resection planes). Never a clinical specification.")]
        public bool educationalPlaceholder;
        [Tooltip("Shipped inactive by default (e.g. deep posterior muscles).")]
        public bool optionalStructure;
        [TextArea(1, 4)] public string notes = "";
    }
}
