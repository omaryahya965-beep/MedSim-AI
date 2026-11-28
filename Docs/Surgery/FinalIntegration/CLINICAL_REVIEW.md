# Clinical review status: integrated hospital

**Status: DRAFT — NOT CLINICALLY VALIDATED.** Neither scenario has been reviewed by a qualified clinician. The notice is shown on the lobby directory, in Room 02 (HUD banner), in the surgical debrief and in the clinical scenario's existing UI. The integration added no clinical facts. All clinical and surgical content comes from each scenario's own authored data.

## Scenarios
| Room | Scenario | Content source | Review status | Details |
|---|---|---|---|---|
| Room 01 | `ER_CHEST_PAIN_001` (Mr. Adam Reyes, 58, chest pain) | v0.6 clinical scenario | `DRAFT`, not reviewed | Project root: `CLINICAL_SCENARIO.md`, `CLINICAL_REVIEW.md`, `MEDICAL_REFERENCES.md` |
| Room 02 | `OR_TKA_TRAINING_001` (generic right total knee arthroplasty, 9 simplified steps) | Person 4's scenario JSON. Person 2's anatomy; Person 3's tolerances | `review.status = "DRAFT"`, reviewer empty | [`Docs/Surgery/Integration/CLINICAL_REVIEW.md`](../Integration/CLINICAL_REVIEW.md) |

## What the integration changed that a reviewer should see
| Change | Clinical relevance |
|---|---|
| Surgeon persona renamed from "Dr. Lina Haddad" to Dr. Sara Hassan (the same supervising doctor as Room 01) | Wording only. The summary and welcome lines in the scenario JSON were edited to name her. No procedural text changed. |
| Mechanics now **enforce the authored step order inside each objective**: guide before saw; every component seated before impaction; tray before insert; both trials before the trial objective completes | This makes the simulation follow the scenario text that was already written. The order itself still needs review. |
| Left knee counts as **wrong side** (a `knee_l.site_marking` target was added) | Strengthens the authored side-confirmation step. The rejection and safety line already existed. |
| Hospital guide lines (`HospitalLines.cs`) | Navigation and welcome only: no clinical content, doses, findings or instructions |
| Arabic criterion notes in the surgical debrief | Translations of the existing English notes. **Need review by an Arabic-speaking clinician.** |

## Values that are illustrative, not clinical
- Placement tolerances: 2–4 mm / 3–4°. A test also uses an implant 9 mm off as out of tolerance.
- Resection planes, guide pin positions, incision length and opening size.
- Implant sizes and shapes: generic and original, not any manufacturer's design.
- Knee flexion angle (97.5°), drape layout and the room layout.
- The 24-point assessment weights.

## Not simulated
- Surgical side (as in Person 4's list): anaesthesia, positioning, tourniquet, ligament balancing, patellar resurfacing, cementing, implant sizing, haemostasis, antibiotic prophylaxis, counts, real measurement.
- Team: sterile technique, scrubbing, time-out with the full team.

## Reviewer checklist
- [ ] **Orthopaedic surgeon:** step order and wording (en/ar); supervisor triggers; tolerances; assessment criteria and weights; limitations text; an operative-technique reference; sign-off in the scenario JSON (`review.reviewer`, `review.reviewedOn`, `review.status`).
- [ ] **Emergency physician:** the v0.6 chest-pain case, per the root `CLINICAL_REVIEW.md` checklist.
- [ ] **Arabic-speaking clinician:** Arabic surgical terms in the supervisor, hint ladder and debrief notes.
- [ ] **Educator:** that guided mode vs. assessment mode match the intended teaching use, and that the score is presented as formative only.

Until it is signed off, the application must not be used for real training, presented as a substitute for clinical supervision, or used as a competency certificate.
