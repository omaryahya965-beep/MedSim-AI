# Realism upgrade: implementation report (increment 1)

> **Draft — not clinically validated.** This is an educational prototype. Scores are formative feedback, not a competency certificate.

- **Branch:** `integration/surgery-final`, PR #6, not merged.
- **Unity:** 6000.3.29f1.
- **Date:** 2026-11-28.

The brief lists six priorities. This increment fixes the critical voice problems (Priority 1) end to end, the context and repetition issues of Priority 2, and a first set of practical changes in Priorities 3 and 4. **Most of Priorities 3 to 6 is not done yet.** The table says exactly what is.

## Status by priority
| Priority | Item | Status |
|---|---|---|
| 1 Voice | Audible doctor, patient and assistant speech | **Fixed and verified** in the standalone executable: fake provider in all areas; one live paid check in reception and Room 01. Root causes: [AUDIO_ROOT_CAUSE](AUDIO_ROOT_CAUSE.md). |
| 1 | Microphone, push-to-talk, hands-free | Verified (real microphone captured; hands-free keeps it open). Not tested with a human voice. |
| 1 | Student transcript in the panel | Restored in Room 02; verified in Room 01 and the corridor. Partial (streaming) transcripts and a scrolling history are **not implemented**. |
| 1 | Hospital routing and correct listener | Fixed. The patient is addressable only in Room 01. Feminine imperatives to Dr. Hassan («خديني») now work inside rooms. |
| 1 | Builds at the original locations | Done. The previous builds are kept as dated backups. One-click launchers start the proxy too. |
| 2 | The doctor knows where the student is | Done (reception, corridor, Room 01, Room 02). Outside Room 01 she acts as the guide and does not ask about a patient the student has not met. |
| 2 | No repeated introductions | Done. The hospital welcome is given once; re-entering a room gets a short welcome-back; the double introduction at the operating-room door is removed. |
| 2 | Language continuity | Room 02 now follows the student's spoken language unless a language was chosen explicitly. |
| 2 | Conversation memory across rooms | **Not done.** Each room's case has its own AI session, so the doctor does not remember corridor conversation inside a case. |
| 2 | Open-ended understanding | Unchanged. Clinical and surgical conversation was already understood by the model; hospital navigation stays deterministic on purpose. |
| 3 Clinical | Blood pressure: attach → START → inflation → authored reading | **Implemented.** The student presses START on the NIBP unit; no reading without the cuff on the arm. |
| 3 | Pulse oximeter live while attached | Implemented. Follows the authored vitals; the reading is removed when the probe is taken off. |
| 3 | Sleeve or clothing interaction, ECG electrodes, stethoscope zones and sounds, patient movement | **Not done in this increment.** The existing v0.6 behaviour is unchanged. |
| 4 Surgery | Accidental contact | **Implemented.** Only a held tool's working tip resting on the target for 0.3 s counts, once per contact; brushes are ignored and never penalised. |
| 4 | Assistant hand-over robustness | Fixed: legs are judged by reach; failures carry a reason. There is **no reach or grasp animation**. |
| 4 | Anatomy transitions, instrument-specific grips, implant handling, LODs | **Not done.** |
| 5 Visuals | Faces, hands, beds, drapes | **Not done.** The hospital signage was fixed (both room entrances are now readable from the lobby side). |
| 6 | Tests, builds, PR | EditMode 236/236 · PlayMode 45/45 · proxy 48/48. Desktop and PCVR built and deployed. PR #6 updated. |

## Commits (this increment)
| Commit | Change |
|---|---|
| `6c4d189` | Both room entrances readable from the lobby side |
| `1458f43` | One shared spending ledger for every proxy checkout |
| `e1149fc` | Voice fixes: session-end race, listener, Room 02 transcript, feminine verbs, door, stale guide action, outdated-proxy message |
| `306be3a` | Standalone voice walkthrough test and fake-provider proxy |
| `baa2658` | Location context and no repeated introductions |
| `910f044` | Live evidence |
| `47a2c9b` | Blood-pressure START and live oximetry |
| `6ed2bbf` | Only deliberate tip applications count in surgery |
| `6df1b6a` | Hand-over judged by reach; failures say why |

A final commit holds the Room 02 language fix and these documents.

## New controls
- **NIBP monitor** (on the instrument trolley): after applying the cuff, look at the monitor and press **E** or left click (VR: poke or select the device) to START.
- **Launchers** (in `D:\Medical-Test\`):
  - `Start_MedSim_AI_Desktop.cmd`
  - `Start_MedSim_AI_PCVR.cmd`
  - `Start_MedSim_Proxy_Integrated.cmd`
- **Test switches:**
  - `-hospitalVoiceTest <dir>`: needs the fake-provider proxy, and refuses to run against a paid proxy unless `-allowLive` is given.
  - `-hospitalLiveSmoke <dir> <clips>`: **paid**.
