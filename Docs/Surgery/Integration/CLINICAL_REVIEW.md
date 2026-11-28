# Clinical review status — OR_TKA_TRAINING_001

**Status: DRAFT — NOT CLINICALLY VALIDATED.** `review.status = "DRAFT"`, reviewer empty. The validator refuses `APPROVED` without a named reviewer, and the UI shows the banner on the scenario card, in the OR and in the debrief.

## What the scenario contains

Nine simplified, simulation-level steps on the patient's right knee: site confirmation → skin incision → exposure → femoral preparation (guide, then saw) → tibial preparation → trial components → femoral component → tibial tray and insert → closure. Each step has en/ar title, explanation and an ordered hint ladder. Tolerances (2–4 mm / 3–4°) are **gameplay values**.

The sequence follows the four basic stages described in public patient-education material (AAOS OrthoInfo, Total Knee Replacement), plus site/side confirmation before incision (WHO Surgical Safety Checklist 2009). No content was generated as "validated procedure" by an AI model.

## Deliberately not simulated

Anaesthesia, positioning, tourniquet, ligament balancing, patellar resurfacing, cementing, implant sizing, haemostasis, antibiotic prophylaxis, counts, and any real measurement.

## Reviewer checklist (qualified orthopaedic surgeon)

- [ ] Step order and wording of every title, explanation and hint (en and ar)
- [ ] Supervisor lines and when they fire (wrong side, out of order, wrong tool, tolerance, idle, help)
- [ ] Assessment criteria and weights; safety observations
- [ ] Limitations text is sufficient
- [ ] Add an authoritative operative-technique reference
- [ ] Sign off: name, date → `review.reviewer`, `review.reviewedOn`, `review.status = "APPROVED"`

Until then, the module must not be used for real training and must not be presented as a substitute for clinical supervision or accredited surgical training.
