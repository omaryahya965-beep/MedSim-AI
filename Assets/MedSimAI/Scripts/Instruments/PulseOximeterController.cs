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
    /// <summary>Finger pulse oximeter: release on a fingertip; displays scenario SpO2 and pulse after a sensor settle time.</summary>
    public class PulseOximeterController : MedicalInstrumentBase
    {
        public override string InstrumentId => "pulse_oximeter";
        protected override bool SupportsSnap => true;
        public TMP_Text deviceDisplay;
        public PatientMonitorController monitor;
        public float settleSeconds = 3f;
        Coroutine routine;
        PatientState liveSource;   // while the probe stays on, the reading follows the patient's authored state (live monitoring)

        protected override void OnPlaced(PlacementZone zone) { if (routine != null) StopCoroutine(routine); routine = StartCoroutine(Read(zone)); }

        IEnumerator Read(PlacementZone zone)
        {
            ClinicalFeedbackBus.Publish("Pulse oximetry", "Probe on " + zone.displayName + ". Acquiring signal…");
            for (float t = 0; t < settleSeconds; t += Time.deltaTime)
            {
                if (deviceDisplay) deviceDisplay.text = "<size=60%>SpO2</size> --\n<size=60%>PR</size> " + new string('·', 1 + (int)(t * 3) % 3);
                yield return null;
            }
            Vitals vit;
            if (DemonstrationUse) vit = Session.Patient.Vitals;   // a teacher's demonstration: current authored values, no exam recorded for the student
            else
            {
                var r = Session.PerformExam(InstrumentId, zone.zoneId);
                if (!r.success) { ClinicalFeedbackBus.Publish("Pulse oximetry", r.message); routine = null; yield break; }
                vit = r.vitals;
            }
            if (deviceDisplay) deviceDisplay.text = $"<size=60%>SpO2</size> {vit.spo2}%\n<size=60%>PR</size> {vit.heartRate}";
            if (monitor) monitor.RevealSpO2(vit.spo2);
            Play(confirmClip);
            ClinicalFeedbackBus.Publish("Pulse oximetry", $"SpO2 {vit.spo2}% on room air, pulse {vit.heartRate}/min.");
            MedSimAI.UI.VitalsResultPanel.Ensure().Show(Arabic ? "نسبة الأكسجين" : "OXYGEN SATURATION", $"{vit.spo2}%", Arabic ? $"النبض {vit.heartRate} /دقيقة" : $"Pulse {vit.heartRate} /min",
                "SpO2 · " + zone.displayName + (DemonstrationUse ? (Arabic ? " · عرض توضيحي" : " · demonstration") : ""), zone.transform);
            routine = null;
            StopLive(); liveSource = Session.Patient; if (liveSource != null) liveSource.VitalsChanged += OnVitals;
        }

        void OnVitals(Vitals v)
        {   // authored state change while the probe is on: the device and the monitor show the new values (never invented)
            if (!Attached || routine != null) return;
            if (deviceDisplay) deviceDisplay.text = $"<size=60%>SpO2</size> {v.spo2}%\n<size=60%>PR</size> {v.heartRate}";
            if (monitor) monitor.RevealSpO2(v.spo2);
        }
        void StopLive() { if (liveSource != null) liveSource.VitalsChanged -= OnVitals; liveSource = null; }
        void OnDestroy() => StopLive();

        protected override void OnDetached() { StopLive(); if (routine != null) { StopCoroutine(routine); routine = null; RecordEvent(ClinicalEvents.InstrumentRemovedEarly, InstrumentId); } if (deviceDisplay) deviceDisplay.text = ""; if (monitor) monitor.HideSpO2(); }
        public override void ResetInstrument() { StopLive(); base.ResetInstrument(); if (deviceDisplay) deviceDisplay.text = ""; }
    }
}
