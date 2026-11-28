# AI architecture (surgical module)

```
Student voice ──► SurgeryVoiceInput (PTT T / A,X · hands-free M · barge-in)
                    │ PCM16 24 kHz
                    ▼
          SurgeryLiveClient ──► proxy /v1/surgery/voice  (same proxy, key, ledger as the clinical module)
                                   Realtime understanding per character (heard + structured intent)
                                   intent constrained to the action catalogue; reply grounded or replaced
                    ◄── header {heard, intent, deferred, text} + streamed speech (if not deferred)
                    │
                    ▼
  SurgerySessionController ──► SurgerySessionCore (engine-free, authoritative for AI behaviour)
     │                               ├─ SurgeryActionValidator  (catalogue, ownership, instrument, availability, policy)
     │                               ├─ SurgicalSupervisor      (authored rules, grace, cooldown, hint ladder)
     │                               ├─ HandoffTracker          (completed only on verified tool.grabbed)
     │                               └─ SurgicalAssessmentEngine + SurgicalDebriefComposer
     │                                         ▲
     │          SurgeryEventBus (Shared) ──────┘◄── Mechanics (Person 3) publish gameplay events, source "mechanics"
     ▼
  SurgeryCharacterActuator ──► SurgicalCharacter (NavMesh, look-at, hold/offer) ── CharacterSpeechController (existing)
  SurgeryHud (banner, objective, transcript RTL, pause, debrief)
```

## Authority

| Decision | Who decides |
|---|---|
| Is an interaction valid / an objective complete / anatomy state | **Mechanics** (Person 3), via events |
| What the surgeon says and when | **SurgicalSupervisor**: authored rules + approved text. The AI may only rephrase under grounding |
| Whether a character action happens | **SurgeryActionValidator** (deterministic), then the actuator reports the real outcome |
| Points | **SurgicalAssessmentEngine**: mechanics events only |
| Understanding speech, phrasing | AI (proxy), constrained and validated |

The AI cannot award points, complete objectives, change anatomy or vitals, invent findings, override interaction restrictions, or run code. It proposes one catalogue intent; everything else is deterministic.

## Proxy routes (`Backend/medsim-proxy/lib/surgery.mjs`)

| Route | Purpose |
|---|---|
| `POST /v1/surgery/session/start` / `end` | Separate surgical session (unknown or clinical scenario ids → 404) |
| `POST /v1/surgery/voice?character=surgeon\|assistant` | Native audio turn. Same framing as `/v1/voice` (JSON line + PCM) |
| `POST /v1/surgery/turn` | Typed turn (tests / fallback) |
| `POST /v1/surgery/say-stream`, `/say` | Character-initiated line. Base text must be grounded in the scenario. `approvedOnly` = verbatim. Otherwise rephrased, and the rephrase must pass grounding against the base text |
| `POST /v1/surgery/event` | Ids-only gameplay context (objective, completed, holding, recent) |
| `POST /v1/surgery/action-result` | What really happened (completed, unavailable, handoff_completed, handoff_not_taken…) |

**Deferred replies:** when the student asks for an action, help or an explanation, the proxy returns `deferred: true` and voices nothing. Unity validates and speaks the approved line (action acknowledgement, hint, explanation), so speech can never contradict the simulation (e.g. "here you go" when the student already holds the tool).

**Grounding:** the shared `validateGrounded` (numbers, clinical lexicon, dosing, citations, diagnosis), plus a surgical lexicon (bone, femur, implant, artery, nerve…) and a meta block (AI, prompt). Debrief numbers must match the session's own counts (`facts`).

## Action catalogue

Assistant: `come_to_student`, `stand_beside_student`, `go_to_instrument_table`, `hand_instrument`, `locate_instrument`, `return_to_position`, `wait`, `look_at_student`.
Surgeon: `come_to_student`, `step_back`, `return_to_observation`, `look_at_student`, `look_at_site`, `explain_objective`, `wait`.
Instrument requests go to the assistant even when the doctor is addressed. An unnamed instrument → "Which instrument?". "The one I need" → the surgeon's hint (counted as a hint). An ambiguous name ("guide") → clarification. Held, in use, missing, or not allowed by policy → declined with an approved line.

## Supported requests (tested)

«دكتور، ممكن تساعدني؟» (help) · «دكتور، تعال هون شوي.» (come) · «ممكن تعطيني الأداة؟» (clarify which) · «وين الأداة اللي بحتاجها؟» (supervisor hint) · «ممكن توضحلي المطلوب؟» (explain) · «استنى شوي.» (wait) · «احكي عربي.» (language lock) · "Please move beside me." · "Can you pass me the instrument?" / "…the scalpel?"

## Failure behaviour

Proxy down / budget reached → text-only subtitles with approved lines, clearly labelled. Voice input asks the student to use H/J. Subscriber exceptions on the event bus are caught, so an AI or UI failure can't corrupt the log or the mechanics (tested).
