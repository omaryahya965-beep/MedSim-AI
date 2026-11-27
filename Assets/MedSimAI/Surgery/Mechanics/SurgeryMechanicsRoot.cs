using System;
using System.Collections.Generic;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Master surgical mechanics coordinator (prefab root: SurgeryMechanics.prefab).
    /// Implements ISurgicalScenarioRuntime and IInstrumentRegistry per contract §6.5.
    /// Publishes verified gameplay events exclusively with source = "mechanics".
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [ExecuteAlways]
    public class SurgeryMechanicsRoot : MonoBehaviour, ISurgicalScenarioRuntime, IInstrumentRegistry
    {
        [Header("Subsystems")]
        [SerializeField] private SurgeryScenarioEngine scenarioEngine;
        [SerializeField] private SurgicalInstrumentController instrumentController;
        [SerializeField] private AnatomyInteractionController anatomyController;
        [SerializeField] private ImplantPlacementController implantController;
        [SerializeField] private SurgicalRobotController robotController;
        [SerializeField] private SurgeryResetController resetController;
        [SerializeField] private SurgicalDesktopInteractionAdapter desktopAdapter;
        [SerializeField] private SurgeryInteractionFeedback feedback;

        public SurgeryScenarioEngine ScenarioEngine => scenarioEngine;
        public SurgicalInstrumentController InstrumentController => instrumentController;
        public AnatomyInteractionController AnatomyController => anatomyController;
        public ImplantPlacementController ImplantController => implantController;
        public SurgicalRobotController RobotController => robotController;
        public SurgeryResetController ResetController => resetController;

        // ISurgicalScenarioRuntime implementation
        public string ScenarioId => scenarioEngine != null ? scenarioEngine.ScenarioId : SurgeryContract.SCENARIO_ID_TKA;
        public string CurrentObjectiveId => scenarioEngine != null ? scenarioEngine.CurrentObjectiveId : "";
        public bool IsFinished => scenarioEngine != null && scenarioEngine.IsFinished;
        public IReadOnlyList<string> CompletedObjectives => scenarioEngine != null ? scenarioEngine.CompletedObjectives : Array.Empty<string>();
        public bool IsObjectiveComplete(string objectiveId) => scenarioEngine != null && scenarioEngine.IsObjectiveComplete(objectiveId);

        public void ResetForNewSession()
        {
            if (resetController != null)
            {
                resetController.ExecuteReset();
            }
            else if (scenarioEngine != null)
            {
                scenarioEngine.ResetForNewSession();
            }
        }

        // IInstrumentRegistry implementation
        public IReadOnlyList<string> AvailableInstrumentIds => instrumentController != null ? instrumentController.AvailableInstrumentIds : Array.Empty<string>();
        public bool IsAvailable(string instrumentId) => instrumentController != null && instrumentController.IsAvailable(instrumentId);
        public InstrumentHolder HolderOf(string instrumentId) => instrumentController != null ? instrumentController.HolderOf(instrumentId) : InstrumentHolder.Missing;

        private void Awake()
        {
            EnsureSubsystems();
        }

        static T Ensure<T>(GameObject go, T current) where T : Component
        {
            if (current) return current;
            var found = go.GetComponentInChildren<T>(true);
            return found ? found : go.AddComponent<T>();   // explicit bool test: '??' does not see Unity fake-null
        }

        public void EnsureSubsystems()
        {
            scenarioEngine = Ensure(gameObject, scenarioEngine);
            instrumentController = Ensure(gameObject, instrumentController);
            anatomyController = Ensure(gameObject, anatomyController);
            implantController = Ensure(gameObject, implantController);
            robotController = Ensure(gameObject, robotController);
            resetController = Ensure(gameObject, resetController);
            desktopAdapter = Ensure(gameObject, desktopAdapter);
            feedback = Ensure(gameObject, feedback);
        }

    }
}
