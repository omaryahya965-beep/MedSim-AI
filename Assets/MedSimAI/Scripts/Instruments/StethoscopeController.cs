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
    /// <summary>Stethoscope: hold the diaphragm on a valid chest zone for a short dwell to auscultate.</summary>
    public class StethoscopeController : MedicalInstrumentBase
    {
        public override string InstrumentId => "stethoscope";
        public float dwellSeconds = 1.0f;
        [Tooltip("2D source near the player's head, simulating the earpieces.")] public AudioSource earpiece;
        float dwell; PlacementZone dwellZone; readonly Dictionary<string, ExamResult> cache = new Dictionary<string, ExamResult>();
        string playingKey;

        protected override void WhileHeld(PlacementZone zone)
        {
            if (zone && IsAligned(zone))
            {
                if (zone != dwellZone) { dwellZone = zone; dwell = 0; }
                dwell += Time.deltaTime;
                if (dwell >= dwellSeconds)
                {
                    if (!cache.TryGetValue(zone.zoneId, out var r))
                    {
                        if (cache.Count == 0) RecordEvent(ClinicalEvents.InstrumentOnPatient, InstrumentId + "@" + zone.zoneId);
                        r = Session.PerformExam(InstrumentId, zone.zoneId); // recorded once per zone
                        cache[zone.zoneId] = r;
                        if (r.success) { ClinicalFeedbackBus.Publish("Auscultation — " + zone.displayName, r.finding); Haptic(0.4f, 0.08f); }
                    }
                    if (r.success)
                    {
                        if (playingKey != r.soundKey) StartSound(r.soundKey);
                        if (r.soundKey == "heart_normal") HeartbeatHaptics(r.vitals.heartRate);
                    }
                }
            }
            else
            {
                dwellZone = null; dwell = 0; StopSound();
                // Diaphragm resting on the patient at a non-auscultation site (e.g. the arm) for a while → wrong placement.
                if (NearWrongZone()) { wrongDwell += Time.deltaTime; if (wrongDwell > 1.2f) { wrongDwell = -6f; RecordEvent(ClinicalEvents.ZoneRejected, InstrumentId); } }
                else if (wrongDwell > 0) wrongDwell = 0;
            }
        }
        float wrongDwell;

        float nextBeat;
        void HeartbeatHaptics(int hr)
        {
            if (Time.time < nextBeat) return;
            nextBeat = Time.time + 60f / Mathf.Max(30, hr);
            Haptic(0.2f, 0.04f);
        }

        void StartSound(string key)
        {
            playingKey = key;
            if (!earpiece) return;
            var hr = Session.Patient.Vitals.heartRate; var rr = Session.Patient.Vitals.respiratoryRate;
            earpiece.clip = key == "heart_normal" ? ProceduralClinicalAudio.HeartSounds(hr) : ProceduralClinicalAudio.BreathSounds(rr);
            earpiece.loop = true; earpiece.Play();
        }
        void StopSound() { playingKey = null; if (earpiece && earpiece.isPlaying) earpiece.Stop(); }
        protected override void ReleaseCore() { base.ReleaseCore(); StopSound(); dwellZone = null; }
        public override void ResetInstrument() { base.ResetInstrument(); cache.Clear(); StopSound(); }
    }
}
