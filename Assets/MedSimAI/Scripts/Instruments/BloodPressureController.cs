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
        [Tooltip("As on a real automatic monitor, the student starts the measurement on the device (START) after applying the cuff. Off: measures as soon as the cuff is placed (v0.6 behaviour).")]
        public bool requireStart = true;
        Coroutine cycle; PlacementZone readyZone;
        /// <summary>The cuff is on a valid zone and the device waits for START.</summary>
        public bool Ready => readyZone != null && Attached && cycle == null;
        /// <summary>The device the student presses START on (the NIBP unit on the trolley).</summary>
        public Transform Device => deviceDisplay ? deviceDisplay.transform.parent : null;

        protected override void Awake()
        {
            base.Awake(); if (!pumpLoop) pumpLoop = MedSimAI.Audio.ProceduralClinicalAudio.PumpLoop();
            var dev = Device;
            if (dev && !dev.GetComponent<NibpStartButton>())
            {   // the modelled NIBP unit becomes the START control: desktop E / click, VR poke or ray select
                var box = dev.gameObject.AddComponent<BoxCollider>(); box.size = new Vector3(0.18f, 0.13f, 0.09f);
                dev.gameObject.AddComponent<NibpStartButton>().cuff = this;
                var xi = dev.gameObject.AddComponent<XRSimpleInteractable>(); xi.selectEntered.AddListener(_ => StartMeasurement());
            }
        }

        protected override void OnPlaced(PlacementZone zone)
        {
            if (cycle != null) { StopCoroutine(cycle); cycle = null; }
            if (!requireStart) { cycle = StartCoroutine(Measure(zone)); return; }
            readyZone = zone;
            if (deviceDisplay) deviceDisplay.text = "READY\n<size=50%>START</size>";
            if (monitor) monitor.SetNibpStatus("Cuff on — press START");
            ClinicalFeedbackBus.Publish("Blood pressure", "Cuff applied to " + zone.displayName + ". Press START on the NIBP monitor.");
            Play(confirmClip);
        }

        /// <summary>START on the device: measures only with the cuff really applied to a valid zone.</summary>
        public bool StartMeasurement()
        {
            if (cycle != null) return false;
            if (!Ready) { ClinicalFeedbackBus.Publish("Blood pressure", "Apply the cuff to the patient's upper arm first, then press START."); return false; }
            cycle = StartCoroutine(Measure(readyZone));
            return true;
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

        protected override void OnDetached()
        {
            readyZone = null;
            if (cycle != null) { StopCoroutine(cycle); cycle = null; RecordEvent(ClinicalEvents.InstrumentRemovedEarly, InstrumentId); if (monitor) monitor.SetNibpStatus(""); }
            if (sfx) sfx.Stop();
        }
        public bool Measuring => cycle != null;
        public override void ResetInstrument() { readyZone = null; base.ResetInstrument(); if (deviceDisplay) deviceDisplay.text = "--/--"; }
    }
}
