# Clinical review status

**DRAFT — NOT CLINICALLY VALIDATED.**
- **ER_CHEST_PAIN_001:** needs review by an emergency physician.
- **OR_TKA_TRAINING_001:** needs review by an orthopaedic surgeon. The checklists are in [`Docs/Surgery/FinalIntegration/CLINICAL_REVIEW.md`](../Surgery/FinalIntegration/CLINICAL_REVIEW.md).

Changes in this increment with possible clinical relevance (for the reviewer):
- **Blood pressure:** the measurement now needs START on the monitor, as automatic monitors do. Values are unchanged (authored).
- **Pulse oximeter:** follows the authored patient state while attached (no new values).
- **Surgery:** contact dwell (0.3 s) and tip distance (3 cm) are gameplay parameters, not clinical values.
- **AI:** outside the examination room the doctor answers general educational questions as a guide. She still does not give diagnoses, doses or management, and says when something is outside what the simulation covers.
