# Clinical Scenario — ER_CHEST_PAIN_001 v0.1.0

**Status: DRAFT — NOT CLINICALLY VALIDATED.** Authoritative source: `Assets/MedSimAI/Resources/Scenarios/ER_CHEST_PAIN_001.json`.

## Case
Mr. Adam Reyes is a fictional 58-year-old bus driver. He has had central, heavy, crushing chest pain for about 45 minutes. It began while he was carrying shopping upstairs. It radiates to the left arm and jaw, he rates it 8/10, and it has not eased with rest. He also has sweating, nausea and mild breathlessness. Over recent weeks he has had brief exertional tightness.

- **Past medical history:** hypertension, hypercholesterolaemia.
- **Medications:** amlodipine, atorvastatin.
- **Allergy:** penicillin (rash).
- **Social:** smokes 20/day for 30 years; occasional alcohol; lives with his wife.
- **Family history:** father had a myocardial infarction at 55.

## Observations (fixed)
HR 98 (sinus) · BP 152/94 · SpO₂ 96% on room air · RR 20 · Temp 37.0 °C. The monitor shows HR and RR on arrival. SpO₂ and NIBP appear only after the student measures them.

## Examination
- **Heart (aortic, pulmonary, tricuspid and mitral zones):** S1 and S2 present, regular, no added sounds.
- **Lungs (left and right zones):** vesicular breath sounds, no crackles or wheeze.

## Investigations
| Investigation | Result |
|---|---|
| 12-lead ECG | Sinus ~98/min. ST elevation in II, III and aVF; reciprocal ST depression in I and aVL. *Simulated educational tracing.* |
| hs-troponin | Sent; result not available within the simulation (do not delay). |
| Chest X-ray | No acute abnormality; mediastinum not widened (simulated report). |
| Glucose | 6.2 mmol/L |
| Echocardiogram | Not available in this scenario |

## Educational working diagnosis (debrief only)
Inferior STEMI. The expected action is urgent senior or cardiology escalation for reperfusion per the local pathway. No drug dosing is taught in v0.1.

## Objectives and rubric
There are 7 objectives and 21 rubric items, each tagged *observable* or *communication*. Three items are safety-critical: allergies, ECG, and escalation. There is one timed expectation: the ECG within 600 s of the session starting, which is the 10-minute guideline target. Speed is not rewarded otherwise.

## State machine
Introduction → {History, Initial assessment, Examination, Investigations in any order} → Clinical reasoning → Supervisor feedback → Debriefing. Patient condition changes only via authored `transitions` (v0.1: escalation acknowledgement, no vital-sign change).
