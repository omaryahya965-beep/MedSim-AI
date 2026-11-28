using System;
using System.Linq;
using MedSimAI.Surgery.AI;
using MedSimAI.Surgery.Shared;
using MedSimAI.Surgery.Shared.Unity;
using UnityEngine;

namespace MedSimAI.Surgery.Characters
{
    /// <summary>
    /// Executes ALREADY-VALIDATED catalogue actions on the surgeon and assistant and reports the real outcome exactly once.
    /// A hand-over reports Started when the assistant is holding the instrument out; the session core then waits for a
    /// verified tool.grabbed from mechanics before anything is called "handed over".
    /// </summary>
    public class SurgeryCharacterActuator : MonoBehaviour, ISurgeryCharacterActuator
    {
        public SurgicalCharacter surgeon, assistant;

        public SurgicalCharacter Get(string id) => id == SurgeryCharacterIds.Surgeon ? surgeon : id == SurgeryCharacterIds.Assistant ? assistant : null;

        public bool Supports(string characterId, string action)
        {
            var c = Get(characterId); if (!c) return false;
            return SurgeryActionCatalog.For(characterId).Contains(action) && action != SurgeryActionCatalog.OtherPhysical && action != SurgeryActionCatalog.None;
        }

        public void Execute(CharacterActionRequest r, Action<CharacterActionResult> onDone)
        {
            var c = Get(r.characterId);
            bool reported = false;
            void Done(ActionOutcome o, string reason = "")
            {
                if (reported) return; reported = true;
                onDone?.Invoke(new CharacterActionResult { characterId = r.characterId, action = r.action, instrumentId = r.instrumentId, outcome = o, reason = reason });
            }
            if (!c) { Done(ActionOutcome.Unavailable, "character_missing"); return; }
            var student = c.student;
            switch (r.action)
            {
                case SurgeryActionCatalog.ComeToStudent:
                    c.GoTo(c.BesideStudent(0.9f, 0.6f), ok => Done(ok ? ActionOutcome.Completed : ActionOutcome.Unavailable, ok ? "" : "no_path"), student); break;
                case SurgeryActionCatalog.StandBesideStudent:
                    c.GoTo(c.BesideStudent(0.7f, 0.2f), ok => Done(ok ? ActionOutcome.Completed : ActionOutcome.Unavailable, ok ? "" : "no_path"), student); break;
                case SurgeryActionCatalog.StepBack:
                {
                    var away = c.transform.position + (c.transform.position - (student ? student.position : c.transform.position - c.transform.forward)).normalized * 0.8f;
                    away.y = c.transform.position.y;
                    c.GoTo(away, ok => Done(ok ? ActionOutcome.Completed : ActionOutcome.Unavailable, ok ? "" : "no_path"), student); break;
                }
                case SurgeryActionCatalog.GoToInstrumentTable:
                    c.GoToAnchor(AnchorIds.AssistantAtTable, ok => Done(ok ? ActionOutcome.Completed : ActionOutcome.Unavailable, ok ? "" : "anchor_or_path_missing"), SurgeryAnchor.Find(AnchorIds.InstrumentTable)); break;
                case SurgeryActionCatalog.ReturnToPosition:
                case SurgeryActionCatalog.ReturnToObservation:
                    c.GoToAnchor(c.homeAnchor, ok => Done(ok ? ActionOutcome.Completed : ActionOutcome.Unavailable, ok ? "" : "anchor_or_path_missing"), SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR)); break;
                case SurgeryActionCatalog.LookAtStudent: c.LookAt(student); Done(ActionOutcome.Completed); break;
                case SurgeryActionCatalog.LookAtSite: c.LookAt(SurgeryAnchor.Find(AnchorIds.SurgicalSiteKneeR)); Done(ActionOutcome.Completed); break;
                case SurgeryActionCatalog.Wait: c.Interrupt(); c.LookAt(student); Done(ActionOutcome.Completed); break;
                case SurgeryActionCatalog.LocateInstrument:
                {
                    var tool = FindInstrument(r.instrumentId);
                    if (!tool) { Done(ActionOutcome.Unavailable, "instrument_not_in_scene"); break; }
                    c.GoToAnchor(AnchorIds.AssistantAtTable, ok => { c.LookAt(tool); Done(ok ? ActionOutcome.Completed : ActionOutcome.Unavailable, ok ? "" : "anchor_or_path_missing"); }, tool);
                    break;
                }
                case SurgeryActionCatalog.HandInstrument:
                {
                    var tool = FindInstrument(r.instrumentId);
                    if (!tool) { Done(ActionOutcome.Unavailable, "instrument_not_in_scene"); break; }
                    c.GoToAnchor(AnchorIds.AssistantAtTable, atTable =>
                    {
                        if (!atTable) { Done(ActionOutcome.Unavailable, "anchor_or_path_missing"); return; }
                        if (!c.Pick(tool)) { Done(ActionOutcome.Failed, "pick_failed"); return; }
                        c.GoTo(c.BesideStudent(0.55f, 0.45f), near => Done(near ? ActionOutcome.Started : ActionOutcome.Unavailable, near ? "offered" : "no_path"), student);
                    }, tool);
                    break;
                }
                default: Done(ActionOutcome.Denied, "not_supported"); break;
            }
        }

        /// <summary>The verified hand-over finished (player grabbed it): let go without moving the instrument.</summary>
        public void HandOverCompleted() { if (assistant) assistant.ReleaseToPlayer(); }

        /// <summary>The offer timed out: put the instrument back in its slot.</summary>
        public void HandOverFailed(string instrumentId)
        {
            if (!assistant || !assistant.Held) return;
            assistant.PutBack(SurgeryAnchor.Find(AnchorIds.InstrumentSlot(instrumentId)));
            assistant.GoToAnchor(assistant.homeAnchor, _ => { });
        }

        static Transform FindInstrument(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var t in FindObjectsByType<SurgeryInstrumentTag>(FindObjectsSortMode.None)) if (t.instrumentId == id) return t.transform;
            return null;
        }
    }
}
