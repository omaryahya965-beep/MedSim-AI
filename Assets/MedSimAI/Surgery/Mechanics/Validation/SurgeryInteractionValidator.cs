using System;
using System.Linq;
using MedSimAI.Surgery.Shared;
using UnityEngine;

namespace MedSimAI.Surgery.Mechanics
{
    public struct ToolValidationResult
    {
        public bool Success;
        public string Reason;
        public string NextAnatomyState;
        public bool CompletesObjective;

        public static ToolValidationResult Valid(string nextState, bool completesObj = true) =>
            new ToolValidationResult { Success = true, Reason = "", NextAnatomyState = nextState, CompletesObjective = completesObj };

        public static ToolValidationResult Rejected(string reason) =>
            new ToolValidationResult { Success = false, Reason = reason, NextAnatomyState = "", CompletesObjective = false };
    }

    public struct ImplantValidationResult
    {
        public bool Success;
        public string Reason;
        public float PositionErrorMm;
        public float AngleErrorDeg;
        public string NextAnatomyState;
        public bool CompletesObjective;

        public static ImplantValidationResult Valid(float posErr, float angErr, string nextState) =>
            new ImplantValidationResult { Success = true, Reason = "", PositionErrorMm = posErr, AngleErrorDeg = angErr, NextAnatomyState = nextState, CompletesObjective = true };

        public static ImplantValidationResult Rejected(string reason, float posErr, float angErr) =>
            new ImplantValidationResult { Success = false, Reason = reason, PositionErrorMm = posErr, AngleErrorDeg = angErr, NextAnatomyState = "", CompletesObjective = false };
    }

    /// <summary>
    /// Pure deterministic rule validator for surgical gameplay interactions.
    /// Ensures only valid instruments, targets, power states, and implant alignments
    /// advance simulation state, strictly enforcing the authored scenario contract.
    /// </summary>
    public static class SurgeryInteractionValidator
    {
        /// <summary>
        /// Validates tool contact with an anatomical target against the active objective.
        /// </summary>
        public static ToolValidationResult ValidateToolInteraction(
            SurgicalObjective objective,
            string toolId,
            string targetId,
            bool isToolActive,
            bool isRightSide = true)
        {
            if (objective == null || string.IsNullOrEmpty(objective.id))
                return ToolValidationResult.Rejected(RejectReasons.PrerequisiteMissing);

            // 1. Surgical side safety check (TKA is right knee)
            if (!isRightSide)
                return ToolValidationResult.Rejected(RejectReasons.WrongSide);

            // 2. Validate tool requirement
            bool toolRequired = objective.requiredTools != null && objective.requiredTools.Contains(toolId);
            if (!toolRequired)
            {
                // Special check: cutting guide placed first on femur/tibia, or mallet/impactor pairing
                return ToolValidationResult.Rejected(RejectReasons.WrongTool);
            }

            // 3. Validate target region
            bool targetMatches = objective.targets != null && objective.targets.Contains(targetId);
            if (!targetMatches)
                return ToolValidationResult.Rejected(RejectReasons.WrongTarget);

            // 4. Validate power / active requirement (e.g. oscillating saw or drill must be active)
            if (RequiresPowerActivation(toolId) && !isToolActive)
                return ToolValidationResult.Rejected(RejectReasons.ToolNotActive);

            // Valid interaction: resolve next anatomy state
            string nextState = ResolveNextState(objective, toolId, targetId);
            return ToolValidationResult.Valid(nextState, completesObj: true);
        }

        /// <summary>
        /// Validates implant positioning and orientation against authored target anchor.
        /// </summary>
        public static ImplantValidationResult ValidateImplantPlacement(
            SurgicalObjective objective,
            string implantId,
            string targetId,
            Vector3 actualPos,
            Quaternion actualRot,
            Vector3 targetPos,
            Quaternion targetRot,
            float toleranceMmOverride = -1f,
            float toleranceDegOverride = -1f)
        {
            if (objective == null || string.IsNullOrEmpty(objective.id))
                return ImplantValidationResult.Rejected(RejectReasons.PrerequisiteMissing, -1f, -1f);

            // Check implant identifier matches objective if specified
            if (!string.IsNullOrEmpty(objective.implantId) && !string.Equals(objective.implantId, implantId, StringComparison.OrdinalIgnoreCase))
            {
                return ImplantValidationResult.Rejected(RejectReasons.WrongTool, -1f, -1f);
            }

            // Check target region
            if (objective.targets != null && objective.targets.Length > 0 && !objective.targets.Contains(targetId))
            {
                return ImplantValidationResult.Rejected(RejectReasons.WrongTarget, -1f, -1f);
            }

            // Calculate errors
            float distMeters = Vector3.Distance(actualPos, targetPos);
            float posErrMm = distMeters * 1000f;
            float angleErrDeg = Quaternion.Angle(actualRot, targetRot);

            float maxMm = toleranceMmOverride >= 0 ? toleranceMmOverride : (objective.toleranceMm > 0 ? objective.toleranceMm : 4f);
            float maxDeg = toleranceDegOverride >= 0 ? toleranceDegOverride : (objective.toleranceDeg > 0 ? objective.toleranceDeg : 4f);

            if (posErrMm > maxMm || angleErrDeg > maxDeg)
            {
                return ImplantValidationResult.Rejected(RejectReasons.OutOfTolerance, posErrMm, angleErrDeg);
            }

            string nextState = string.IsNullOrEmpty(objective.completionState) ? AnatomyStates.Implanted : objective.completionState;
            return ImplantValidationResult.Valid(posErrMm, angleErrDeg, nextState);
        }

        public static bool RequiresPowerActivation(string toolId)
        {
            return toolId == InstrumentIds.OscillatingSaw || toolId == InstrumentIds.Drill;
        }

        public static string ResolveNextState(SurgicalObjective objective, string toolId, string targetId)
        {
            if (!string.IsNullOrEmpty(objective.completionState))
                return objective.completionState;

            switch (objective.id)
            {
                case "obj.site_confirmation": return AnatomyStates.Marked;
                case "obj.incision": return AnatomyStates.Incised;
                case "obj.exposure": return AnatomyStates.Exposed;
                case "obj.femoral_resection":
                case "obj.tibial_resection":
                    return toolId == InstrumentIds.OscillatingSaw ? AnatomyStates.Resected : AnatomyStates.GuidePlaced;
                case "obj.trial_reduction": return AnatomyStates.TrialFitted;
                case "obj.implant_femoral":
                case "obj.implant_tibial": return AnatomyStates.Implanted;
                case "obj.closure": return AnatomyStates.Closed;
                default: return AnatomyStates.Intact;
            }
        }
    }
}
