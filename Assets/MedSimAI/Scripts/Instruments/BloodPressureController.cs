using System.Collections;
using System.Collections.Generic;
using MedSimAI.Audio;
using MedSimAI.Clinical;
using MedSimAI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MedSimAI.Instruments
{
    /// <summary>Blood-pressure cuff: release on the upper arm to wrap; the monitor then runs a scenario-defined NIBP cycle.</summary>
    public class BloodPressureController : MedicalInstrumentBase
    {
        public override string InstrumentId => "bp_cuff";
        protected override bool SupportsSnap => true;
        public TMP_Text deviceDisplay;
        public PatientMonitorController monitor;
        public Transform cuffVisual; public float inflateScale = 1.12f;
        public AudioClip pumpLoop;
        Coroutine cycle;

        protected override void Awake() { base.Awake(); if (!pumpLoop) pumpLoop = MedSimAI.Audio.ProceduralClinicalAudio.PumpLoop(); }

        protected override void OnPlaced(PlacementZone zone)
        {
            if (cycle != null) StopCoroutine(cycle);
            cycle = StartCoroutine(Measure(zone));
        }

        IEnumerator Measure(PlacementZone zone)
        {
            ClinicalFeedbackBus.Publish("Blood pressure", "Cuff applied to " + zone.displayName + ". Measuring…");
            if (sfx && pumpLoop) { sfx.clip = pumpLoop; sfx.loop = true; sfx.Play(); }
            float t = 0, inflate = 4f, deflate = 3f;
            while (t < inflate + deflate)
            {
                t += Time.deltaTime;
                float p = t < inflate ? Mathf.Lerp(0, 180, t / inflate) : Mathf.Lerp(180, 40, (t - inflate) / deflate);
                if (deviceDisplay) deviceDisplay.text = $"<size=60%>CUFF</size>\n{p:0}<size=50%> mmHg</size>";
                if (monitor) monitor.SetNibpStatus($"Measuring… {p:0}");
                if (cuffVisual) cuffVisual.localScale = Vector3.one * Mathf.Lerp(1, inflateScale, Mathf.Clamp01(p / 180f));
                yield return null;
            }
            if (sfx) { sfx.loop = false; sfx.Stop(); }
            if (cuffVisual) cuffVisual.localScale = Vector3.one;
            var r = Session.PerformExam(InstrumentId, zone.zoneId);
            if (!r.success) { ClinicalFeedbackBus.Publish("Blood pressure", r.message); yield break; }
            var v = r.vitals;
            if (deviceDisplay) deviceDisplay.text = $"{v.systolic}/{v.diastolic}\n<size=50%>PR {v.heartRate}</size>";
            if (monitor) monitor.RevealBloodPressure(v.systolic, v.diastolic);
            Play(confirmClip);
            ClinicalFeedbackBus.Publish("Blood pressure", $"{v.systolic}/{v.diastolic} mmHg, pulse {v.heartRate}/min ({zone.displayName}).");
            cycle = null;
        }

        protected override void OnDetached() { if (cycle != null) { StopCoroutine(cycle); cycle = null; } if (sfx) sfx.Stop(); }
        public override void ResetInstrument() { base.ResetInstrument(); if (deviceDisplay) deviceDisplay.text = "--/--"; }
    }
}
