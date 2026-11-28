using System;
using MedSimAI.Clinical;
using UnityEngine;

namespace MedSimAI.Core
{
    /// <summary>Unity owner of the authoritative ClinicalSession. Everything clinical goes through here.</summary>
    [DefaultExecutionOrder(-100)]
    public class ClinicalScenarioManager : MonoBehaviour
    {
        public static ClinicalScenarioManager Instance { get; private set; }
        [SerializeField] string scenarioId = "ER_CHEST_PAIN_001";
        [Tooltip("Scenario-time multiplier for authored time-based transitions (1 = real time). Demo/testing only.")]
        public float scenarioTimeScale = 1f;
        public ClinicalSession Session { get; private set; }
        public SupervisorDirector Director { get; private set; }
        public ScenarioData Scenario => Session?.Scenario;
        public float SessionStartTime { get; private set; }
        public string SessionStartUtc { get; private set; }
        public bool Running { get; private set; }
        public float Elapsed => Running ? (Time.time - SessionStartTime) * scenarioTimeScale + offset : frozenElapsed;
        float frozenElapsed, offset, nextTick;

        public event Action SessionReset;

        void Awake()
        {
            Instance = this;
            Session = new ClinicalSession(ScenarioData.LoadFromResources(scenarioId)) { Clock = () => Elapsed };
            Director = new SupervisorDirector(Session);
            if (!Scenario.IsClinicallyValidated) Debug.Log("[MedSim] " + Scenario.ValidationBanner);
        }

        public void BeginSession()
        {
            SessionStartTime = Time.time; SessionStartUtc = DateTime.UtcNow.ToString("o"); Running = true; offset = 0; nextTick = 0;
        }

        /// <summary>Testing/demo hook: advances scenario time (e.g. to exercise authored deterioration without waiting).</summary>
        public void AdvanceScenarioTime(float seconds) { offset += seconds; Tick(); }

        public void EndSession() { frozenElapsed = Elapsed; Running = false; }

        public void ResetSession()
        {
            Session.Reset(); Director.Reset(); Running = false; frozenElapsed = 0; offset = 0; SessionReset?.Invoke();
        }

        void Update()
        {
            if (!Running || Time.time < nextTick) return;
            nextTick = Time.time + (Scenario.supervisorPolicy?.tickSeconds ?? 0.5f);
            Tick();
        }

        void Tick() { Session.Tick(); if (!PauseCoaching) Director.Tick(); }
        /// <summary>Set while the doctor is demonstrating: she is already teaching, so the supervisor's coaching rules wait.</summary>
        public static bool PauseCoaching;
    }
}
