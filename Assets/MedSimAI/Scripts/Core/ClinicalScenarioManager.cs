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
        public ClinicalSession Session { get; private set; }
        public ScenarioData Scenario => Session?.Scenario;
        public float SessionStartTime { get; private set; }
        public string SessionStartUtc { get; private set; }
        public bool Running { get; private set; }
        public float Elapsed => Running ? Time.time - SessionStartTime : frozenElapsed;
        float frozenElapsed;

        public event Action SessionReset;

        void Awake()
        {
            Instance = this;
            Session = new ClinicalSession(ScenarioData.LoadFromResources(scenarioId)) { Clock = () => Elapsed };
            if (!Scenario.IsClinicallyValidated) Debug.Log("[MedSim] " + Scenario.ValidationBanner);
        }

        public void BeginSession()
        {
            SessionStartTime = Time.time; SessionStartUtc = DateTime.UtcNow.ToString("o"); Running = true;
        }

        public void EndSession() { frozenElapsed = Elapsed; Running = false; }

        public void ResetSession()
        {
            Session.Reset(); Running = false; frozenElapsed = 0; SessionReset?.Invoke();
        }
    }
}
