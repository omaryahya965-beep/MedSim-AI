#!/usr/bin/env bash
# Runs every test that does not need Unity: surgical C# tests, clinical (existing) C# regression, Unity-side compile
# check, and the proxy's node tests. Needs: .NET 8 SDK and Node 22. No API key, no network, no spending.
set -e
cd "$(dirname "$0")"
echo "== Surgery (engine-free C#)";  (cd Surgery && dotnet run)
echo "== Clinical regression (existing EditMode tests)"; (cd Clinical && dotnet run)
echo "== Compile check (Unity-side Surgery scripts vs API stub)"; (cd CompileCheck && dotnet build -p:IncludeP4Unity=true -v q)
echo "== Proxy (node)"; (cd ../../Backend/medsim-proxy && node --test server.test.mjs live.test.mjs surgery.test.mjs)
