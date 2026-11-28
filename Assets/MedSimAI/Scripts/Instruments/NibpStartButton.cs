using UnityEngine;

namespace MedSimAI.Instruments
{
    /// <summary>Marks the NIBP unit as the blood-pressure START control (found by the desktop interaction ray).</summary>
    public class NibpStartButton : MonoBehaviour
    {
        public BloodPressureController cuff;
    }
}
