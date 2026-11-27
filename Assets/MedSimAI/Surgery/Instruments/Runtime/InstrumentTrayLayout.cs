using UnityEngine;

namespace MedSimAI.Surgery.Instruments
{
    /// <summary>Instrument tray: one ANCHOR_InstrumentSlot_&lt;instrumentId&gt; (SurgeryAnchor) per instrument.</summary>
    [DisallowMultipleComponent]
    public class InstrumentTrayLayout : MonoBehaviour
    {
        public Transform[] slots = new Transform[0];
        public Transform FindSlot(string instrumentId)
        {
            foreach (var s in slots) if (s && s.name == "ANCHOR_InstrumentSlot_" + instrumentId) return s;
            return null;
        }
    }
}
