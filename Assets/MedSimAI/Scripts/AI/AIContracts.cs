using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MedSimAI.AI
{
    public enum Persona { Patient, Instructor }

    /// <summary>Read-only, persona-filtered view of the scenario. This is all the AI ever sees.</summary>
    [Serializable] public class PersonaContext
    {
        public Persona persona;
        public string personaName;
        public string roleDescription;
        public string stage;
        public List<AllowedFact> allowedFacts = new List<AllowedFact>();
        public List<string> forbiddenTopics = new List<string>();
        public List<string> allowedNumbers = new List<string>();
        public string unknownAnswer;
    }

    [Serializable] public class AllowedFact { public string id; public string text; }

    [Serializable] public class ConversationMessage { public string role; public string text; }

    [Serializable] public class AIRequest
    {
        public PersonaContext context;
        public string studentUtterance;
        public List<ConversationMessage> history = new List<ConversationMessage>();
        public string language = "en";
    }

    /// <summary>Structured output required from every provider.</summary>
    [Serializable] public class AIResponse
    {
        public string speech;
        public string[] usedFactIds = new string[0];
        public bool outOfScope;
        public string providerName;
        // Grounded-proxy structured fields (classification only; clinical text is rendered from approved content)
        public bool grounded;
        public string leadIn;
        public string intent;            // instructor: hint_request | investigation_request | summary_or_reasoning | question_about_case | smalltalk | out_of_scope
        public string investigationId;
        public bool mentionsEscalation;
        public string reply;
        // v0.2 Live AI (text already validated server-side; speech synthesised at runtime)
        public bool live;
        public string audioId;
        public bool escalationRequested, diagnosisNamed, isQuestion;
        public string[] concepts = new string[0];
        public string liveSource;
    }

    public enum ResponseSource { ScenarioFact, OfflineScript, AIValidated, AIRejectedFallback, SafetyGuard, ProviderFailureFallback }

    public class ConversationTurn
    {
        public Persona persona; public string studentText; public string responseText; public ResponseSource source; public string note;
        public InvestigationOutcome investigation;
        public string audioId; // runtime-generated speech to fetch from the proxy (null → text-only fallback)
    }

    public class InvestigationOutcome { public string id, name, result, imageKey; public bool available; }

    public interface IAIProvider
    {
        string Name { get; }
        bool IsOnline { get; }
        Task<AIResponse> GenerateAsync(AIRequest request, CancellationToken ct);
    }

    public interface ISpeechToTextProvider
    {
        string Name { get; }
        bool IsAvailable { get; }
        Task<string> TranscribeAsync(UnityEngine.AudioClip clip, string language, CancellationToken ct);
    }

    public interface ITextToSpeechProvider
    {
        string Name { get; }
        bool IsAvailable { get; }
        Task<UnityEngine.AudioClip> SynthesizeAsync(string text, Persona persona, string language, CancellationToken ct);
    }

    public interface IClinicalContextProvider { PersonaContext BuildContext(Persona persona); }

    public interface IResponseValidator { ValidationResult Validate(AIResponse response, PersonaContext context); }

    public interface IConversationOrchestrator
    {
        Task<ConversationTurn> HandleAsync(Persona target, string studentUtterance, CancellationToken ct);
        void CancelCurrent();
        IReadOnlyList<ConversationMessage> History(Persona p);
    }

    public struct ValidationResult { public bool valid; public string reason; }
}
