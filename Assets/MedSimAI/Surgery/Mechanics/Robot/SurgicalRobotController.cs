using System;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    /// <summary>
    /// Controlled educational robotic guidance arm (contract §6.4 and §12).
    /// Provides visual workspace guidance, joint articulation, and emits tool.activated.
    /// Does not claim clinical equivalence or real-world surgical robot control.
    /// </summary>
    public class SurgicalRobotController : MonoBehaviour
    {
        [Header("Robot Configuration")]
        [SerializeField] private string toolId = InstrumentIds.RobotArm;
        [SerializeField] private Transform baseTransform;
        [SerializeField] private Transform endEffector;
        [SerializeField] private Transform[] joints;
        [SerializeField] private float workspaceRadius = 1.2f;

        private Vector3 initialBasePos;
        private Quaternion initialBaseRot;
        private Quaternion[] initialJointRots;
        private bool isGuidanceActive;

        public bool IsGuidanceActive => isGuidanceActive;

        private void Awake()
        {
            if (baseTransform == null) baseTransform = transform;

            initialBasePos = baseTransform.position;
            initialBaseRot = baseTransform.rotation;

            if (joints != null && joints.Length > 0)
            {
                initialJointRots = new Quaternion[joints.Length];
                for (int i = 0; i < joints.Length; i++)
                {
                    if (joints[i] != null) initialJointRots[i] = joints[i].localRotation;
                }
            }

            // Snap base to anchor if available
            var baseAnchor = SurgeryAnchor.Find(AnchorIds.RobotBase);
            if (baseAnchor != null)
            {
                baseTransform.position = baseAnchor.position;
                baseTransform.rotation = baseAnchor.rotation;
            }
        }

        public void ActivateGuidance()
        {
            isGuidanceActive = true;
            SurgeryEventBusHost.Publish(SurgeryEvent.Tool(SurgeryEventIds.ToolActivated, toolId));
        }

        public void DeactivateGuidance()
        {
            isGuidanceActive = false;
        }

        public bool IsWithinWorkspace(Vector3 position)
        {
            Vector3 origin = baseTransform ? baseTransform.position : transform.position;
            return Vector3.Distance(origin, position) <= workspaceRadius;
        }

        public void ResetRobot()
        {
            isGuidanceActive = false;

            if (baseTransform != null)
            {
                var baseAnchor = SurgeryAnchor.Find(AnchorIds.RobotBase);
                if (baseAnchor != null)
                {
                    baseTransform.position = baseAnchor.position;
                    baseTransform.rotation = baseAnchor.rotation;
                }
                else
                {
                    baseTransform.position = initialBasePos;
                    baseTransform.rotation = initialBaseRot;
                }
            }

            if (joints != null && initialJointRots != null)
            {
                for (int i = 0; i < joints.Length && i < initialJointRots.Length; i++)
                {
                    if (joints[i] != null) joints[i].localRotation = initialJointRots[i];
                }
            }
        }
    }
}
