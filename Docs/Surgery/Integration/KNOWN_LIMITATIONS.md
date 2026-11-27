# Known limitations

- **Not fully integrated.** Person 2 (anatomy, instruments, implants) and Person 3 (mechanics) had not pushed their branches. The OR uses named placeholders and **mock mechanics** until then. Mock results are labelled and must not be presented as real performance.
- **Not verified in Unity from this environment.** The Unity-side scripts compile against an API stub and the engine logic is tested, but the scenes have not been generated and played, and no build or headset test was done here.
- **Characters:** the surgeon reuses the existing Rocketbox doctor model and the assistant reuses the existing Rocketbox male model (the ER patient's body) with procedural scrubs, cap and mask. A dedicated OR model can replace them with no code change. The mask covers the mouth, so lip sync is mostly hidden.
- **Animation:** the assistant uses the doctor's Animator controller. There is no dedicated "hand over" clip; the instrument is parented to the right hand bone and held out.
- **Anchors** derived by the integration layer (surgeon observation spot, assistant at the instrument table, implant table = instrument table) are first-guess positions. Check them in Person 1's room, and P1 may add proper anchors.
- **No `player.entered_room` publisher** in the environment yet (P1's room has a vestibule but no trigger). The supervisor doesn't depend on it.
- **Voice offline:** without the proxy there is no speech understanding. H/J keys give help and explanations.
- **Scenario content is DRAFT** and deliberately simplified (see CLINICAL_REVIEW.md). Tolerances are simulation values.
- **Debrief language:** the written report uses the session language at completion; it does not regenerate if the language is switched afterwards.
