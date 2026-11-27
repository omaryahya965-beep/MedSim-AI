using System;
using System.Collections.Generic;
using System.Linq;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Executes the authored surgical objective sequence defined in scenario JSON.
    /// Controls objective lifecycle, prerequisite enforcement, and scenario completion.
    /// Implements ISurgicalScenarioRuntime.
    /// </summary>
    public class SurgeryScenarioEngine : MonoBehaviour, ISurgicalScenarioRuntime
    {
        [Header("Scenario Configuration")]
        [SerializeField] private string scenarioResourcePath = "SurgeryScenarios/OR_TKA_TRAINING_001";
        [SerializeField] private bool autoStartFirstObjective = true;

        private SurgicalScenarioData scenarioData;
        private readonly List<string> completedObjectiveIds = new List<string>();
        private readonly List<SurgicalObjective> objectives = new List<SurgicalObjective>();
        private int currentObjectiveIndex = -1;
        private bool isFinished;

        public event Action<SurgicalObjective> OnObjectiveStarted;
        public event Action<SurgicalObjective> OnObjectiveCompleted;
        public event Action OnScenarioCompleted;

        // ISurgicalScenarioRuntime implementation
        public string ScenarioId => scenarioData != null ? scenarioData.scenarioId : SurgeryContract.SCENARIO_ID_TKA;
        public string CurrentObjectiveId => (currentObjectiveIndex >= 0 && currentObjectiveIndex < objectives.Count)
            ? objectives[currentObjectiveIndex].id
            : "";
        public bool IsFinished => isFinished;
        public IReadOnlyList<string> CompletedObjectives => completedObjectiveIds;

        public SurgicalObjective CurrentObjective => (currentObjectiveIndex >= 0 && currentObjectiveIndex < objectives.Count)
            ? objectives[currentObjectiveIndex]
            : null;

        public IReadOnlyList<SurgicalObjective> AllObjectives => objectives;

        private void Awake()
        {
            if (scenarioData == null)
            {
                LoadScenarioFromResources(scenarioResourcePath);
            }
        }

        private void Start()
        {
            if (autoStartFirstObjective && currentObjectiveIndex < 0 && objectives.Count > 0)
            {
                StartNextObjective();
            }
        }

        public void InitializeWithScenario(SurgicalScenarioData data)
        {
            scenarioData = data;
            objectives.Clear();
            completedObjectiveIds.Clear();
            currentObjectiveIndex = -1;
            isFinished = false;

            if (scenarioData?.objectives != null)
            {
                objectives.AddRange(scenarioData.objectives.OrderBy(o => o.order));
            }
        }

        public bool LoadScenarioFromResources(string path)
        {
            var textAsset = Resources.Load<TextAsset>(path);
            if (textAsset == null)
            {
                Debug.LogWarning($"[SurgeryScenarioEngine] Scenario not found at Resources/{path}");
                return false;
            }

            try
            {
                var data = JsonUtility.FromJson<SurgicalScenarioData>(textAsset.text);
                InitializeWithScenario(data);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SurgeryScenarioEngine] Failed to parse scenario JSON: {ex.Message}");
                return false;
            }
        }

        public bool IsObjectiveComplete(string objectiveId)
        {
            return completedObjectiveIds.Contains(objectiveId);
        }

        public bool ArePrerequisitesMet(SurgicalObjective objective)
        {
            if (objective == null) return false;
            if (objective.prerequisites == null || objective.prerequisites.Length == 0) return true;

            foreach (var req in objective.prerequisites)
            {
                if (!completedObjectiveIds.Contains(req))
                    return false;
            }
            return true;
        }

        public bool StartNextObjective()
        {
            if (isFinished) return false;

            int nextIndex = currentObjectiveIndex + 1;
            if (nextIndex >= objectives.Count)
            {
                isFinished = true;
                OnScenarioCompleted?.Invoke();
                return false;
            }

            var nextObj = objectives[nextIndex];
            if (!ArePrerequisitesMet(nextObj))
            {
                Debug.LogWarning($"[SurgeryScenarioEngine] Prerequisites not met for objective {nextObj.id}");
                return false;
            }

            currentObjectiveIndex = nextIndex;

            // Ordering guarantee §7.3: objective.started(X) comes before any gameplay event tagged X
            SurgeryEventBusHost.Publish(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveStarted, nextObj.id));
            OnObjectiveStarted?.Invoke(nextObj);
            return true;
        }

        public bool CompleteCurrentObjective()
        {
            var cur = CurrentObjective;
            if (cur == null || isFinished) return false;

            if (!completedObjectiveIds.Contains(cur.id))
            {
                completedObjectiveIds.Add(cur.id);
            }

            // Ordering guarantee §7.3: objective.completed(X) published at most once
            SurgeryEventBusHost.Publish(SurgeryEvent.Objective(SurgeryEventIds.ObjectiveCompleted, cur.id));
            OnObjectiveCompleted?.Invoke(cur);

            // Automatically transition to next objective
            if (currentObjectiveIndex + 1 < objectives.Count)
            {
                StartNextObjective();
            }
            else
            {
                isFinished = true;
                OnScenarioCompleted?.Invoke();
            }

            return true;
        }

        /// <summary>
        /// Restores scenario state for a new session without publishing events directly.
        /// </summary>
        public void ResetForNewSession()
        {
            completedObjectiveIds.Clear();
            currentObjectiveIndex = -1;
            isFinished = false;

            if (autoStartFirstObjective && objectives.Count > 0)
            {
                StartNextObjective();
            }
        }
    }
}
