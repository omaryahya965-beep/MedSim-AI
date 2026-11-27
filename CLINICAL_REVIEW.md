# Clinical Review Record — ER_CHEST_PAIN_001

**Current status: DRAFT — NOT CLINICALLY VALIDATED**

The scenario was authored by the software team for technical demonstration only. No clinician has reviewed it. No reviewer names, credentials or approvals have been invented.

## Review gate (code-enforced)
`ScenarioData.IsClinicallyValidated` is true only if `review.status == "APPROVED"` **and** reviewer name, credentials and date are all filled in. Until then the menu board, tablet header, posters and debrief show the DRAFT banner. The rule is covered by the test `ReviewGate_RequiresReviewerFieldsForApproval`.

## Items for the reviewer to check
1. History content and wording (`historyFacts`), for plausibility and consistency.
2. Vital signs and auscultation findings.
3. ECG description and the simulated tracing (`ECGTracingRenderer`, key `ecg_inferior_ste`): ST changes are schematic. Confirm they are acceptable for teaching or replace them with an approved tracing image.
4. Troponin handling message ("do not delay").
5. Rubric items, points, safety-critical flags, and the 10-minute ECG expectation.
6. Hints and supervisor lines; the debrief teaching points.
7. References (see MEDICAL_REFERENCES.md): applicability and currency.
8. Escalation keywords that count as "escalated".

## Approval fields
| Field | Value |
|---|---|
| Reviewer name | |
| Professional credentials / registration | |
| Institution | |
| Review date | |
| Scenario version reviewed | 0.1.0 |
| Decision (Approved / Changes required) | |
| Required changes | |
| Signature | |

After approval, set the `review` block in the JSON, bump `version`, and re-run the tests and `RenderVoices.ps1` if any text changed.

## Additional review items (Phase 2)
9. **Arabic translation** (`Resources/Scenarios/ER_CHEST_PAIN_001.ar.json`, status `DRAFT_TRANSLATION_NOT_REVIEWED`): needs review by a bilingual clinician. After review, set its status to `APPROVED_TRANSLATION`.
10. **Onboarding lines** and the **patient chart** text (triage category 2, "allergies to be confirmed").
11. **AI-intent grading:** decide whether history items recognised only through AI intent (labelled in the debrief) should count towards the score.
