using System.Linq;
using MedSimAI.Surgery.Shared;
using NUnit.Framework;

namespace MedSimAI.Surgery.Tests
{
    public class SurgicalScenarioTests
    {
        SurgicalScenarioData s;
        [SetUp] public void SetUp() => s = TestScenarioLoader.LoadTka();

        [Test] public void Tka_PassesValidation() => CollectionAssert.IsEmpty(SurgicalScenarioValidator.Validate(s));
        [Test] public void Tka_IsDraft_WithBanner()
        {
            Assert.AreEqual("DRAFT", s.review.status); Assert.IsFalse(s.IsClinicallyValidated);
            Assert.AreEqual(SurgeryContract.VALIDATION_BANNER, s.ValidationBanner("en"));
            Assert.AreEqual(SurgeryContract.VALIDATION_BANNER_AR, s.ValidationBanner("ar"));
        }
        [Test] public void Tka_IsIndependentFromChestPain()
        {
            Assert.AreNotEqual(SurgeryContract.CLINICAL_SCENARIO_ID, s.scenarioId);
            var json = System.IO.File.ReadAllText(TestScenarioLoader.RepoFile(TestScenarioLoader.TkaPath)).ToLowerInvariant();
            foreach (var leak in new[] { "chest pain", "troponin", "adam reyes", "sara hassan", "ecg", "ألم صدر" }) StringAssert.DoesNotContain(leak, json);
        }
        [Test] public void Tka_ObjectivesOrdered_PrerequisitesPointBackwards()
        {
            var order = s.Ordered.Select(o => o.id).ToList();
            foreach (var o in s.objectives) foreach (var p in o.prerequisites) Assert.Less(order.IndexOf(p), order.IndexOf(o.id), o.id + " <- " + p);
        }
        [Test] public void Validator_CatchesApprovedWithoutReviewer()
        {
            s.review.status = "APPROVED"; s.review.reviewer = "";
            CollectionAssert.Contains(SurgicalScenarioValidator.Validate(s), "APPROVED status requires a named reviewer");
        }
        [Test] public void Validator_CatchesUnknownTool()
        {
            s.objectives[0].requiredTools = new[] { "inst.lightsaber" };
            Assert.IsTrue(SurgicalScenarioValidator.Validate(s).Any(e => e.Contains("inst.lightsaber")));
        }
    }
}
