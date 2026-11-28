# Surgical interactions (Room 02)

> **Draft — not clinically validated.** Tolerances, dwell times and distances are simulation parameters.

## Objective progression
Nine authored objectives, driven only by verified mechanics events. Authored sequencing is enforced:
- guide → saw;
- every component seated → impaction;
- tray → insert;
- both trials.

Unchanged in this increment; covered by `SurgerySequencingTests` and the hospital full-scenario test.

## Intentional contact (new)
**VR contact path.** One attempt is counted only when:
- the tool is **held by the student** (XR grab);
- its **working tip** (`SurgeryInstrumentTag.tip`) is within **3 cm** of the target;
- it stays there for **0.3 s**.

It is not repeated while the tool stays in contact. Brushes with another part of the tool, quick passes, and tools that are not held are **ignored**: no anatomy change, no rejected attempt.

**Desktop** applies a tool by clicking, through the same validator. Physical contacts are ignored there, because no tool is XR-held. This also closes a gap: Unity sends trigger callbacks to disabled components, so disabling the triggers had not stopped them.

Covered by `VrContact_OnlyDeliberateTipApplication_Counts_BrushesAreIgnored`.

## Assistant hand-over (improved)
1. The request is validated.
2. Sami walks to the back table and picks the instrument when it is within reach (0.85 m).
3. He carries it to the student and holds it out when within 1.15 m.
4. The hand-over completes **only** on the student's verified `tool.grabbed`.

Failures are reported with a reason (`could_not_reach_instrument`, `could_not_reach_student`, `pick_failed`). There is no reach or grasp animation yet.

## Not done
- Progressive anatomy transitions.
- Instrument-specific grips and moving parts.
- Implant rotation control.
- LODs.
