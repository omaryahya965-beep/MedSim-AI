# Surgical assessment and debrief

Code: `Assets/MedSimAI/Surgery/AI/Core/SurgicalAssessment.cs` (engine-free). Criteria are authored in the scenario JSON (`assessment[]`).

## Evidence rule

Only **gameplay events published by mechanics** (`source = "mechanics"`) earn credit: `objective.*`, `tool.*`, `anatomy.*`, `implant.*`, `interaction.rejected`. Events from `ai`, `assistant`, `ui` or `mock` never score. Hints and interventions are counted from the supervisor's own `hint.given` / `supervisor.intervened` events. A student saying "I made the incision" earns nothing (test `VerbalOrAIClaims_EarnNothing`). Gameplay-shaped events from other sources are counted as "unverified, not scored" in the report.

## Criterion kinds

| Kind | Met when |
|---|---|
| `objective_completed` | mechanics published `objective.completed` for it |
| `objective_clean` | completed **and** no `interaction.rejected` in that objective |
| `placement_within_tolerance` | `implant.placement_completed` with position/angle error within the objective's tolerances. **Not assessed** (excluded from the maximum) if mechanics don't measure accuracy |
| `prerequisites_respected` | no `prerequisite_missing` rejection and no objective completed before its prerequisites |
| `hints_at_most` / `interventions_at_most` | session completed **and** count ≤ threshold (zero hints in an unfinished session is not an achievement) |
| `session_completed` | all objectives completed (mechanics) and `session.completed` |
| `duration_at_most` | session completed within the threshold |

Score = sum of weights met / sum of weights assessed.

## Report contents (written, en/ar)

Score and percentage, time, hints, interventions, valid and invalid interactions, completed and missed objectives, every criterion with its note, scenario-defined safety observations (wrong side, non-sterile, out of order, out of tolerance; plus invalid attempts in safety-critical steps), strengths, areas for improvement, the clinical-validation disclaimer, and "not a surgical competency certification". In mock mode the debrief is prefixed with a mock warning.

**Spoken debrief:** `SurgicalDebriefComposer.SpokenBase` (deterministic, used offline) is sent to the proxy with id-level `facts`. The AI may rephrase it, but any number or surgical term not in the base text is rejected and the base text is spoken.

**AI-interpreted communication** (help requests, language) is reported separately and never scored.
