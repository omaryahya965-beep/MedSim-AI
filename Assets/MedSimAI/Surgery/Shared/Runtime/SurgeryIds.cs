// MedSim AI — Surgery shared contract (see Docs/Surgery/SURGERY_CONTRACT.md).
// Pure C# (no UnityEngine): every team assembly can reference this without pulling in another person's code.
// Changing or removing an existing identifier is a CONTRACT CHANGE: it needs the integration lead's approval
// and a new CONTRACT_VERSION. Adding a new identifier is allowed with a pull request into feature/surgery-complete.

namespace MedSimAI.Surgery.Shared
{
    public static class SurgeryContract
    {
        /// <summary>Bumped on any breaking change to ids, payloads or interfaces.</summary>
        public const string CONTRACT_VERSION = "1.1.0";
        public const string SCENARIO_ID_TKA = "OR_TKA_TRAINING_001";
        public const string CLINICAL_SCENARIO_ID = "ER_CHEST_PAIN_001";
        public const string VALIDATION_BANNER = "DRAFT — NOT CLINICALLY VALIDATED";
        public const string VALIDATION_BANNER_AR = "مسودة — غير معتمدة سريرياً";
    }

    /// <summary>Stable gameplay event identifiers. Person 3 (mechanics) is the only producer of the "gameplay" group.</summary>
    public static class SurgeryEventIds
    {
        // --- session lifecycle (producer: Integration / SurgerySessionController) ---
        public const string SessionStarted = "session.started";
        public const string SessionCompleted = "session.completed";
        public const string SessionReset = "session.reset";
        public const string PlayerEnteredRoom = "player.entered_room";      // producer: Environment trigger (Person 1)

        // --- gameplay (producer: Mechanics, Person 3) — the ONLY events that can earn assessment credit ---
        public const string ObjectiveStarted = "objective.started";
        public const string ObjectiveCompleted = "objective.completed";
        public const string ToolGrabbed = "tool.grabbed";
        public const string ToolReleased = "tool.released";
        public const string ToolActivated = "tool.activated";
        public const string AnatomyInteracted = "anatomy.interacted";
        public const string AnatomyStateChanged = "anatomy.state_changed";
        public const string ImplantPlacementAttempted = "implant.placement_attempted";
        public const string ImplantPlacementCompleted = "implant.placement_completed";
        public const string InteractionRejected = "interaction.rejected";

        // --- communication / AI (producer: AI + Characters, Person 4) — never earn objective credit ---
        public const string HelpRequested = "help.requested";
        public const string HintGiven = "hint.given";
        public const string SupervisorIntervened = "supervisor.intervened";
        public const string AssistantActionRequested = "assistant.action_requested";
        public const string AssistantActionCompleted = "assistant.action_completed";
        public const string AssistantActionFailed = "assistant.action_failed";
        public const string AssistantHandoffOffered = "assistant.handoff_offered";     // assistant holds the tool out
        public const string AssistantHandoffCompleted = "assistant.handoff_completed"; // ONLY after tool.grabbed by the player

        public static readonly string[] Gameplay = {
            ObjectiveStarted, ObjectiveCompleted, ToolGrabbed, ToolReleased, ToolActivated, AnatomyInteracted,
            AnatomyStateChanged, ImplantPlacementAttempted, ImplantPlacementCompleted, InteractionRejected };

        public static readonly string[] All = {
            SessionStarted, SessionCompleted, SessionReset, PlayerEnteredRoom,
            ObjectiveStarted, ObjectiveCompleted, ToolGrabbed, ToolReleased, ToolActivated, AnatomyInteracted,
            AnatomyStateChanged, ImplantPlacementAttempted, ImplantPlacementCompleted, InteractionRejected,
            HelpRequested, HintGiven, SupervisorIntervened, AssistantActionRequested, AssistantActionCompleted,
            AssistantActionFailed, AssistantHandoffOffered, AssistantHandoffCompleted };

        public static bool IsGameplay(string id) => System.Array.IndexOf(Gameplay, id) >= 0;
        public static bool IsKnown(string id) => System.Array.IndexOf(All, id) >= 0;
    }

    /// <summary>Who emitted an event. Assessment credits only <see cref="Mechanics"/> gameplay events.</summary>
    public static class SurgeryEventSources
    {
        public const string Mechanics = "mechanics";     // Person 3
        public const string Environment = "environment"; // Person 1
        public const string Integration = "integration"; // Person 4 session controller
        public const string AI = "ai";                   // Person 4 supervisor / conversation
        public const string Assistant = "assistant";     // Person 4 assistant character
        public const string UI = "ui";
        public const string Mock = "mock";               // test fixtures only; never credited in a real build
    }

    /// <summary>Instrument identifiers (prefab root carries SurgeryInstrumentTag with this id). Owner: Person 2.</summary>
    public static class InstrumentIds
    {
        public const string SkinMarker = "inst.skin_marker";
        public const string Scalpel = "inst.scalpel";
        public const string Retractor = "inst.retractor";
        public const string FemoralCuttingGuide = "inst.cutting_guide_femoral";
        public const string TibialCuttingGuide = "inst.cutting_guide_tibial";
        public const string OscillatingSaw = "inst.oscillating_saw";
        public const string Drill = "inst.drill";
        public const string Mallet = "inst.mallet";
        public const string Impactor = "inst.impactor";
        public const string NeedleDriver = "inst.needle_driver";
        public const string SkinStapler = "inst.skin_stapler";
        public const string RobotArm = "robot.arm"; // optional robotic guidance arm (see Robot prefab contract)

        public static readonly string[] All = { SkinMarker, Scalpel, Retractor, FemoralCuttingGuide, TibialCuttingGuide,
            OscillatingSaw, Drill, Mallet, Impactor, NeedleDriver, SkinStapler, RobotArm };
    }

    /// <summary>Anatomical interaction targets (collider GameObjects carry SurgeryAnatomyTag). Owner: Person 2. Operative side: patient's RIGHT knee.</summary>
    public static class AnatomyTargetIds
    {
        public const string SiteMarking = "knee_r.site_marking";
        public const string IncisionLine = "knee_r.incision_line";
        public const string JointCapsule = "knee_r.joint_capsule";
        public const string Patella = "knee_r.patella";
        public const string DistalFemur = "knee_r.distal_femur";
        public const string ProximalTibia = "knee_r.proximal_tibia";
        public const string Wound = "knee_r.wound";

        public static readonly string[] All = { SiteMarking, IncisionLine, JointCapsule, Patella, DistalFemur, ProximalTibia, Wound };
    }

    /// <summary>Anatomy state values used in anatomy.state_changed (payload.state).</summary>
    public static class AnatomyStates
    {
        public const string Intact = "intact";
        public const string Marked = "marked";
        public const string Incised = "incised";
        public const string Exposed = "exposed";
        public const string GuidePlaced = "guide_placed";
        public const string Resected = "resected";
        public const string TrialFitted = "trial_fitted";
        public const string Implanted = "implanted";
        public const string Closed = "closed";
    }

    /// <summary>Implant component identifiers. Owner: Person 2.</summary>
    public static class ImplantIds
    {
        public const string FemoralTrial = "implant.femoral_trial";
        public const string TibialTrial = "implant.tibial_trial";
        public const string FemoralComponent = "implant.femoral_component";
        public const string TibialTray = "implant.tibial_tray";
        public const string PolyInsert = "implant.poly_insert";

        public static readonly string[] All = { FemoralTrial, TibialTrial, FemoralComponent, TibialTray, PolyInsert };
    }

    /// <summary>Reasons for interaction.rejected (payload.reason). Mechanics may add new values; AI treats unknown as "invalid".</summary>
    public static class RejectReasons
    {
        public const string WrongTool = "wrong_tool";
        public const string WrongTarget = "wrong_target";
        public const string PrerequisiteMissing = "prerequisite_missing";
        public const string WrongSide = "wrong_side";
        public const string OutOfTolerance = "out_of_tolerance";
        public const string NotSterile = "not_sterile";
        public const string ToolNotActive = "tool_not_active";
    }

    /// <summary>Scene anchor names (GameObject names AND SurgeryAnchor.anchorId). Owner of each anchor in the contract doc.</summary>
    public static class AnchorIds
    {
        public const string OperatingRoomRoot = "ANCHOR_OR_Root";              // P1
        public const string PlayerSpawn = "ANCHOR_PlayerSpawn";                // P1
        public const string RoomEntry = "ANCHOR_RoomEntry";                    // P1 (trigger volume)
        public const string OperatingTable = "ANCHOR_OperatingTable";          // P1
        public const string PatientRoot = "ANCHOR_PatientRoot";                // P1 places, P2 patient prefab parents here
        public const string SurgicalSiteKneeR = "ANCHOR_SurgicalSite_Knee_R";  // P2 (child of patient prefab)
        public const string InstrumentTable = "ANCHOR_InstrumentTable";        // P1
        public const string InstrumentSlotPrefix = "ANCHOR_InstrumentSlot_";   // P2: + instrument id, e.g. ANCHOR_InstrumentSlot_inst.scalpel
        public const string ImplantTable = "ANCHOR_ImplantTable";              // P1
        public const string RobotBase = "ANCHOR_RobotBase";                    // P1
        public const string OverheadMonitor = "ANCHOR_OverheadMonitor";        // P1
        public const string SurgeonObserve = "ANCHOR_SurgeonObserve";          // P1 places, P4 uses
        public const string AssistantHome = "ANCHOR_AssistantHome";            // P1 places, P4 uses
        public const string AssistantAtTable = "ANCHOR_AssistantAtInstrumentTable"; // P1 places, P4 uses
        public const string StudentOperatingPosition = "ANCHOR_StudentOperatingPosition"; // P1

        public static string InstrumentSlot(string instrumentId) => InstrumentSlotPrefix + instrumentId;
    }

    /// <summary>
    /// Contract 1.1: accepted alternative transform names for anchors (Person 1's environment uses these names under
    /// IntegrationAnchors). SurgeryAnchor.Find resolves an id by its SurgeryAnchor component first, then by these names.
    /// </summary>
    public static class AnchorAliases
    {
        static readonly System.Collections.Generic.Dictionary<string, string[]> map = new System.Collections.Generic.Dictionary<string, string[]>
        {
            [AnchorIds.PlayerSpawn] = new[] { "PlayerSpawn" },
            [AnchorIds.PatientRoot] = new[] { "PatientRoot" },
            [AnchorIds.SurgicalSiteKneeR] = new[] { "SurgicalSite" },
            [AnchorIds.InstrumentTable] = new[] { "InstrumentTable" },
            [AnchorIds.RobotBase] = new[] { "RobotBase" },
            [AnchorIds.OverheadMonitor] = new[] { "MonitorPosition" },
            [AnchorIds.StudentOperatingPosition] = new[] { "SurgeonPosition" },   // the operator's (student's) working position, patient's right
            [AnchorIds.AssistantHome] = new[] { "AssistantPosition" },
        };
        public static string[] For(string anchorId) => map.TryGetValue(anchorId, out var a) ? a : new string[0];
    }

    /// <summary>Character identifiers for the surgical module (independent from the ER Patient/Instructor personas).</summary>
    public static class SurgeryCharacterIds
    {
        public const string Surgeon = "surgeon";
        public const string Assistant = "assistant";
    }
}
