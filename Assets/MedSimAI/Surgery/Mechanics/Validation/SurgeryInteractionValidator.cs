using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Integration (2026-11-28): progress inside the CURRENT objective, so multi-step objectives authored in
    /// OR_TKA_TRAINING_001 are not completed by their first valid contact. Simulation sequencing only (DRAFT):
    ///   * resection objectives (cutting guide + oscillating saw): guide on the target first (guide_placed), then the
    ///     powered saw (resected, completes). Saw before guide -> prerequisite_missing.
    ///   * placement objectives (implantId): every component of the same kind (trial / final) whose seat is one of the
    ///     objective's targets must be seated within tolerance; the tibial insert needs the tray first. Objectives that
    ///     also list impaction tools complete when one of them is applied to the target after all components are seated.
    /// </summary>
    public sealed class ObjectiveProgress
    {
        public readonly HashSet<string> GuidedTargets = new HashSet<string>();
        public readonly HashSet<string> PlacedImplants = new HashSet<string>();
        public void Clear() { GuidedTargets.Clear(); PlacedImplants.Clear(); }
    }

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


        static readonly string[] AllImplants = { ImplantIds.FemoralTrial, ImplantIds.TibialTrial, ImplantIds.FemoralComponent, ImplantIds.TibialTray, ImplantIds.PolyInsert };
        public static bool IsCuttingGuide(string toolId) => toolId == InstrumentIds.FemoralCuttingGuide || toolId == InstrumentIds.TibialCuttingGuide;
        public static bool IsTrial(string implantId) => implantId == ImplantIds.FemoralTrial || implantId == ImplantIds.TibialTrial;
        public static string TargetOfImplant(string implantId) =>
            implantId == ImplantIds.FemoralTrial || implantId == ImplantIds.FemoralComponent ? AnatomyTargetIds.DistalFemur
            : implantId == ImplantIds.TibialTrial || implantId == ImplantIds.TibialTray || implantId == ImplantIds.PolyInsert ? AnatomyTargetIds.ProximalTibia : "";
        /// <summary>Components the objective needs seated (same kind as objective.implantId, seat in objective.targets).</summary>
        public static string[] ComponentsFor(SurgicalObjective o)
        {
            if (o == null || string.IsNullOrEmpty(o.implantId)) return Array.Empty<string>();
            bool trial = IsTrial(o.implantId);
            var list = AllImplants.Where(x => IsTrial(x) == trial && o.targets != null && o.targets.Contains(TargetOfImplant(x))).ToList();
            if (!list.Contains(o.implantId)) list.Insert(0, o.implantId);
            return list.ToArray();
        }
        static bool IsSequencedResection(SurgicalObjective o) =>
            o.requiredTools != null && o.requiredTools.Any(IsCuttingGuide) && o.requiredTools.Contains(InstrumentIds.OscillatingSaw);

        /// <summary>Tool contact with progress-aware sequencing (see ObjectiveProgress).</summary>
        public static ToolValidationResult ValidateToolInteraction(SurgicalObjective objective, string toolId, string targetId, bool isToolActive, bool isRightSide, ObjectiveProgress progress)
        {
            var basic = ValidateToolInteraction(objective, toolId, targetId, isToolActive, isRightSide);
            if (!basic.Success || progress == null) return basic;
            var components = ComponentsFor(objective);
            if (components.Length > 0)   // impaction after seating
                return components.All(progress.PlacedImplants.Contains)
                    ? ToolValidationResult.Valid(string.IsNullOrEmpty(objective.completionState) ? AnatomyStates.Implanted : objective.completionState, true)
                    : ToolValidationResult.Rejected(RejectReasons.PrerequisiteMissing);
            if (IsSequencedResection(objective))
            {
                if (IsCuttingGuide(toolId))
                    return progress.GuidedTargets.Contains(targetId) ? ToolValidationResult.Valid("", false) : ToolValidationResult.Valid(AnatomyStates.GuidePlaced, false);
                if (toolId == InstrumentIds.OscillatingSaw && !progress.GuidedTargets.Contains(targetId))
                    return ToolValidationResult.Rejected(RejectReasons.PrerequisiteMissing);
            }
            return basic;
        }

        /// <summary>Placement with progress: allowed components, tray-before-insert order, completion when all are seated.</summary>
        public static ImplantValidationResult ValidateImplantPlacement(SurgicalObjective objective, string implantId, Vector3 actualPos, Quaternion actualRot, Vector3 targetPos, Quaternion targetRot, ObjectiveProgress progress)
        {
            if (objective == null || string.IsNullOrEmpty(objective.id)) return ImplantValidationResult.Rejected(RejectReasons.PrerequisiteMissing, -1f, -1f);
            var components = ComponentsFor(objective);
            if (!components.Contains(implantId)) return ImplantValidationResult.Rejected(RejectReasons.WrongTool, -1f, -1f);
            if (implantId == ImplantIds.PolyInsert && components.Contains(ImplantIds.TibialTray) && (progress == null || !progress.PlacedImplants.Contains(ImplantIds.TibialTray)))
                return ImplantValidationResult.Rejected(RejectReasons.PrerequisiteMissing, -1f, -1f);
            var r = ValidateImplantPlacement(objective, objective.implantId, TargetOfImplant(implantId), actualPos, actualRot, targetPos, targetRot);
            if (!r.Success) return r;
            bool allSeated = components.All(c => c == implantId || (progress != null && progress.PlacedImplants.Contains(c)));
            bool needsImpaction = objective.requiredTools != null && objective.requiredTools.Length > 0;
            r.CompletesObjective = allSeated && !needsImpaction;
            r.NextAnatomyState = IsTrial(implantId) ? AnatomyStates.TrialFitted : "";   // final components: state changes on impaction
            return r;
        }

        /// <summary>Fallback when an objective names no completion state: the state the tool itself produces.</summary>
        public static string StateForTool(string toolId)
        {
            switch (toolId)
            {
                case InstrumentIds.SkinMarker: return AnatomyStates.Marked;
                case InstrumentIds.Scalpel: return AnatomyStates.Incised;
                case InstrumentIds.Retractor: return AnatomyStates.Exposed;
                case InstrumentIds.FemoralCuttingGuide: case InstrumentIds.TibialCuttingGuide: return AnatomyStates.GuidePlaced;
                case InstrumentIds.OscillatingSaw: return AnatomyStates.Resected;
                case InstrumentIds.Impactor: case InstrumentIds.Mallet: return AnatomyStates.Implanted;
                case InstrumentIds.NeedleDriver: case InstrumentIds.SkinStapler: return AnatomyStates.Closed;
                default: return AnatomyStates.Intact;
            }
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
                default: return StateForTool(toolId);
            }
        }
    }
}
