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
            var r = Session.PerformExam(InstrumentId, zone.zoneId);
            if (!r.success) { ClinicalFeedbackBus.Publish("Pulse oximetry", r.message); yield break; }
            if (deviceDisplay) deviceDisplay.text = $"<size=60%>SpO2</size> {r.vitals.spo2}%\n<size=60%>PR</size> {r.vitals.heartRate}";
            if (monitor) monitor.RevealSpO2(r.vitals.spo2);
            Play(confirmClip);
            ClinicalFeedbackBus.Publish("Pulse oximetry", $"SpO2 {r.vitals.spo2}% on room air, pulse {r.vitals.heartRate}/min.");
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
