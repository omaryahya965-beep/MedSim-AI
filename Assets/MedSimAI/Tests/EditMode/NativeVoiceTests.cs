using System.Reflection;
using System.Text;
using MedSimAI.AI;
using MedSimAI.Characters;
using MedSimAI.Clinical;
using MedSimAI.Voice;
using NUnit.Framework;
using UnityEngine;

namespace MedSimAI.Tests
{
    /// <summary>Native voice (v0.3): streamed speech buffering, the proxy's framed response, and tracking of voice turns.</summary>
    public class NativeVoiceTests
    {
        static float[] Read(StreamingVoice v, int n)
        {
            var buf = new float[n];
            typeof(StreamingVoice).GetMethod("OnRead", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(v, new object[] { buf });
            return buf;
        }
        static void Receive(VoiceStreamHandler h, byte[] data) =>
            typeof(VoiceStreamHandler).GetMethod("ReceiveData", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(h, new object[] { data, data.Length });

        [Test] public void StreamingVoice_ReassemblesSamplesSplitAcrossChunks_AndDrainsOnlyWhenComplete()
        {
            var v = new StreamingVoice();
            short a = 16384, b = -8192; // 0.5, -0.25
            var bytes = new byte[] { (byte)a, (byte)(a >> 8), (byte)b, (byte)(b >> 8) };
            v.PushPcm16(bytes, 0, 1); v.PushPcm16(bytes, 1, 2); v.PushPcm16(bytes, 3, 1); // odd split: a sample straddles chunks
            Assert.AreEqual(2f / StreamingVoice.Rate, v.ReceivedSeconds, 1e-7);
            Assert.IsFalse(v.Drained, "not drained while the stream is still open");
            var outp = Read(v, 4);
            Assert.AreEqual(0.5f, outp[0], 1e-4); Assert.AreEqual(-0.25f, outp[1], 1e-4); Assert.AreEqual(0f, outp[2]); // silence while waiting
            Assert.IsFalse(v.Drained);
            v.Complete();
            Assert.IsTrue(v.Drained, "drained once complete and everything played");
            Assert.Greater(v.FirstAudioTime, 0f);
        }

        [Test] public void StreamingVoice_Cancel_DropsBufferedAudio()
        {
            var v = new StreamingVoice();
            v.PushPcm16(new byte[9600], 0, 9600); // 0.2 s buffered
            Assert.Greater(v.BufferedSeconds, 0.19f);
            v.Cancel();
            Assert.IsTrue(v.Drained); Assert.AreEqual(0f, v.BufferedSeconds);
        }

        [Test] public void VoiceStreamHandler_ParsesHeaderSplitAcrossChunks_ThenStreamsPcm()
        {
            var v = new StreamingVoice(); string meta = null;
            var h = new VoiceStreamHandler(v, m => meta = m);
            var json = "{\"heard\":\"Are you allergic?\",\"text\":\"No allergies.\",\"source\":\"ai\"}\n";
            var all = new byte[Encoding.UTF8.GetByteCount(json) + 6];
            Encoding.UTF8.GetBytes(json, 0, json.Length, all, 0);
            int split = 10;
            var c1 = new byte[split]; System.Array.Copy(all, c1, split);
            var c2 = new byte[all.Length - split]; System.Array.Copy(all, split, c2, 0, c2.Length);
            Receive(h, c1); Assert.IsNull(meta, "header incomplete");
            Receive(h, c2);
            Assert.IsNotNull(meta);
            var r = JsonUtility.FromJson<LiveTurnResponse>(meta);
            Assert.AreEqual("Are you allergic?", r.heard); Assert.AreEqual("No allergies.", r.text);
            Assert.AreEqual(3f / StreamingVoice.Rate, v.ReceivedSeconds, 1e-7, "the 6 bytes after the newline are 3 PCM samples");
        }

        [Test] public void ApplyVoiceTurn_RunsDeterministicTracking_OnWhatWasHeard()
        {
            var s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001");
            var session = new ClinicalSession(s);
            var orch = new ConversationOrchestrator(session, new OfflineScriptedProvider(session));
            var resp = new AIResponse { live = true, speech = s.FindFact("H_ONSET").answer, usedFactIds = new[] { "H_ONSET" }, liveSource = "ai" };
            var t = orch.ApplyVoiceTurn(Persona.Patient, "When did the pain start?", resp);
            Assert.AreEqual("When did the pain start?", t.studentText, "transcript shows what the Realtime session heard");
            Assert.AreEqual(resp.speech, t.responseText, "subtitle text is the validated reply that is being spoken");
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.Fact("H_ONSET")));
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.StudentSpoke));
        }

        [Test] public void ApplyVoiceTurn_DoctorInvestigationRequest_IsRecorded()
        {
            var s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001");
            var session = new ClinicalSession(s);
            var orch = new ConversationOrchestrator(session, new OfflineScriptedProvider(session));
            var t = orch.ApplyVoiceTurn(Persona.Instructor, "Doctor Hassan, can I get an ECG for him please?",
                new AIResponse { live = true, speech = "Of course, I'll arrange it.", intent = "investigation_request", investigationId = "INV_ECG", liveSource = "ai" });
            Assert.IsTrue(session.Tracker.Has(ClinicalEvents.Inv("INV_ECG")));
            Assert.IsNotNull(t.investigation);
        }

        [Test] public void ApplyVoiceTurn_DosingReply_IsReplacedByApprovedLine()
        {
            var s = ScenarioData.LoadFromResources("ER_CHEST_PAIN_001");
            var session = new ClinicalSession(s);
            var orch = new ConversationOrchestrator(session, new OfflineScriptedProvider(session));
            var t = orch.ApplyVoiceTurn(Persona.Instructor, "what dose?", new AIResponse { live = true, speech = "Give 300 mg of aspirin.", liveSource = "ai" });
            Assert.AreEqual(s.instructorLines.outOfScope, t.responseText);
        }
    }
}
