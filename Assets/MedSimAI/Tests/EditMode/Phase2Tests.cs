using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MedSimAI.AI;
using MedSimAI.Assessment;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MedSimAI.Tests
{
    public class Phase2Tests
    {
        ScenarioData s; ClinicalSession session;
        [SetUp] public void SetUp() { s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001"); session = new ClinicalSession(s); }

        class FakeGrounded : IAIProvider
        {
            public AIResponse R; public string Name => "fake-grounded"; public bool IsOnline => true;
            public Task<AIResponse> GenerateAsync(AIRequest r, CancellationToken ct) => Task.FromResult(R);
        }

        // ---------- Server-style grounded classification ----------
        [Test] public async Task Grounded_DifferentPhrasings_RenderSameApprovedFact_AndAreTaggedForDebrief()
        {
            var fake = new FakeGrounded { R = new AIResponse { grounded = true, usedFactIds = new[] { "H_ONSET" } } };
            var orch = new ConversationOrchestrator(session, new OfflineScriptedProvider(session), fake) { UseOnline = true };
            var a = await orch.HandleAsync(Persona.Patient, "What time of day did you first notice it?", CancellationToken.None);
            var b = await orch.HandleAsync(Persona.Patient, "How long has this been going on?", CancellationToken.None);
            Assert.AreEqual(s.FindFact("H_ONSET").answer, a.responseText);
            Assert.AreEqual(a.responseText, b.responseText);
            var ev = session.Tracker.Log.First(e => e.eventId == "FACT:H_ONSET");
            Assert.IsTrue(ev.detail.StartsWith(ConversationOrchestrator.AiIntentTag));
            var r = AssessmentEngine.Evaluate(s, session.Tracker.Log, 0, 10);
            Assert.IsTrue(r.criteria.First(c => c.id == "R_ONSET").viaAIIntent);
        }

        [Test] public async Task Grounded_InjectedTextAndUnknownIds_AreDiscarded()
        {
            var fake = new FakeGrounded { R = new AIResponse { grounded = true, usedFactIds = new[] { "H_FAKE", "H_SMOKING" }, leadIn = "I also have a headache", speech = "I have chest pain 10/10 and diabetes" } };
            var orch = new ConversationOrchestrator(session, new OfflineScriptedProvider(session), fake) { UseOnline = true };
            var t = await orch.HandleAsync(Persona.Patient, "tell me about your habits", CancellationToken.None);
            Assert.AreEqual(s.FindFact("H_SMOKING").answer, t.responseText);
            Assert.IsFalse(session.Tracker.Has("FACT:H_FAKE"));
        }

        [Test] public async Task Grounded_Instructor_InvestigationAndEscalationIntents_UseScenarioData()
        {
            var fake = new FakeGrounded { R = new AIResponse { grounded = true, intent = "investigation_request", investigationId = "INV_ECG" } };
            var orch = new ConversationOrchestrator(session, new OfflineScriptedProvider(session), fake) { UseOnline = true };
            var t = await orch.HandleAsync(Persona.Instructor, "could we get a tracing of his heart", CancellationToken.None);
            Assert.AreEqual("INV_ECG", t.investigation.id);
            Assert.IsTrue(session.Tracker.Has("INV:INV_ECG"));
            fake.R = new AIResponse { grounded = true, intent = "smalltalk", reply = "He has a STEMI, give 300 mg aspirin." };
            var t2 = await orch.HandleAsync(Persona.Instructor, "what do you think", CancellationToken.None);
            Assert.AreEqual(ScriptedLines.InstructorGeneric, t2.responseText, "unsafe reply replaced by approved line");
            fake.R = new AIResponse { grounded = true, intent = "summary_or_reasoning", mentionsEscalation = true };
            await orch.HandleAsync(Persona.Instructor, "I would like the senior team to see him urgently because I am worried about his heart", CancellationToken.None);
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.EscalationRequested));
        }

        [Test] public void LeadIn_Validation()
        {
            Assert.IsTrue(GroundedText.ValidLeadIn("Oh, let me think."));
            Assert.IsFalse(GroundedText.ValidLeadIn("My chest hurts more"));
            Assert.IsFalse(GroundedText.ValidLeadIn("It is 8 out of 10"));
        }

        // ---------- Arabic ----------
        [Test] public void Arabic_TranslationFile_IsSeparate_Draft_AndCoversApprovedLines()
        {
            var pack = LanguagePack.Load(s, "ar");
            Assert.AreEqual("ar", pack.Language);
            Assert.IsFalse(pack.IsReviewed, "translation must not be marked approved without review");
            foreach (var f in s.historyFacts) Assert.IsTrue(pack.Has(f.answer), "missing Arabic for " + f.id);
            StringAssert.IsMatch("[؀-ۿ]", pack.Localize(s.FindFact("H_ONSET").answer));
            Assert.AreEqual("unknown English text", pack.Localize("unknown English text"), "no runtime machine translation");
        }

        [Test] public void Arabic_Shaper_ProducesContextualForms_AndLamAlef()
        {
            // "سلام": seen initial(FEB3) + lam-alef final(FEFC) + meem isolated(FEE1)
            var shaped = ArabicShaper.Shape("سلام");
            Assert.AreEqual("ﺳﻼﻡ", shaped);
            Assert.AreEqual("Hello", ArabicShaper.Shape("Hello"));
            Assert.IsTrue(ArabicShaper.Shape("بيت").All(c => c >= 'ﹰ'));
        }

        // ---------- Characters ----------
        // v0.2: the scene characters are the Rocketbox-based models (the MakeHuman prefabs are no longer used in the scene)
        [TestCase("Assets/MedSimAI/Characters/v2/Doctor/DrSaraHassan.fbx")]
        [TestCase("Assets/MedSimAI/Characters/v2/Patient/AdamReyes.fbx")]
        public void Character_IsRiggedHumanoid_WithFacialBlendShapes(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(go, path);
            var a = go.GetComponent<Animator>();
            Assert.IsTrue(a.avatar.isValid && a.avatar.isHuman, "valid humanoid avatar");
            var body = go.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh.blendShapeCount).First(); // the face/body mesh (was named "Human" in the MakeHuman export)
            var names = Enumerable.Range(0, body.sharedMesh.blendShapeCount).Select(i => body.sharedMesh.GetBlendShapeName(i)).ToList();
            foreach (var n in new[] { "eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "browInnerUp", "mouthFrownLeft", "aa_02", "p_b_m_21", "sil_00", "AA_VI_10_aa", "AA_VI_01_PP", "AA_VI_07_SS" }) CollectionAssert.Contains(names, n);
            Assert.IsTrue(go.GetComponentsInChildren<SkinnedMeshRenderer>().All(r => r.bones.Length > 0), "all garments skinned");
            var bounds = go.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            Assert.That(bounds.size.y, Is.InRange(1.55f, 1.95f), "real-world metric height");
            int verts = go.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => r.sharedMesh.vertexCount);
            Assert.Less(verts, 45000, "VR vertex budget per character");
        }

        // ---------- Characters v0.2: Animator contracts + audio lip sync ----------
        [TestCase("Assets/MedSimAI/Characters/v2/Animations/DrHassan.controller", new[] { "Speed", "Speaking", "Listening", "Mood", "Point" })]
        [TestCase("Assets/MedSimAI/Characters/v2/Animations/AdamReyes.controller", new[] { "Condition", "Speaking", "Listening", "Mood", "Point", "Wince", "HandToChest", "PainShift", "Think", "Nod" })]
        public void AnimatorController_ExposesEveryParameterTheCodeSets(string path, string[] required)
        {
            var c = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path);
            Assert.NotNull(c, path);
            var names = c.parameters.Select(p => p.name).ToList();
            foreach (var r in required) CollectionAssert.Contains(names, r);
            foreach (var layer in c.layers) foreach (var st in layer.stateMachine.states)
                if (st.state.motion is AnimationClip clip) Assert.IsTrue(clip.isHumanMotion, st.state.name + " plays a humanoid clip");
        }

        [TestCase("Assets/MedSimAI/Characters/v2/Doctor/DrSaraHassan.fbx")]
        [TestCase("Assets/MedSimAI/Characters/v2/Patient/AdamReyes.fbx")]
        public void AudioLipSync_EveryPhonemeMapsToAnExistingVisemeShape(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var body = go.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh.blendShapeCount).First();
            foreach (var kv in ULipSyncVisemeDriver.PhonemeToViseme) Assert.GreaterOrEqual(body.sharedMesh.GetBlendShapeIndex(kv.Value), 0, kv.Key + " -> " + kv.Value);
            var profile = AssetDatabase.LoadAssetAtPath<uLipSync.Profile>("Packages/com.hecomi.ulipsync/Assets/Profiles/uLipSync-Profile-Sample-Male.asset");
            Assert.NotNull(profile, "uLipSync sample profile");
            foreach (var m in profile.mfccs) if (m.name != "-") Assert.IsTrue(ULipSyncVisemeDriver.PhonemeToViseme.ContainsKey(m.name), "profile phoneme " + m.name + " is mapped");
        }

        [Test] public void FaceRig_VisemeMapping_CoversAlphabet()
        {
            foreach (var c in "abcdefghijklmnopqrstuvwxyz") CollectionAssert.Contains(FaceRig.Visemes, FaceRig.VisemeFor(c, ' '));
            Assert.AreEqual("p_b_m_21", FaceRig.VisemeFor('m', 'a'));
            Assert.AreEqual("sil_00", FaceRig.VisemeFor(' ', 'a'));
        }
    }
}
