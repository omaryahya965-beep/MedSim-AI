# Off-Unity tests (no Unity install needed)

Engine-free MedSim Surgery logic and its NUnit tests compile and run with the plain .NET 8 SDK (~1 GB instead of
the Unity editor). No NuGet access is needed: NUnit is shimmed in `NUnitShim/`.

```bash
# Windows: winget install Microsoft.DotNet.SDK.8   ·  Ubuntu: sudo apt install dotnet-sdk-8.0
cd Tools/OffUnityTests/Surgery
dotnet run                 # all tests
dotnet run -- Assessment   # only tests whose name contains "Assessment"
```

`CompileCheck/` type-checks the Unity-side scripts against a tiny `UnityStub/` (catches typos and API misuse only).
Run it with `dotnet build`, or `dotnet build -p:IncludeP4Unity=true` to include the AI/Characters/Integration scripts.

This does **not** replace Unity's Test Runner: scenes, prefabs, physics, XR and audio still need the editor.
