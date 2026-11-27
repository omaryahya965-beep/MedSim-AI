# MedSim AI — Research and Visual Direction (Build Spec)

**Date:** 2026-11-27 · **Scope:** art direction, environment, characters, body language, voice, experience. No Unity file was changed.
**Labels:** **[R]** = research finding (URL given) · **[V]** = verified by me today (API query or file opened) · **[D]** = my design decision · **[C]** = needs clinician or client confirmation.
**Coordinates:** Unity world metres, existing room: x −3…+3, z −2.5…+2.5, ceiling 2.8, door centre x = 1.85 on the z = −2.5 wall, bed at (0.3, 0.9) with head to the +z wall.

---

## 0. Top 15 changes, ranked by realism gain per effort

| # | Change | Gain | Effort |
|---|---|---|---|
| 1 | Swap both characters to Rocketbox: `Medical_Female_02` (doctor) and `Male_Adult_05` (patient). Both are already staged in `AssetStaging/Rocketbox` | Very high | Low |
| 2 | Drive them with the Rocketbox mocap clips (same skeleton, MIT): idle, talk, listen, walk, turn, clipboard | Very high | Low–medium |
| 3 | Re-light: walls no brighter than sRGB 222, ceiling panels cut by about half, one warm sun through the windows, baked AO, Neutral tonemapping | High | Low |
| 4 | Delete the floating menu board, tablet pole, rusty bins, domestic drawer chest and the "CHEST PAIN" poster (it gives away the answer) | High | Very low |
| 5 | Build the 5.0 × 2.4 m corridor bay with the bilingual sign; spawn there | High | Medium |
| 6 | Hinged door with a real lever, closer and latch sound; the room is revealed through it | High | Medium |
| 7 | Patient reclined properly: backrest 40°, head on the pillow, cloth-simulated blanket folded to the waist, real gown | Very high | Medium–high |
| 8 | Window: HDRI sky plus two near exterior objects for parallax, blinds half tilted | Medium–high | Low |
| 9 | New bed model (split rails, column base, castors, pendant) | High | High |
| 10 | 13-second welcome; the doctor then stays quiet and observes from the foot of the bed | High | Low |
| 11 | Head and eye aim (Animation Rigging) plus latency-masking "thinking" behaviours | High | Medium |
| 12 | Pre-render every approved line with `marin` (doctor) and `cedar` (patient) using the instructions in §7 | High | Low |
| 13 | NIBP cuff with hose, clip oximeter with cable, ECG cart with a 10-wire lead bundle and electrodes | Medium–high | Medium |
| 14 | Move the student's working side to the patient's right; monitor to the far side **[C]** | Medium | Medium |
| 15 | Spoken 30–45 s debrief and an in-world ending; the report becomes optional | Medium | Low |

---

## 1. Research: what professional simulations do

### 1.1 Findings

| Product / study | Finding | Lesson for MedSim |
|---|---|---|
| **Oxford Medical Simulation** | **[R]** Voice control for AI-driven patients; hand control with haptics for procedures; an independent review notes scaffolding: "start with menu-based communication and then go to voice control". Physiology and "unwellness signs change over time, including respiratory rate, diaphoresis, pallor". Every action is "ranked, tracked and explained"; guided debrief. Runs with "no facilitator input". ([product page](https://oxfordmedicalsimulation.com/product/vr-medical-simulation/), [OADN review](https://www.unboundmedicine.com/oadn/view/Virtual%20Simulation%20Reviews/2455046/all/Oxford_Medical_Simulation)) | Illness is shown on the body (sweat, pallor, breathing), not in a panel. Keep the tablet only as a fallback under voice. Feedback explains why. |
| **UbiSim** | **[R]** Sells presence through small physical truths: the patient "talks to me, looks at me", "things happen when I grab his arm", heart and lung sounds are audible, learners reach for instruments. Prebrief → simulation → debrief is treated as one experience. AI dialogue was added later, after the physical room worked. ([immersive realism](https://www.ubisimvr.com/immersive-realism), [resources](https://www.ubisimvr.com/resources)) | Eye contact and a reaction to touch matter more than polygon count. The corridor welcome is our prebrief. |
| **Body Interact** | **[R]** Questions "by voice or text"; live physiology on a monitor; time pressure; a time-stamped report and an AI summary of what went well and what did not, organised by stage of care. ([simulator page](https://bodyinteract.com/virtual-patient-simulator/), [Wolters Kluwer](https://www.wolterskluwer.com/en/solutions/lippincott-medicine/medical-education/body-interact-virtual-patient-care-simulator)) | The monitor is the only clinical UI needed. Structure the debrief by stage: history, examination, ECG, escalation. |
| **SimX** | **[R]** "No drop-down menus": learners talk and act; a human moderator voices the patient and triggers events. ([platform](https://www.simxvr.com/platform/), [AMA](https://www.ama-assn.org/education/accelerating-change-medical-education/virtual-reality-taking-dummies-out-medical)) | Dr. Hassan is our in-world moderator. No menus inside the room. |
| **Health Scholars** | **[R]** The learner leads by voice and directs virtual team members; a voice-led VR ACLS refresher was comparable to high-fidelity simulation for satisfaction. ([NIST](https://www.nist.gov/news-events/news/2020/03/modernizing-first-responder-training-vr-and-speech-recognition), [JMIR 2020](https://www.jmir.org/2020/3/e17425)) | Requests ("I'd like a 12-lead ECG") go to the doctor by voice. |
| **Laerdal vSim / vrClinicals** | **[R]** Conversational AI patients, automated feedback and scoring, reflection exercises, SBAR handover tool. ([Laerdal](https://laerdal.com/products/courses-learning/virtual-simulation/vrclinicals-for-nursing/overviewpartial), [Unity case study](https://unity.com/resources/laerdal-medical)) | End with a short handover-style summary to the supervisor. |
| **MedVR Education** | **[R]** Authoring tool where educators define AI patients for conversation practice. ([HealthySim profile](https://www.healthysimulation.com/vendors/medvr-education/)) | Keep persona (how he speaks) separate from clinical facts (what is true). |
| **CLiVR** (Unity, Quest 3, LLM patient) | **[R]** Uses uLipSync and a four-state indicator (Idle, Listening, Thinking, Speaking). Mean round-trip 1.35 s. Faculty (n = 13) still named latency and unnatural speech as the main problems and asked for vitals, exam findings and more expressive avatars. ([arXiv 2510.19031](https://arxiv.org/html/2510.19031v1)) | Even 1.4 s feels slow. Mask it with behaviour (§6.4). |
| **LLM ECAs for communication training** | **[R]** Students ranked realistic scenarios first; "high-fidelity characters, objects, and animations"; keep the interface simple because most were new to VR; checklists and visible grading made them anxious and less authentic. ([arXiv 2503.01767](https://arxiv.org/html/2503.01767v1)) | No score or checklist is visible during the case. |
| **Personality-driven VR patients** | **[R]** Physicians found the talkative, anxious patient more human; terse patients "seemed more robotic" ("realism-verbosity paradox"). Only lip-sync was animated, and the authors named body language as the missing piece. ([arXiv 2509.14132](https://arxiv.org/html/2509.14132v1)) | Mr. Reyes speaks in short sentences because of pain, so his body must carry the humanity. |
| **Systematic review of LLM virtual patients** | **[R]** Hallucination is the main risk to history-taking accuracy; speech input improves immersion. ([JMIR Med Inform 2026](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC12811743/)) | Keep the existing grounding contract unchanged. |

### 1.2 What they share, and what they avoid

- **Fidelity level [R→D]:** clean, slightly idealised realism on mobile-class hardware. None is photoreal. They win with correct proportions, real equipment silhouettes, calm lighting and characters that look at you. Our target: every object identifiable by silhouette at 3 m and convincing at 0.6 m.
- **They avoid:** floating menus in the scene, score read-outs during the case, long tutorials, gore, brand logos, empty voids outside the room, and patients who do not react.
- **Not copied:** no layouts, characters, UI or audio from these products. Only general practice is taken.

---

## 2. Space A — corridor waiting bay **[D]**

### 2.1 Shell

| Item | Spec |
|---|---|
| Interior footprint | x −1.60 … +3.40, z −5.02 … −2.62 → **5.0 × 2.4 m**. The north wall is the existing door wall (0.12 thick) |
| Ceiling | **2.60 m** suspended 600 × 600 grid. Lower than the room (2.8) on purpose: the room feels like a release |
| Floor | Same sheet vinyl as the room, welded seam at the threshold, 100 mm coved skirting |
| Wall treatment | Painted above; 0.90 m wall-protection dado (PVC sheet, 30 mm cap trim) on the north and south walls |
| Enclosure | **West end (x = −1.60):** closed single door "غرفة الفحص ٢ · Exam Room 2", vision-panel blind shut. **East end (x = +3.40):** closed double fire doors (2 × 0.80 × 2.10) with frosted, softly lit vision panels, green "مخرج · EXIT" sign above, muffled ward ambience behind. Neither opens. No visible void anywhere |
| Storey | Ground floor (matches the eye-level exterior HDRI) |

### 2.2 Plan (north is +z, up the page)

```
 z=-2.62 ┌─[notice]──[ WELCOME SIGN ]──╔═ EXAM 3 ═╗[plate][san][ext]─┐  N wall
         │   -0.95        +0.50        ║1.32–2.38║ 2.56  2.80 3.15  │
         │                    D0 ●(1.30,-3.30)       ● D1(2.60,-3.20)║ E: double
 W: Exam │                                                           ║ fire doors
 Room 2  │        S ▲(0.45,-4.15) yaw 20°          sun patch         ║ + EXIT
         │  ┌─bench 1.65─┐  [leaflets]  ┌──── window 2.0 ─────┐      │
 z=-5.02 └──┴─ -0.50 ────┴───+0.55──────┴── 0.90 … 2.90 ──────┴──────┘  S wall
        x=-1.60                                                  x=+3.40
```

### 2.3 Fixtures

| Fixture | Position (x, y centre, z) | Size / note |
|---|---|---|
| **Welcome sign** | (0.50, 1.60, −2.62) north wall | 1.20 × 0.45 m, 6 mm matte acrylic on 4 stand-offs. Arabic on top: «جامعة بيرزيت — أهلاً وسهلاً» (Noto Kufi Arabic, cap height 70 mm); below: "Birzeit University — Welcome" (45 mm). Dark green-grey text on off-white. No logo. Small footer: "Clinical Skills Simulation Suite · provisional signage" |
| Room plate | (2.56, 1.50, −2.62) | 0.30 × 0.12 m: «غرفة الفحص ٣ · Exam Room 3» |
| Room-in-use lamp | (1.85, 2.32, −2.62) | 60 mm dome over the frame, amber when the door is shut after entry |
| Hand sanitiser | (2.80, 1.10, −2.62) | 0.12 × 0.25 × 0.10 dispenser with drip tray; sign above «عقّم يديك · Clean your hands». Reuse the existing trigger logic |
| Fire extinguisher | (3.15, base 0.75, −2.68) | On a bracket, ID sign at 1.80. Poly Haven `korean_fire_extinguisher_01` **[V]**; replace the label texture |
| Notice board | (−0.95, 1.50, −2.62) | 0.90 × 0.60 felt board, aluminium frame, 4–5 pinned A4 sheets (rota, hand-hygiene audit, simulation etiquette, DRAFT notice) |
| Handrail | North wall, top at 0.90 | 40 mm tube, 50 mm off the wall, returns into the wall; runs x −1.5 → 1.20 and 2.50 → 3.30 |
| Beam bench | (−0.50, 0, −4.72) south wall | 3 seats, 1.65 W × 0.60 D × 0.80 H, seat height 0.44; steel beam, polypropylene shells |
| Leaflet rack | (0.55, 1.35, −5.02) | 3 × A4 acrylic pockets |
| Window | South wall, x 0.90 → 2.90, sill 0.90, head 2.10 | Two panes with mullion, roller blind a quarter down, low radiator panel 1.6 × 0.5 below |
| Ceiling | LED panels 600 × 600 at (0.0, −3.82) and (2.4, −3.82); air diffuser tile at x = 1.2; smoke detector at x = −1.0 | |
| Hanging sign | (2.75, underside 2.15, −3.82) | 0.90 × 0.22: «← الطوارئ Emergency» / «الأشعة Radiology →» |
| Optional | Folded wheelchair by the east doors (Poly Haven `wheelchair_01` **[V]**, 40k polys → decimate to 12k; check it looks modern) | |

### 2.4 Spawn and marks

| Mark | Position | Facing |
|---|---|---|
| **Student spawn S** | (0.45, 0, −4.15) | Yaw 20°. Sign is 18° left of centre, doctor 25° right, door beyond her |
| **Doctor D0** (welcome) | (1.30, 0, −3.30) | Toward the student; 1.2 m away |
| **Doctor D1** (door, hinge side) | (2.60, 0, −3.20) | Toward the lever; "after you" gesture |

---

## 3. The door **[D]**

Keep the existing opening (clear 1.06 m). Replace the leaf, frame and handle.

| Part | Spec |
|---|---|
| Leaf | 1.04 × 2.10 × 0.045 m. Light-oak HPL faces, 2 mm PVC edge guards, stainless kick plate 0.25 m high on both faces, stainless push plate on the corridor face, vision panel 0.18 × 0.75 m (centre 1.40 m high, latch side) with a 15 mm bead and a closed integral blind |
| Frame | Powder-coated steel, 50 mm face, 12 mm stop with a grey seal, 3 stainless butt hinges at 0.25 / 1.05 / 1.85 m |
| Hinge / swing | Hinged on the **east jamb (x = 2.38)**, opens **into the room**, 0–95°. Open, it lies against the right wall and never hides the patient |
| Lever | Return-to-door stainless lever, 20 mm tube, 140 mm long, on a 52 mm rose with a separate escutcheon below; axis at **1.00 m**, 60 mm backset → corridor-side grab point ≈ (1.42, 1.00, −2.66). Travel 0–35°, spring return |
| Closer | Overhead surface closer on the room face (body 0.28 × 0.06 × 0.045, scissor arm) |
| Behaviour | Grab lever → turn past 25° unlatches (click) → push; hinge resistance with light haptic ramp; the closer returns it over about 5 s after both characters are inside; soft latch click. Above 70° fires `DOOR_OPENED`. If the student fumbles for 8 s, Dr. Hassan says "Push the handle down and lean in", once |
| Tris | Leaf + frame 4–6k, lever set 1.5k, closer 0.8k |
| Performance | When the door is latched, disable the corridor renderers (the leaf is the occluder) |

---

## 4. Space B — examination room

### 4.1 Layout revisions

**[C]** Clinical convention is to examine from the patient's right. With the head at +z, the patient's right is the window side (−x). Today the instruments are on his left. I recommend moving the working zone; if the clinical reviewer does not mind, mirror the x-values below and keep the current side.

| Action | Object | New position / note |
|---|---|---|
| Keep | Bed | (0.3, 0.9). Mattress top 0.72 m, backrest **40°**, near-side (−x) rail **down**, far-side rail up |
| Move | Curtain track | L-shaped: x = −1.30 from the back wall to z = −1.00, then across to x = +1.90. Curtains fully stacked (0.35 m bundles) at the back wall and at the +x end. Nothing hangs in front of the window |
| Move | Instrument trolley | (−0.95, 1.80), long side to the back wall, within reach of the working zone |
| Move | ECG cart | (−0.85, −0.35), screen turned toward the working zone |
| Move | Monitor | Back-wall arm at x = +1.10, screen centre 1.55 m, yawed 30° toward the working zone. Cables emerge behind the headboard on the patient's right |
| Move | IV stand | (0.95, 2.10); a capped line hooked up, nothing running |
| Add | Visitor chair | (1.50, 1.60), turned 30° toward the patient. A folded jacket and a clear belongings bag (his shirt, driver's lanyard) on it. This is his wife's chair; she has gone to register him |
| Add | Doctor stool | (2.25, 0.45), parked |
| Add | Pedal bins ×2 | (2.78, 0.30) black lid "general", (2.78, −0.15) orange lid "clinical"; sharps bin stays on the trolley shelf |
| Add | Over-bed table | (1.35, −0.10), pushed aside; cardboard vomit bowl and tissues on it (he is nauseated) |
| Add | Working-zone marker | None visible. Teleport anchor at (−0.75, 0.70) facing +x |
| Remove | Menu board, tablet on pole (move to the pause menu / optional), rusty bins, wooden drawer chest, separate NIBP box, "CHEST PAIN" poster, "MEDSIM AI" poster (its DRAFT text moves to the corridor notice board) | |
| Replace | Posters | Hand hygiene (keep, bilingual) and a neutral observations chart |
| Fix | Exam lamp | Wall-mounted articulated LED head (200 mm disc) at (−0.60, 1.90, 2.44); off by default |
| Keep | Cabinets, sink, glove rack, bedhead services panel, clock, ceiling panels, window frame | Add a towel dispenser and soap pump by the sink |

**Doctor marks in the room:** D2 introduction (1.55, −1.20) → **O1 observe (1.25, −0.55)** at the foot-left corner, 2.2 m from the student with a clear line to the patient, monitor and student → O2 discussion (0.30, −1.05) at the foot centre.

### 4.2 Real-world dimensions

| Object | W × D × H (m) | Key heights |
|---|---|---|
| Hospital bed | 1.00 × 2.20 overall; mattress 0.90 × 2.00 × 0.15 | Deck 0.57, mattress top 0.72 (hi-lo range 0.40–0.80); rail top 0.38 above the deck; headboard top 1.05, footboard top 0.95; castors Ø 150 mm; under-clearance 0.15 |
| Pillow | 0.70 × 0.45 × 0.14 | Compressed to 0.07 under the head |
| Visitor chair | 0.55 × 0.58 × 0.82 | Seat 0.45 |
| Doctor stool | Seat Ø 0.36, base Ø 0.55 | Seat 0.48–0.62 (shown at 0.52) |
| Instrument trolley | 0.60 × 0.45 × 0.85 | Two shelves at 0.25 and 0.82, 15 mm lip on three sides, 75 mm castors |
| Over-bed table | 0.80 × 0.40 top | Top 0.95, C-base |
| Pedal bin (30 L) | 0.30 × 0.36 × 0.58 | |
| Sharps bin (5 L) | 0.18 × 0.14 × 0.28 | |
| Monitor | 0.34 × 0.15 × 0.29, 12-inch screen | Centre 1.55; wall channel 0.6 m with a 0.3 m arm |
| ECG cart | 0.50 × 0.55 footprint | Top 0.92; device 0.38 × 0.30 × 0.09; cable arm to 1.25 |
| IV stand | Base Ø 0.55, 5 legs | 4 hooks at 1.95 |
| Curtain | Track at 2.75 (ceiling), 25 mm section | Top 0.45 m open mesh; hem 0.30 above the floor |
| Clinical basin | 0.50 × 0.40 | Rim 0.85; elbow-lever mixer |
| Bedhead services | 1.60 × 0.25 trunking | Centre 1.45; O₂ (white), air (black/white), vacuum (yellow), 4 sockets, nurse-call |

---

## 5. Materials, lighting and hero objects

### 5.1 Material palette

Rule: **nothing in the albedo above sRGB 230 or below 30. No glossy fabric. No pure white walls.** All IDs below were confirmed to exist today through the ambientCG and Poly Haven public APIs **[V]**; I did not inspect each map, so check the look on import.

| Surface | Base colour (sRGB) | Rough / metal | Source |
|---|---|---|---|
| Sheet-vinyl floor | #A9B0AE with 10–15% fine fleck | 0.40 ±0.08 / 0 | **No CC0 vinyl exists on ambientCG (query returned 0)**; Poly Haven's `linoleum_brown` and `old_linoleum_flooring_01` are vintage. Build it: solid colour, fleck mask from ambientCG **Terrazzo018** (desaturated, tiled at 0.5 m), roughness variation from ambientCG **Smear007** tiled at 3 m. No normal map |
| Coved skirting | #8E9694 | 0.45 | Same material, darker |
| Walls (paint) | #DDDAD2 warm off-white | 0.85 | ambientCG **PaintedPlaster017**, normal strength 0.3 |
| Accent wall behind the bed | #7FA3A0 muted sage-teal (less saturated than now) | 0.85 | Same |
| Dado / wall protection | #C9CCC8 | 0.60 | ambientCG **Plastic013A** normal |
| Ceiling tiles | #E4E4DF | 0.95 | ambientCG **OfficeCeiling001** (try 001–006) |
| Door leaf (HPL oak) | Light oak, about #C2A47E | 0.50 | ambientCG **Wood049** |
| Worktop / cabinet HPL | #D8D6CF / #E6E5E0; accent doors #7FA3A0 | 0.40 | Flat colour plus Plastic013A normal |
| Brushed stainless (trolley, kick plate, lever, sink) | #C8CACB | 0.38 / 1.0 | ambientCG **Metal009** |
| Powder-coated steel (bed frame, IV pole, stool base) | #D6D7D4 | 0.50 / 0 | ambientCG **Plastic010** normal at 0.2 |
| Moulded ABS (bed boards, monitor, ECG, bins) | #E3E2DC; bin lids #2B2D2F and #E07A2A | 0.45 | Plastic013A |
| Vinyl upholstery (chair, stool, mattress cover) | Chair #4E6E7A, stool #2F3A40, mattress #3F566B | 0.55 | Poly Haven **leather_white** normal, tinted |
| Linen (sheet, pillowcase) | #E6E6E2 | 0.90 | Poly Haven **stretch_poplin** normal |
| Cellular blanket | #9FB4C7 pale blue | 0.95 | Poly Haven **waffle_pique_cotton** normal, tinted |
| Gown | #C5D6DC with a small two-tone print | 0.90 | stretch_poplin plus a custom print |
| Curtain | #7FA3A0 with a 40 mm tonal stripe | 0.90, specular near zero | Poly Haven **rough_linen** normal |
| Rubber (tubing, castors, hose) | #1C1E20 | 0.60 | ambientCG **Rubber004** |
| Splashback tiles | #E2E2DE | 0.25 | ambientCG **Tiles107** |
| Glass | Transparent, alpha 0.10 | Smoothness 0.95 | URP Lit |

### 5.2 Lighting plan

**Why it looks flat now:** near-white walls, three bright panels as the only light, no directional source, no occlusion and a white window.

| Light | Type / mode | Colour | Relative intensity | Note |
|---|---|---|---|---|
| Sun | Directional, **Mixed (Shadowmask)** | 5600 K | 1.0 (reference) | Yaw 40°, pitch 38°: it enters both the west room window and the south corridor window. Sun patch on the room floor in the working zone, striped by the blinds; it never reaches the patient's face. In the corridor it lands in front of the exam door |
| Sky | HDRI ambient | — | 1.0 | Poly Haven **quadrangle_sunny** **[V]** (university quad, midday, partly cloudy, 5400 K). Flatter alternative: **quadrangle_cloudy** |
| Room ceiling panels ×3 | Area, **Baked** | 4000 K | About **half** today's value; they should supply roughly 40% of the light at the bed | Diffuser emissive intensity about 1.5; it must not clip to pure white |
| Bedhead uplight strip | Area, Baked | 3000 K | 0.15 | Warm wash above the services panel; gives warm/cool separation behind the patient |
| Exam lamp | Spot, Baked, **off** | 4500 K | — | Emissive off |
| Corridor panels ×2 | Area, Baked | 4000 K | 0.6 × room panels | The corridor is dimmer than the room |
| East door glow | Emissive, Baked | 4000 K | Low | Sells "more hospital beyond" |
| Bed fill (PC only) | Spot, Mixed, soft shadows 1024 | 4000 K | 0.2 | Above the bed at the middle panel: gives the patient, doctor and instruments a grounded real-time shadow. Android: disable; use blob shadows |

- **Bake:** Progressive GPU, two 2048 lightmaps (room about 35 texels/m, corridor 20), directional mode on PC, **lightmap AO on** (max distance 0.4 m, indirect 1.0, direct 0.3), OIDN denoise.
- **Probes:** light-probe grid at 0.75 m, y = 0.2 / 1.1 / 1.9; extra rows 0.2 m either side of the door threshold and a tighter ring round the bed. **Two reflection probes**, baked, box-projected, 256 px (128 on Android): room box 6 × 2.8 × 5 at (0, 1.4, 0); corridor box 5 × 2.6 × 2.4 at (0.9, 1.3, −3.82); 0.3 m blend at the door.
- **Signature look:** the soft window streak reflected in the vinyl floor (roughness 0.40 does this). That one reflection reads as "hospital".
- **Window exterior:** HDRI as skybox **plus two real objects for VR parallax**, because a flat photo card reads as a poster in stereo: a 0.3 m limestone-coloured sill ledge outside each window, and one object 3–6 m out (Poly Haven `exterior_aircon_unit` **[V]** on a low wall, or a decimated tree). Blinds tilted 35°, lowered to 60%.
- **Post (VR profile):** Tonemapping **Neutral**, post-exposure tuned so a wall at 1.5 m height reads sRGB 150–170 on screen, contrast +8, saturation −5. No bloom, vignette, SSAO, depth of field or motion blur. 4× MSAA, single-pass instanced. Sun shadows: 1 cascade, 2048, 8 m.

### 5.3 Hero objects

**Sourcing rule [D]:** Poly Haven and ambientCG have no modern medical equipment, and Kenney and Quaternius props are stylised and would clash. So the clinical hero set is **modelled in Blender 4.2** to one consistent standard. The CC-BY Sketchfab models listed in the audit are a later upgrade if an account is provided. **(B)** = Blender, **(B-cloth)** = Blender cloth simulation baked to a static or skinned mesh, **(DL)** = account-free download.

| Object | Generic reference design and the features that sell it | Tris (LOD0) | Source |
|---|---|---|---|
| **Bed** | Electric ward bed. Twin-column or scissor base with a moulded base cover; four Ø150 twin castors with a central-brake pedal bar at the foot; four-section deck with visible perforated panels; **split side rails** (head 0.85 long, foot 0.60) as moulded panels with hand slots and embossed control ovals; blow-moulded head and foot boards with grip cut-outs, removable on posts; corner bumper wheels; red CPR lever under the backrest each side; coiled-lead control pendant hooked on the far rail; IV socket at each head corner; drainage-bag hooks | 25–35k | B |
| Mattress + sheet | Separate fitted sheet with tucked-corner creases; mattress edge piping visible at the foot | 2k | B |
| **Pillow** | Head dent with radiating creases, formed by a cloth pressure simulation against the head collider | 2–3k | B-cloth |
| **Blanket** | Sheet plus cellular blanket, folded back to the waist with a 0.25 m turn-down, draped over legs and feet with a toe bump, hanging 0.25 m over the near side, tucked on the far side. Simulated against the final reclined body and the mattress; rails are collision objects | 8–12k | B-cloth |
| **Gown** | Short-sleeved tie-back cotton gown. Two authored states as blend shapes: closed, and **opened to the xiphoid** for auscultation and electrodes. Skinned by weight transfer from the body | 6–10k | B-cloth |
| **Door set** | §3 | 6–8k | B |
| **BP cuff (NIBP)** | Adult nylon cuff 0.14 × 0.52 m laid slightly curled; hook-and-loop panel, artery marker and range line printed in white, bladder bulge, 8 mm grey hose with a bayonet connector, **2.5 m hose** from the bedhead cable point (12-segment verlet or spline). Wrapped state is a second mesh that inflates by 4 mm on a blend shape | 3–5k | B |
| **Stethoscope** | Dual-head chest piece (Ø 45 diaphragm with rim ring, small bell), Y-tube, sprung binaurals with soft ear tips. Tube on 10 bones for droop. In use: the chest piece stays in the hand and the binaurals snap to a neck anchor | 3–4k | B |
| **Pulse oximeter** | Hinged finger clip 60 × 35 × 30 mm with a soft grey inner pad, **red emitter glow** between the jaws, 1.5 m cable to the bedhead point. No built-in display (the monitor shows SpO₂) | 1.5–2k | B |
| **Monitor housing** | 12-inch bezel 15 mm, right-hand column with rotary knob and 5 keys, alarm light bar on top, carry handle, side connector panel with colour-coded sockets, VESA arm in a wall channel. Reuse the existing screen texture | 5–8k | B |
| **ECG cart** | Four-castor cart with handle, drawer and wire basket; device with a tilting 10-inch screen, membrane keypad, paper slot with a 50 mm strip of printout; **articulated cable arm** holding the trunk cable, a yoke, and **10 lead wires** (4 limb, 6 chest, colour-coded) with clip ends; a card of electrode tabs on the cart. After recording, electrodes and wires are visible on the patient | 12–18k (leads 4k) | B |
| **Visitor chair** | Stackable four-leg steel-tube chair, upholstered wipe-clean seat and back pads, no arms, glides | 4–6k | B |
| **Stool** | Round padded seat with stitched welt, chrome gas column, height ring, five-star base, twin castors | 4–6k | B |
| **Bins** | Rectangular pedal bin, lid with soft-close gap, visible bag rim, foot pedal, waste-stream label | 2–3k each | B |
| **Curtain** | Track with gliders and end stops; two stacked bundles with real fold depth, 0.45 m mesh header (alpha-tested), weighted hem | 6–10k | B-cloth |
| Trolley | Two-shelf stainless trolley, tube frame, push handle | 4–6k | B |
| Fire extinguisher, wheelchair, clock, exterior air-con unit | Poly Haven models **[V]** | Decimate to ≤ 12k | DL |

**Scene budget:** PC about 450k tris visible; Android target 250k using LOD1 at 50% for the bed, ECG cart and characters.

---

## 6. Characters

### 6.1 Casting (Rocketbox, MIT) — I opened the staged thumbnails **[V]**

| Role | Avatar | What it already has | Work needed |
|---|---|---|---|
| **Dr. Sara Hassan** | `Medical_Female_02` (`_facial.fbx`, textures `f153_*`) | Woman in her 30s, dark hair tied back, olive skin, **white lab coat**, light-blue shirt, dark trousers, **stethoscope round the neck**, chest ID badge | Texture work only (below) |
| **Mr. Adam Reyes** | `Male_Adult_05` (`_facial.fbx`, textures `m009_*`) | Man in his 50s, short dark greying hair, **grey stubble beard**, weathered face | Head is right. The torso and arms need replacing (below) |

Rejected: `Medical_Female_01` (short-sleeved nurse tunic), `Medical_Female_03` (paramedic jacket), `Male_Adult_03` (too polished, tweed), `Male_Adult_14` (too young), `Business_Male_07` (shirt and tie baked in), `Male_Adult_19` (Gulf-style dress; would read as a stereotype for a Palestinian bus driver).

### 6.2 Dr. Hassan — look **[D]**

- Keep the lab coat. Recolour shirt to ceil blue #7FA6C9 and trousers to navy #26344A so the outfit reads as scrubs under a coat at conversation distance. Repaint the shoes as dark closed clinical shoes.
- **Badge:** replace the texture with a provisional card: photo, «د. سارة حسن · Dr. Sara Hassan», "Emergency Medicine", green stripe, no logo. Add a pen in the chest pocket and a fob watch or lanyard.
- **Hair — two options for the team to choose [C]:** (A) as supplied, dark hair tied back; (B) a plain hijab in soft grey, navy or white, tucked into the coat collar: a 2–3k-tri Blender mesh replacing the hair mesh. Both are common among Palestinian physicians. Option B also removes hair-card problems. I make no recommendation between them.
- Face: brown iris, slight warm under-eye tone, natural lips, no heavy make-up.

### 6.3 Mr. Reyes — look **[D]**

- Rocketbox clothing is the body surface, so there is no skin under the sweater. In Blender: keep head, neck and hands; delete the torso and arm clothing; **graft bare forearms and upper arms** from a short-sleeved Rocketbox male (same skeleton; I did not check which avatar is the best donor, so pick one from the `Male_Adult_*` or `Sports_Male_*` thumbnails and match skin tone in the texture); model a simple upper-chest skin patch (visible only in the gown-open state) with sparse grey chest hair as a decal.
- Legs stay as his own trousers under the blanket (not visible). Feet form the toe bump.
- **Unwell:** skin saturation −12%, slightly grey lips, **sweat**: smoothness mask up on forehead, temples, upper lip and sternum (0.35 → 0.7) with a few alpha-tested droplets at the hairline; damp hair strands at the temples. A hospital ID band on the right wrist (white, printed name).
- Body: slightly heavy, shoulders rounded into the mattress, head resting **on** the pillow.

### 6.4 Acceptance criteria — "not a mannequin"

1. Never motionless for more than 2 s. The patient's chest and abdomen move 6–10 mm at **20 breaths/min, phase-locked to the monitor's Resp trace**.
2. Blinks 12–20/min; saccades every 0.8–3 s; eye contact about 65% while listening and 40% while speaking, with look-aways down and to the side.
3. Eyes lead head turns by 150–250 ms; the head follows only when the target is more than 15° off.
4. The doctor shifts weight every 8–15 s and never repeats the same idle twice in a row.
5. No cloth intersects body, rail or blanket in any clip. Resting hands touch a surface (gap under 5 mm).
6. Lips close in silence; audio and visemes within ±80 ms (uLipSync).
7. A visible reaction within 300–500 ms of being spoken to or touched. During AI latency a **thinking behaviour** plays within 300 ms (patient: breath in, glance down, "mm…"; doctor: small nod, "hm"). Pre-render 4 fillers per character.
8. Gestures occur only while speaking and return to rest. Never the single-arm raise seen in `P01`.
9. Brown irises with one soft specular highlight; no red tint; visible lower-lid shadow.
10. Clothing is identifiable in one glance: lab coat, hospital gown.

---

## 7. Body language and animation

### 7.1 Sources

| Source | Licence / access | Use |
|---|---|---|
| **Rocketbox `Assets/Animations`** | MIT, GitHub, no account. **[V]** 326 static clips plus root-motion sets (`motextr_xy` has walks and runs; `motextr_xyz` has turns and sit/stand) | **Primary.** Same skeleton as the avatars, so there is no retargeting error |
| Blender 4.2, keyed on the Rocketbox rig | Local | Patient reclined base pose and five reaction clips; the doctor's clasped-hands pose |
| Mixamo | Free but **needs an Adobe login** | Fallback only |
| Quaternius Universal Animation Library | CC0, no account ([site](https://quaternius.com/packs/universalanimationlibrary.html)) | Game-style locomotion; not needed. No lying or clinical clips confirmed |
| CMU mocap | Free, noisy | Not needed |

### 7.2 Dr. Hassan — clip list (Rocketbox names, prefix `f_`)

| Behaviour | Clips |
|---|---|
| Idle pool | `idle_neutral_01`, `_03`, `_05`, `_07`; `idle_breathe_01`–`03` |
| Weight shift / fidget (rare) | `idle_move_leg_01`, `idle_waiting_01`, `idle_look_around_01` |
| Walk | `walk_start`, `walk_neutral_01`, `walk_slow_01`, `walk_stop` |
| Turn | `turn_left_60/90/120/180`, `turn_right_60/90/120/180`, plus `_to_walk` variants |
| Door | `invite_sit` ("after you" sweep); `try_door_inwards` (only if she must open it herself) |
| Observe | `idle_neutral_*` with a Blender pose override: **hands clasped low in front** (default) and **clipboard held at the waist**. Arms crossed is not used: it reads as judging |
| Listen / nod | `gestic_listen_accept_01`–`05`, `gestic_listen_neutral_01`–`03` |
| Think | `gestic_thoughtful_01` |
| Explain | `gestic_talk_neutral_01`–`05`, `gestic_talk_relaxed_01`–`02`, `gestic_talk_self-assured_01` |
| Indicate / point | `gestic_presentation_left_01/02`, `_right_01/02` (open-hand indication, which is more courteous than a finger point) |
| Clipboard | `documents_in`, `documents_idle`, `documents_check`, `documents_note`, `documents_out` |

**Animator layers:** Base locomotion (root motion, driven by the NavMeshAgent's desired velocity) → Upper-body gesture (avatar mask) → Additive breathing → Face (blend shapes).

### 7.3 Mr. Reyes — clip list

| Clip | Source | Description |
|---|---|---|
| `reclined_base` | Blender pose | Hips on the mattress, spine along the 40° backrest, head on the pillow turned 10° toward the student, knees slightly bent, forearms on the blanket |
| `reclined_idle_A/B/C` | `m_sit_chair_idle_neutral_01/02`, `m_sit_chair_breathe_01`, upper body only, at 50% weight | Small torso and head drift |
| `breathing` | Additive, procedural or keyed | 3 s cycle, upper chest more than abdomen, slight shoulder lift (he is breathless) |
| `pain_shift` | Blender, 3.5 s | Presses shoulders back, shifts hips, exhales through pursed lips, settles. Every 25–45 s |
| `hand_to_chest` | Blender, 2.5 s plus hold | **Far-side hand** closes to a fist on the sternum, rubs once, returns. On pain questions and pain pulses |
| `rub_left_arm` | Blender, 3 s | Right hand rubs the left upper arm. Only when the right arm is free |
| `wince_cold_steth` | Blender, 0.8 s | Sharp small inhale, shoulders lift 1 cm, eyes narrow, then a breath out. First stethoscope contact only |
| `cuff_squeeze` | Additive face + hand | Mild grimace and fingers flex at peak inflation |
| `nod_yes`, `shake_no` | `m_gestic_listen_accept_01`, `m_gestic_listen_deny_01`, head and neck mask | Small amplitude; the head stays on the pillow |
| `look_at_doctor` | Aim rig | Glances at Dr. Hassan when she speaks, then back to the student |
| `offer_arm` | Blender, 1.5 s | Lifts and straightens the near arm when the cuff or oximeter approaches within 0.3 m |
| `sit_forward` (optional) | Blender, 2 s | Leans 15° off the backrest if lung zones on the back are ever added |

### 7.4 Where IK earns its cost (Unity Animation Rigging)

| Rig | Use |
|---|---|
| **Multi-Aim** head (0.6), neck (0.3), upper spine (0.1), plus eye bones or eye-look blend shapes | Both characters. Targets: speaker's face, the instrument in the student's hand, the monitor when a value appears, the ECG paper. Gaze should be **anticipatory**: the doctor looks at the monitor about 0.5 s before she refers to it |
| **Two-Bone IK**, patient far hand → sternum target on the gown | The fist lands on cloth, not inside it |
| **Two-Bone IK**, patient near arm → pose target during cuff and oximeter | The arm stays exactly where the snap zones are |
| **Two-Bone IK**, doctor right hand → pen-on-clipboard target; clipboard parented to the left hand | Makes `documents_note` precise |
| Two-Bone IK, doctor hand → lever | Only if she opens the door herself (fallback path) |
| Not used | Foot IK (flat floor) and full-body IK |

---

## 8. Personalities and voice

All example lines are **delivery examples**. Anything clinical must come from the scenario JSON and pass the same clinical review **[C]**.

### 8.1 Dr. Sara Hassan — teaching persona

Calm, warm, brief. Socratic: she asks before she tells. She treats the student as a junior colleague and the patient as a person. She speaks only when it helps; her silence means "you are doing fine". At most two sentences at a time, and never while the patient is speaking.

| Situation | Behaviour | Example phrasing |
|---|---|---|
| **Silence** (student on track) | At O1, hands clasped, eyes following the work; a small nod at good moments; occasional clipboard note | — |
| **Acknowledge** (at most once every 60 s) | Nod, half-smile | "Good." / «ممتاز.» |
| **Nudge** (ordinary gap, after the grace period) | Half-step in, open hand toward the relevant object, a question | "What would you like to know about his breathing?" · "Is there anything you'd check before you examine him?" |
| **Second nudge** | More specific, still a question | "We have a monitor here. What is it not showing yet?" |
| **Safety-critical** | Steps forward, uses the student's attention first, direct, no question | "Stop for a moment. Before anything else, he needs a 12-lead ECG. Go ahead and ask for it." |
| **Asked for help** | Answers the question asked, then hands control back | "Start with the pain itself. You lead; I'm here." |
| **To the patient** | Turns fully to him, softer | "You're doing well, Mr. Reyes. We're looking after you." |
| **Debrief** | Faces the student at conversation distance; structure: one strength → one miss and why it matters → one thing for next time | "You built trust quickly, and your history was thorough. You got to the ECG later than I'd like; with chest pain like his, minutes matter. Next time, ask for it as soon as you hear the story. Well done today." |

### 8.2 Mr. Adam Reyes — patient persona

Fifty-eight, bus driver for decades, practical, not a complainer, privately frightened (his father had a heart attack at 55). He answers what is asked and no more. **Pain shapes his speech:** short sentences, a breath mid-sentence, a pause before answering, trailing off when the pain pulses, an occasional "sorry". He plays it down at first ("it's probably nothing") and then admits "it's bad".

- **Reassurance** → a longer exhale, shoulders drop, a slightly longer answer, a quiet "thank you, doctor".
- **Brusque or jargon-heavy student** → shorter answers, looks to Dr. Hassan, "what does that mean?"
- **When examined** → cooperative: offers his arm, lifts his chin. Winces at the cold stethoscope: "That's cold."
- **Worry lines** (persona only, no clinical facts): "Is it my heart?" · "My wife's outside, can someone tell her?" · "I've got a shift in the morning."

### 8.3 Voices (OpenAI list) **[D]**

**[R]** OpenAI recommends `marin` and `cedar` for best quality, says the voices are optimised for English, and will shut down every model on the Speech endpoint on **2027-01-06** ([TTS guide](https://developers.openai.com/api/docs/guides/text-to-speech)). So: **render the whole approved line library to WAV now** and keep the files; they outlive the endpoint.

| Character | First choice | Alternatives | Why |
|---|---|---|---|
| Dr. Hassan | **marin** | coral (warmer, livelier), sage (quieter) | Natural, adult, unhurried; not perky |
| Mr. Reyes | **cedar** | onyx (deeper, heavier), ash (lighter) | Mature, grounded male voice that takes "breathless and worried" direction well |
| Avoid | fable (British storyteller colour), verse and ballad (theatrical), shimmer and nova (too bright for an emergency department) | | |

**Arabic caution [C]:** dialect cannot be reliably forced by instructions. Write the text itself in Palestinian colloquial spelling, add diacritics on ambiguous words, audition each voice, and have a native speaker approve every file. Use spoken Palestinian Arabic for the patient, and educated colloquial ("white" Arabic) for the doctor, who keeps the medical terms a Palestinian clinician would actually use.

### 8.4 Steerable-TTS `instructions`

**Dr. Hassan — English**
```
Voice: woman in her late thirties, senior emergency physician, Palestinian, speaking fluent English with a light, natural Arabic accent.
Tone: calm, warm, quietly confident. A mentor beside a junior colleague, never a lecturer.
Pace: unhurried, about 150 words per minute; short sentences; a small pause before a question so it lands as a real question.
Emotion: steady and encouraging. For safety-critical lines become firmer and slightly slower, never louder, never alarmed.
Delivery: conversational, close-mic, low volume as if in a patient's room. No announcer energy, no upward sing-song, no vocal fry. Smile only on praise.
```

**Dr. Hassan — Arabic**
```
الصوت: طبيبة طوارئ فلسطينية في أواخر الثلاثينات، تتكلّم بعامية فلسطينية مهذّبة قريبة من الفصحى المبسّطة، وتستخدم المصطلحات الطبية كما يقولها الأطباء في فلسطين.
النبرة: هادئة، دافئة، واثقة. زميلة أكبر تُرافق طالب الطب، لا محاضِرة.
الإيقاع: متمهّل، جمل قصيرة، وقفة قصيرة قبل السؤال.
الانفعال: ثابت ومشجّع. في تنبيهات السلامة يصبح الصوت أكثر حزماً وأبطأ قليلاً، دون رفع الصوت أو توتّر.
الأداء: حديث طبيعي بصوت منخفض داخل غرفة مريض. بلا نبرة مذيعين وبلا مبالغة.
```

**Mr. Reyes — English**
```
Voice: man of 58, working-class bus driver, plain-spoken, slightly gravelly.
State: ongoing heavy chest pain, mildly short of breath, sweaty, nauseated, frightened but trying to stay composed.
Pace: slow and effortful. Short phrases. Take an audible small breath in the middle of longer sentences. Pause briefly before answering.
Emotion: worried, tired, cooperative. Understate at first. Let the fear show on questions about his heart or his family.
Delivery: quiet, close, a little strained; trail off slightly at the end of sentences. No acting, no groaning, no theatrical gasps. When reassured, the voice loosens and warms a little.
```

**Mr. Reyes — Arabic (Palestinian dialect)**
```
الصوت: رجل عمره ٥٨ سنة، سائق باص، يحكي باللهجة الفلسطينية العامية (لهجة منطقة رام الله)، بسيط ومباشر، صوته خشن قليلاً.
الحالة: وجع ثقيل مستمر في الصدر، نفَسه قصير شوي، عرقان، حاسس بغثيان، وخايف بس بحاول يضل متماسك.
الإيقاع: بطيء ومتعَب. جمل قصيرة. ياخذ نفَس خفيف مسموع في نص الجملة الطويلة. يسكت لحظة قبل ما يجاوب.
الانفعال: قلقان، تعبان، متعاون. يهوّن الموضوع في الأول، ويبيّن الخوف لما ينسأل عن قلبه أو عن أهله.
الأداء: صوت واطي وقريب وفيه شدّة خفيفة، ويخفت آخر الجملة. بدون تمثيل زايد وبدون أنين أو لهاث مبالغ فيه. لما يطمّنوه، يرتاح صوته شوي.
```

Sample Arabic delivery (persona only): «دكتور… هو قلبي؟» · «آه… هون، في نص صدري… زي إشي ثقيل قاعد عليه.» · «الله يرضى عليك، خبّروا مرتي، هي برّا.»

---

## 9. Experience and UX direction

### 9.1 The seven steps

| Step | Staging | Doctor position, gaze and behaviour | Pacing |
|---|---|---|---|
| **1. Corridor welcome** | Fade in from black (1.5 s) at S. Ambience: air handling, distant trolley, a muffled page. No menu | D0. She is reading her clipboard; she looks up when the student's gaze rests on her for 0.5 s (or after 3 s), lowers the clipboard and smiles. One line, at most 15 s: *"Welcome to Birzeit. I'm Dr. Hassan; I'll be supervising you. Mr. Reyes, fifty-eight, has come in with chest pain. Take a history, examine him, and tell me what you think. I'm right behind you. Shall we?"* (about 13 s). «أهلاً وسهلاً في بيرزيت. أنا الدكتورة سارة حسن، ورح أكون معك. المريض السيد آدم، عمره ٥٨ سنة، إجا بوجع في صدره. خُذ منه القصة وافحصه، وبعدين احكيلي شو رأيك. أنا وراك. يلّا نفوت؟» | ≤ 20 s to door prompt |
| **2. Door** | The sun patch lies in front of the door. Lever at 1.0 m | On "Shall we?" she turns, walks to D1 and gestures "after you", looking at the lever and then at the student. She follows 1 m behind and to the student's left once the door passes 70°. The closer shuts the door behind them. No controls speech unless the student fumbles for 8 s | 10–20 s |
| **3. Meet the patient** | Reveal through the door: cabinets ahead, bed to the front-left, patient lit from the window side. Monitor beep fades in at the threshold | D2. Looks at the patient: *"Mr. Reyes, this is one of our student doctors. They'll be looking after you, with me."* The patient lifts his head, then looks at the student. She walks to **O1** and goes quiet | 10 s, then the student owns the room |
| **4. Examination** | Student at the patient's right; trolley at the head end, ECG cart at the foot end | O1. Her gaze follows the student's hands and looks at the monitor when a value appears. A nod on good practice (hand hygiene, introduction, consent) | Student-paced |
| **5. Supervision** | Unchanged scene | Interventions as in §8.1. Before speaking she takes a half-step and waits for a gap of at least 0.7 s in speech. Cooldown ≥ 20 s between unprompted remarks, except safety-critical | — |
| **6. Discussion** | Triggered by the student ("I'm ready to present") or by her after escalation | She walks to O2 and half-turns so the patient is out of the conversation: *"Step over here with me. Tell me what you've found."* Four questions, each after the student finishes: findings → what matters most → impression → next step. She nods and makes a clipboard note between answers | 2–3 min |
| **7. Spoken debrief** | Same place, lower voice | 30–45 s, structure in §8.1, built only from rubric results. Then: *"That's all for today. Put the stethoscope back on the trolley when you're ready."* She turns to the patient: *"Mr. Reyes, the team is on its way."* | ≤ 45 s |

**Ending [D]:** when the stethoscope is replaced, or the student says goodbye, or 8 s pass, the room sound fades and the image fades to a soft warm grey over 2 s. A single quiet panel then offers: *View report · Try again · Exit*. The session ends on her last words, not on a button.

### 9.2 UI rules

- **Diegetic only during the case:** bedside monitor, ECG screen and printout, the wall clock, the doctor's clipboard. No score, timer, checklist, toast, zone glow or floating label. Replace the zone glow with a small haptic tick and a subtle rim highlight on the instrument only.
- **Pause menu** (menu button; world dims 60%, audio ducks, simulation time stops): panel 1.2 m ahead, 10° below eye level. Items: Resume · Language (English / العربية) · Subtitles (Off / On) · Comfort (seated or standing height, snap or smooth turn, teleport or walk) · Volume · Controls card · Fallback question list (the old tablet content) · Restart · End session. The **DRAFT — NOT CLINICALLY VALIDATED** banner lives here, on the corridor notice board and in the report.
- **Mode selection** (training or assessment, language) happens before the fade-in on the operator's desktop window, or on a first-run pause panel in the corridor.
- **Subtitles (off by default):** head-locked with a lazy follow, 1.4 m away, 18° below gaze; maximum two lines of 40 characters; text height at least 1.2° (about 30 mm at 1.4 m); off-white on a 70% dark rounded plate; a small colour chip and name for the speaker (teal = Dr. Hassan, sand = Mr. Reyes). Arabic is right-aligned in Noto Sans Arabic.
- **Listening / Speaking indicator:** remove the floating tag. (a) A 10 mm light on the talk controller: white pulse = listening, slow amber = thinking, off = speaking; a short haptic tick at start and end of capture. (b) More important, the addressed character **shows** listening: turns, holds eye contact, tilts the head, then plays a thinking behaviour. The character is the indicator.
- **Audio:** each voice from the character's mouth (spatial, light room reverb); monitor beep at 98/min from the monitor; corridor ambience occluded when the door shuts; soft footsteps for the doctor.

---

## 10. Open decisions

Build in the order of the Top 15 table (§0).

**Open decisions for the client [C]:** hijab option; working side of the bed; Arabic dialect sign-off by a native speaker; whether the patient keeps the name "Adam Reyes" in Arabic mode; university approval of the provisional sign wording.

---

## 11. Environment redesign (v0.2) — implemented vs. deferred

Built by `Assets/MedSimAI/Editor/MedSimRedesignBuilder.cs` (menu **MedSim AI ▸ Build Redesign (v0.2)**, then **MedSim AI ▸ Bake Lighting**). The builder is idempotent: it rebuilds the roots `Redesign_Corridor`, `Redesign_Door`, `Redesign_Room`, `Redesign_Lighting`, `Redesign_Staging`, `Redesign_Exterior` and every child named `v2_*`; legacy visuals are deactivated, not deleted. Asset conversion: `scratch_env/convert.py` and `blanket.py` (Blender 4.2). Credits: `ASSET_CREDITS.md`.

### Implemented
| Spec | What was built | Deviation |
|---|---|---|
| §2 Corridor | 5.0 × 2.4 m bay at x −1.60…3.40, z −5.02…−2.62, ceiling 2.60 m: sheet vinyl with coved skirting, 0.9 m dado with cap, 600 mm ceiling grid, two LED panels, air diffuser, smoke detector, oak handrail, south window (frame, mullion, roller blind, sill, radiator), closed west door "Exam Room 2", closed east double fire doors with EXIT sign, hanging way-finding sign, notice board with four pinned sheets (the DRAFT notice moved here), beam seating, sanitiser stand, wall-mounted extinguisher with ID sign, room plate | Bench is the 5-seat beam (2.2 m) instead of 3 seats; leaflet rack and wheelchair omitted; extinguisher is at x = 3.20 next to the sanitiser stand (x = 2.78) |
| §2.3 Sign | 1.20 × 0.45 m panel on four stand-offs at (0.50, 1.60, −2.62): «جامعة بيرزيت — أهلاً وسهلاً» / "Birzeit University — Welcome" / footer. Noto Kufi Arabic through `ArabicShaper`; verified joined and right-to-left in `C01_sign_closeup.png`. No logo | — |
| §3 Door | CC-BY hospital door: separate frame, leaf (vision panel, kick plate, hinges) and lever on its spindle; hinge pivot on the east jamb at (2.39, −2.445); opens into the room 0–95°; kinematic leaf collider; latch / swing / close audio; overhead closer; amber "in use" dome | Vision panel is opaque (blind shut); no push plate; the closer is decorative (auto-close is the session script's job) |
| §4 Room | New bed (split rails, castors, boards) around the existing mattress pivot; cloth-simulated blanket; pillow; visitor chair; doctor stool; two pedal bins away from the door; resus trolley replaces the domestic chest; over-bed table with a pulp bowl; IV pole with bag; towel dispenser; neutral observations chart replaces the "CHEST PAIN" poster; "MEDSIM AI" poster, rusty bins, invisible arm-chair / stool / fire alarm removed; tablet stand parked by the east wall | **Layout was not mirrored** (open decision [C] in §4.1): the student still works from the patient's left (+x), so the doctor observes from the far-side foot corner **(−0.80, −0.45)** instead of (1.25, −0.55), where the ECG cart stands. Backrest stays at 35° (existing patient pose) |
| §5.1 Materials | Wall paint #D9D6CE and sage accent with ambientCG plaster normal, acoustic ceiling tile, procedural two-tone-fleck sheet vinyl (smoothness 0.60), ABS / powder-coat / stainless / vinyl upholstery / linen / cellular blanket families | Sheet vinyl has no roughness-variation map |
| §5.2 Lighting | HDRI sky (Poly Haven urban_courtyard_02) as window view and ambient; baked sun at yaw 40°, pitch 38° through both windows; ceiling panels re-tuned; warm bedhead wash; exam spot remains the single Mixed light; 370+ light probes incl. door rows and a bed ring; two box-projected reflection probes (room, corridor); Neutral tonemapping, vignette off | Sky is urban_courtyard_02 (staged) rather than quadrangle_sunny; sun is Baked, not Mixed (the project keeps one realtime-shadow light for VRAM reasons); blinds unchanged |
| §5.3 Instruments | Stethoscope, aneroid sphygmomanometer (loose state; the wrapped cuff shows when snapped to the arm via `InstrumentVisualState`) and fingertip oximeter visuals swapped; scripts, colliders, probe points and displays untouched. Monitor housing gained handle, alarm bar, keys, knob, connector panel and cables; ECG cart gained a frame, drawer, basket, push handle, cable arm and colour-coded leads | Device bodies are still the procedural boxes underneath; no 2.5 m NIBP hose / oximeter cable |
| §9 Staging | Student spawn (0.45, −4.00) yaw 20°; doctor corridor mark (1.30, −3.30); doorway mark (1.50, −1.45); observe mark (−0.80, −0.45); `RoomEntryTrigger` inside the doorway; NavMeshSurface over both spaces (agent radius 0.25 m); NavMeshAgent + `DoctorMovement` + capsule on the instructor; capsules on the patient; teleport areas for corridor and doorway; SessionManager wired | — |
| Audio | Corridor HVAC room tone (0.10) and muffled plant noise behind the fire doors (low-pass 900 Hz, 0.06) | No walla / PA (French speech and a possible baby in the staged files) |

### Deferred
- Mirroring the working side to the patient's right, wall-arm monitor move, L-shaped curtain track (needs the clinical decision [C]).
- Characters, gown, reclined 40° pose, hands (another agent). The blanket is fitted to the *current* patient mesh: re-run `export_obj.cs` → `blanket.py` after the patient changes.
- Corridor renderer culling when the door is latched; lever fumble prompt; door closer animation.
- Curtain cloth model with mesh header, electrodes on the patient, NIBP hose, exam-lamp replacement, roughness-variation map for the floor, exterior parallax object beyond the two stone ledges.

---

## 12. Characters v0.2 — implemented vs. deferred

Installed by `Assets/MedSimAI/Editor/MedSimCharactersV2.cs` (menu **MedSim AI ▸ Characters v2 ▸ Run all**; idempotent). Assets: `Assets/MedSimAI/Characters/v2/`. Blender preparation: `AssetStaging/CharacterPrep/README.md`. Do not re-run the old **MedSimSceneBuilder** character step: it would put the MakeHuman prefabs back.

### Implemented
| Spec | What was built | Deviation |
|---|---|---|
| §6.1 Casting | Dr. Hassan = Rocketbox `Medical_Female_02`. Mr. Reyes = `Male_Adult_05` head and rig on the body of `Male_Adult_09` (bare arms; identical skeleton) | Arm donor chosen in Blender from four candidates |
| §6.2 Doctor look | Lab coat, stethoscope, recoloured shirt and trousers, brown iris, darker hair, badge card "Dr. S. Hassan / Emergency Medicine / SIMULATION FACULTY" | Tied-back hair only (hijab variant not built); no Arabic on the badge; no pen or fob watch |
| §6.3 Patient look | Short-sleeved print gown skinned to the body, wristband, grey stubble, 12% desaturated skin; blanket fitted to the pose replaces the environment blanket (same `Blanket_Cellular` material) | Jeans and shoes remain under the blanket; no chest skin under the gown; sweat mask not done |
| §7.2 Doctor animation | Idle ring (`Idle_Neutral_01/02/03`, `Idle_LookAround`, and the long `Idle_WeightShift` as the observing idle, so no idle repeats back to back); `Speed` blend of `Walk_Slow_Loop` (0.89 m/s) and `Walk_Loop` (1.21 m/s) around `DoctorMovement.walkSpeed` 1.05; `Listen_Neutral_01` with two nods while `Listening`; upper-body talk gestures while `Speaking` (`Talk_Relaxed_01` when `Mood` = Approving) | Observing idle is the weight-shift / hands-on-hips clip, not crossed arms or clasped hands. `Point` pauses talk gestures and leaves the aimed arm to `HumanoidLifeController`; the Rocketbox presentation clips point high and ignore the target. `Clipboard_Idle` is a bent-over-a-desk clip and is not used |
| §7.3 Patient animation | Reclined pose authored for the 35° backrest; idles per `Condition` (0, 1, 2); `PainShift`, `HandToChest`, `Wince` as full-body triggers; `Nod`, `ShakeNo`, `Think` on a head layer. `PatientInteractionController` fires `Wince` on the first finding and then every third one `HandToChest` (7 s cooldown; never while something is attached to the right arm or chest), `PainShift` every 45–75 s (about twice as often in distress), `Think` when the AI starts preparing his answer | Condition-0 idle has no baked breathing: `HumanoidLifeController` breathes on top. `rub_left_arm`, `cuff_squeeze`, `offer_arm` not built |
| §6.4 Lip sync | uLipSync 3.1.5 analyses each character's AudioSource; `ULipSyncVisemeDriver` maps its phonemes (A, I, U, E, O, S) to the `AA_VI_*` visemes through `FaceRig`. While a line has audio, the text-timed path in `CharacterSpeechController` is off (`audioDrivenLipSync`); text-only lines still use it. Verified with a local test voice: visemes follow the audio and return to rest within a few frames of `Stop()` | Vowels and sibilants only (the sample MFCC profiles have no other consonants); profiles are the package samples, not calibrated to the TTS voices. ±80 ms sync was not measured |
| Zones, pillow | The 10 PlacementZones are re-parented to the new bones and re-fitted by raycast on the posed gown and arms; the pillow is moved under the back of the head | — |

### Deferred
- Hijab variant, Arabic badge text, sweat and pallor material pass, eye bones in the Humanoid avatar (gaze uses the eye-look blend shapes).
- Hands-clasped observing pose, clipboard prop, Two-Bone IK for the hand on the sternum.
- Lower body under the blanket (jeans, shoes): never show the patient without the blanket.
- uLipSync profile calibration against the production TTS voices.
