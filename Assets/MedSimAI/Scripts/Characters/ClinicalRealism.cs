using UnityEngine;

namespace MedSimAI.Characters
{
    /// <summary>
    /// Runtime set-up of the clinical-room realism fixes on the existing patient (no scene regeneration needed):
    /// head-on-pillow support and the roll-up gown sleeve. Idempotent; called once at start-up by the clinical UI.
    /// </summary>
    public static class ClinicalRealism
    {
        public static void Install()
        {
            var patient = Object.FindAnyObjectByType<PatientInteractionController>();
            if (!patient) return;
            var go = patient.gameObject;
            if (!go.GetComponent<HeadPillowSupport>())
            {
                var bed = GameObject.Find("Hospital Bed");
                var hs = bed ? bed.transform.Find("Head_Section") : null;
                var pillow = hs ? (hs.Find("v2_Pillow") ?? hs.Find("Pillow")) : null;
                if (hs && pillow && pillow.gameObject.activeInHierarchy)
                {
                    var s = go.AddComponent<HeadPillowSupport>(); s.backrest = hs; s.pillow = pillow;
                    var mf = pillow.GetComponentInChildren<MeshFilter>(); if (mf && mf.sharedMesh) s.pillowThickness = mf.sharedMesh.bounds.size.y * pillow.lossyScale.y * s.pillowFlatten;
                }
            }
            if (!go.TryGetComponent<GownSleeveRoll>(out var roll)) roll = go.AddComponent<GownSleeveRoll>();
            var mgr = MedSimAI.Core.ClinicalScenarioManager.Instance;
            if (mgr) mgr.SessionReset += () => { if (roll) roll.SetRolled(false, false); };
        }
    }
}
