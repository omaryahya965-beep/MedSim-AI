using UnityEngine;

namespace MedSimAI.Surgery.Instruments
{
    public enum InstrumentCategory { Marking, Cutting, Retraction, Guide, PoweredCutting, PoweredDrilling, Impaction, Suturing, Closure, Grasping, Suction, Other }

    /// <summary>
    /// Catalog metadata on the ROOT of every instrument prefab, next to the shared <c>SurgeryInstrumentTag</c>
    /// (which carries the contract id, tip and display names). Conventions (contract §4): pivot = grip point,
    /// +Z toward the working tip, +Y up, scale (1,1,1). No gameplay: Person 3 attaches the interaction behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public class InstrumentAssetInfo : MonoBehaviour
    {
        public InstrumentCategory category;
        public string catalogName = "";
        [Tooltip("Overall size (mm): X width, Y height, Z length.")] public Vector3 sizeMm;
        public Transform gripPoint, tip, interactionPoint, attachmentPoint, resetAnchor;
        [Tooltip("Separately movable child components (jaws, blade, chuck, trigger...).")] public InstrumentMovablePart[] movableParts = new InstrumentMovablePart[0];
        [Tooltip("True if the id is a proposed extra that is not (yet) in SurgeryIds.InstrumentIds.")] public bool proposedIdNotInContract;
        [TextArea(1, 3)] public string notes = "Original generic design (MedSim AI). Not a model of any manufacturer's product.";
    }
}
