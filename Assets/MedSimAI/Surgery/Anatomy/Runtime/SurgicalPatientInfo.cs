using UnityEngine;

namespace MedSimAI.Surgery.Anatomy
{
    /// <summary>
    /// Root of SurgicalPatient.prefab (independent of the ER patient). Pure metadata + convenience toggles for tools.
    /// Frame: origin on the table support plane under the mid-point of the hip joints; +Z toward the head; +Y up;
    /// patient's RIGHT = −X (contract §4). Parent it to ANCHOR_PatientRoot with a zero local pose.
    /// The patient is static (anaesthetised, positioned): no animation or behaviour is implied.
    /// </summary>
    [DisallowMultipleComponent]
    public class SurgicalPatientInfo : MonoBehaviour
    {
        public string operativeSide = "R";
        [Tooltip("Standing height of the source body (m).")] public float statureMeters = 1.78f;
        [Tooltip("Operative knee flexion in the authored position (deg).")] public float kneeFlexionDeg = 97.5f;
        [Tooltip("Operative hip flexion in the authored position (deg).")] public float hipFlexionDeg = 48f;
        [Tooltip("Width across the shoulders/arms (m) — must fit the table deck (0.51 m).")] public float bodyWidthMeters = 0.50f;
        public Transform surgicalSite;      // ANCHOR_SurgicalSite_Knee_R
        public Transform kneeAnatomy;       // KneeAnatomy_R instance
        public Transform drape;             // SurgicalDrape instance
        public Transform bodyRoot;          // body meshes
        public Transform jointAnchors;      // Joint_* empties (hip/knee/ankle, both sides)

        public void SetDrapeVisible(bool on) { if (drape) drape.gameObject.SetActive(on); }
        public void SetBodyVisible(bool on) { if (bodyRoot) bodyRoot.gameObject.SetActive(on); }
    }
}
