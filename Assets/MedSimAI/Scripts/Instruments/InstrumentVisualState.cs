using UnityEngine;

namespace MedSimAI.Instruments
{
    /// <summary>
    /// Swaps an instrument's visual between its "loose" model (lying on the trolley / held in the hand) and its
    /// "applied" model (e.g. the blood-pressure cuff wrapped round the arm) when the instrument is snapped to a zone.
    /// Purely cosmetic: the instrument scripts, colliders and probe points are untouched.
    /// </summary>
    public class InstrumentVisualState : MonoBehaviour
    {
        public GameObject[] looseVisuals, appliedVisuals;
        Transform homeParent; bool applied, init;

        void Awake() { homeParent = transform.parent; Apply(false); }

        void LateUpdate()
        {
            bool now = transform.parent != homeParent && transform.parent != null;
            if (now != applied || !init) Apply(now);
        }

        void Apply(bool on)
        {
            applied = on; init = true;
            if (looseVisuals != null) foreach (var g in looseVisuals) if (g) g.SetActive(!on);
            if (appliedVisuals != null) foreach (var g in appliedVisuals) if (g) g.SetActive(on);
        }
    }
}
