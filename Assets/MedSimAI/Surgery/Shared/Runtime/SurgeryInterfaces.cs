using System;
using System.Collections.Generic;

namespace MedSimAI.Surgery.Shared
{
    // Minimal runtime interfaces between the four feature assemblies. Each one has a mock in Shared/Mocks so every
    // person can build and test alone. Implementations live in the owner's assembly; consumers depend ONLY on these.

    /// <summary>Implemented by Person 3 (mechanics). Read-only view of the authored objective sequence.</summary>
    public interface ISurgicalScenarioRuntime
    {
        string ScenarioId { get; }
        string CurrentObjectiveId { get; }           // "" when not started or all complete
        bool IsObjectiveComplete(string objectiveId);
        IReadOnlyList<string> CompletedObjectives { get; }
        bool IsFinished { get; }
        /// <summary>Restart the mechanics for a new session (anatomy back to intact, tools back to slots). Must publish nothing itself; the session controller publishes session.reset.</summary>
        void ResetForNewSession();
    }

    /// <summary>Implemented by Person 2/3. Where each instrument is and who holds it.</summary>
    public interface IInstrumentRegistry
    {
        IReadOnlyList<string> AvailableInstrumentIds { get; }  // present in this scene
        bool IsAvailable(string instrumentId);                 // present AND not currently held by anyone
        InstrumentHolder HolderOf(string instrumentId);
    }

    public enum InstrumentHolder { None, OnTable, Player, Assistant, OnPatient, Missing }

    /// <summary>Implemented by Person 2 on each anatomy target (visual state only; mechanics decide WHEN to change it).</summary>
    public interface IAnatomyStateView
    {
        string TargetId { get; }
        string CurrentState { get; }
        IReadOnlyList<string> SupportedStates { get; }
        /// <summary>Returns false (and changes nothing) for an unsupported state.</summary>
        bool SetState(string state);
    }

    /// <summary>Result of a validated character action, reported back to the AI so it never claims what did not happen.</summary>
    public enum ActionOutcome { Completed, Started, Denied, NeedsClarification, Unavailable, Interrupted, Failed }

    [Serializable]
    public class CharacterActionRequest
    {
        public string characterId = "";   // SurgeryCharacterIds.*
        public string action = "";        // catalogue id (see SurgeryActionCatalog in the AI assembly / contract doc)
        public string instrumentId = "";  // for hand-over / locate
        public string utterance = "";     // what the student said (for logs and clarification only)
    }

    [Serializable]
    public class CharacterActionResult
    {
        public string characterId = "";
        public string action = "";
        public string instrumentId = "";
        public ActionOutcome outcome;
        public string reason = "";        // machine reason: not_in_catalogue, instrument_unavailable, ambiguous_instrument, objective_locked, ...
        public override string ToString() => $"{characterId}:{action}({instrumentId}) → {outcome} {reason}";
    }

    /// <summary>Implemented by Person 4 characters (and by a mock). Executes ONLY already-validated requests.</summary>
    public interface ISurgeryCharacterActuator
    {
        bool Supports(string characterId, string action);
        /// <summary>Starts the physical action; calls onDone exactly once with the real outcome.</summary>
        void Execute(CharacterActionRequest request, Action<CharacterActionResult> onDone);
    }

    /// <summary>Pure clock abstraction so assessment/supervisor tests are deterministic.</summary>
    public interface ISurgeryClock { double Now { get; } }
}
