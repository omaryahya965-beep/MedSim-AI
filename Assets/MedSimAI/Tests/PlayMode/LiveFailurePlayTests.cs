using System.Collections;
using System.Collections.Generic;
using MedSimAI.AI;
using MedSimAI.Clinical;
using MedSimAI.Core;
using MedSimAI.Instruments;
using MedSimAI.Voice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimAI.Tests
{
    /// <summary>Network failure & recovery without cost: the AI endpoint is unreachable (closed localhost port).</summary>
    public class LiveFailurePlayTests
    {
        [UnitySetUp] public IEnumerator Load()
        {
            AIProviderManager.ForceOffline = true; MedSimAI.XR.DisplayModeManager.DisableAutoDesktop = true;
            AIProviderManager.ConfigOverride = new AIRuntimeConfig { proxyEndpoint = "http://127.0.0.1:9", clientSessionToken = "test", preferOnline = true, timeoutMs = 3000 };
            yield return SceneManager.LoadSceneAsync("MedSim_ER");
            float t = 0; while ((SessionManager.Instance == null || SessionManager.Instance.State != AppState.MainMenu) && t < 10) { t += Time.unscaledDeltaTime; yield return null; }
        }

        [TearDown] public void Clear() { AIProviderManager.ConfigOverride = null; }

        [UnityTest] public IEnumerator UnreachableAI_FallsBackToTextOnly_WithClearMessage_AndNoAudio()
        {
            var ai = AIProviderManager.Instance;
            Assert.AreEqual(ConversationMode.OnlineAI, ai.Mode, "live path selected");
            var errors = new List<string>(); VoiceInteractionManager.Instance.Error += errors.Add;
            ConversationTurn turn = null; VoiceInteractionManager.Instance.TurnReady += x => turn = x;
            var t = VoiceInteractionManager.Instance.Submit(Persona.Patient, "When did the pain start?");
            float w = 0; while (!t.IsCompleted && w < 15) { w += Time.unscaledDeltaTime; yield return null; }
            Assert.NotNull(turn, "a reply is still produced");
            Assert.AreEqual(ResponseSource.ProviderFailureFallback, turn.source);
            Assert.IsNull(turn.audioId, "no audio → text only");
            Assert.AreEqual(LiveStatus.Offline, ai.Live.Status);
            StringAssert.Contains("unreachable", ai.Live.LastError);
            Assert.IsTrue(ClinicalScenarioManager.Instance.Session.Tracker.Has("FACT:H_ONSET"), "deterministic tracking still works");
            yield return new WaitForSeconds(0.2f);
            var src = VoiceInteractionManager.Instance.patientSpeech.GetComponent<AudioSource>();
            Assert.IsTrue(src.clip == null || !src.isPlaying, "no pre-recorded or synthetic audio played");
        }

        [UnityTest] public IEnumerator SessionRestart_ResetsStateDirectorAndDoor()
        {
            var sm = SessionManager.Instance; sm.StartClinicalSession(true); yield return null;
            var m = ClinicalScenarioManager.Instance;
            m.AdvanceScenarioTime(560f);
            Assert.AreEqual("S_DETERIORATION", m.Session.Patient.ConditionId);
            var door = Object.FindAnyObjectByType<HospitalDoor>(); if (door) { door.OpenBy(85); yield return new WaitForSeconds(1.5f); Assert.IsTrue(door.IsOpen); }
            sm.Restart(); yield return null;
            Assert.AreEqual("S_PRESENTATION", m.Session.Patient.ConditionId);
            Assert.AreEqual(0, m.Director.History.Count);
            Assert.AreEqual(ClinicalStage.Introduction, m.Session.Stages.Current);
            if (door) Assert.IsFalse(door.IsOpen, "door closed and latched after restart");
        }
    }
}
