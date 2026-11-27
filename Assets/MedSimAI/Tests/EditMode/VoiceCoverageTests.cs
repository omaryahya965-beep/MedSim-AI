using System.Linq;
using MedSimAI.AI;
using MedSimAI.Clinical;
using NUnit.Framework;
using UnityEngine;

namespace MedSimAI.Tests
{
    public class VoiceCoverageTests
    {
        [Test] public void EveryApprovedOfflineLine_HasPrerenderedVoice()
        {
            var s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001");
            var lib = new PrerenderedVoiceLibrary();
            var missing = ScriptedLines.Distinct(s)
                .Where(x => lib.SynthesizeAsync(x.text, x.persona, "en", default).Result == null)
                .Select(x => $"{x.persona}: {x.text}").ToList();
            CollectionAssert.IsEmpty(missing, "Re-run AgentScripts/RenderVoices.ps1 after changing scenario text");
        }

        [Test] public void OfflinePatientAnswers_AreAllInApprovedCatalog()
        {
            var s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001");
            var session = new ClinicalSession(s);
            var offline = new OfflineScriptedProvider(session);
            var ctx = new ClinicalContextProvider(session).BuildContext(Persona.Patient);
            var approved = ScriptedLines.Distinct(s).Where(x => x.persona == Persona.Patient).Select(x => x.text).ToHashSet();
            foreach (var q in new[] { "When did it start?", "Do you smoke?", "What's your favourite film?", "Any allergies?", "Does it spread to your arm?" })
                Assert.IsTrue(approved.Contains(offline.GenerateAsync(new AIRequest { context = ctx, studentUtterance = q }, default).Result.speech), q);
        }
    }
}
