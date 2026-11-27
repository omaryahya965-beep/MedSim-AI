#!/bin/bash
cd "/d/Medical-Test/My project"
unity command script recompile >/dev/null 2>&1; sleep 2
for i in $(seq 1 60); do out=$(unity command script recompile_status 2>/dev/null); echo "$out" | grep -qE "status: (completed|up_to_date|failed)" && break; sleep 3; done
echo "$out" | head -80
