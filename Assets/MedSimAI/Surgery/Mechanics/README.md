# Mechanics — owner: Person 3 (feature/surgery-mechanics)

Objective engine and interactions. Implements ISurgicalScenarioRuntime + IInstrumentRegistry, reads objectives from the scenario JSON and publishes all gameplay events with source "mechanics" (contract §6.5, §7). Must not reference AI/Characters/UI/Integration or the network. Test in `Scenes/Mechanics_Test.unity`.

Assembly: `MedSimAI.Surgery.Mechanics`. Allowed references are listed in `Docs/Surgery/SURGERY_CONTRACT.md` §3. Ask Person 4 before adding new ones.
