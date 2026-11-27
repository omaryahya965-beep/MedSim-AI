using System;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Coordinates full session reset of scenario progress, instruments, anatomy states,
    /// implants, and robotic arm without reloading the scene.
    /// Responds to session.reset events or direct invocations.
    /// </summary>
    public class SurgeryResetController : MonoBehaviour
    {
        [Header("Components to Reset")]
        [SerializeField] private SurgeryScenarioEngine scenarioEngine;
        [SerializeField] private SurgicalInstrumentController instrumentController;
        [SerializeField] private AnatomyInteractionController anatomyController;
        [SerializeField] private ImplantPlacementController implantController;
        [SerializeField] private SurgicalRobotController robotController;

        public event Action OnResetCompleted;

        private void Awake()
        {
            if (!scenarioEngine) scenarioEngine = FindFirstObjectByType<SurgeryScenarioEngine>();
            if (!instrumentController) instrumentController = FindFirstObjectByType<SurgicalInstrumentController>();
            if (!anatomyController) anatomyController = FindFirstObjectByType<AnatomyInteractionController>();
            if (!implantController) implantController = FindFirstObjectByType<ImplantPlacementController>();
            if (!robotController) robotController = FindFirstObjectByType<SurgicalRobotController>();
        }

        private void Start()
        {
            // Subscribe to session.reset on event bus if host is active
            if (SurgeryEventBusHost.Instance != null && SurgeryEventBusHost.Instance.Bus != null)
            {
                SurgeryEventBusHost.Instance.Bus.EventPublished += HandleEventPublished;
            }
        }

        private void OnDestroy()
        {
            if (SurgeryEventBusHost.Instance != null && SurgeryEventBusHost.Instance.Bus != null)
            {
                SurgeryEventBusHost.Instance.Bus.EventPublished -= HandleEventPublished;
            }
        }

        private void HandleEventPublished(SurgeryEvent e)
        {
            if (e != null && e.id == SurgeryEventIds.SessionReset)
            {
                ExecuteReset();
            }
        }

        /// <summary>
        /// Restores all mechanics state to pristine session start.
        /// Ordering guarantee §7.3: On session.reset, mechanics restore every target to intact
        /// and every tool to its slot before the next objective.started.
        /// </summary>
        public void ExecuteReset()
        {
            // 1. Reset anatomy targets to intact
            if (anatomyController != null)
            {
                anatomyController.ResetAllAnatomyViews();
            }

            // 2. Reset instruments to table slots
            if (instrumentController != null)
            {
                instrumentController.ResetAllInstruments();
            }

            // 3. Reset implants to table
            if (implantController != null)
            {
                implantController.ResetAllImplants();
            }

            // 4. Reset robot to home position
            if (robotController != null)
            {
                robotController.ResetRobot();
            }

            // 5. Reset scenario engine (will start first objective)
            if (scenarioEngine != null)
            {
                scenarioEngine.ResetForNewSession();
            }

            OnResetCompleted?.Invoke();
        }
    }
}
