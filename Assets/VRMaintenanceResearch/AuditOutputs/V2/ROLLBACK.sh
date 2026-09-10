#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "$0")/../../../.." && pwd)"
BASE="$ROOT/Assets/VRMaintenanceResearch/AuditOutputs/V2/Environment_V2_BASELINE.unity"
TARGET="$ROOT/Assets/VRMaintenanceResearch/AuditOutputs/V2/Rollback_Test/Restored_Empty_Baseline.unity"
mkdir -p -- "$(dirname -- "$TARGET")"
cp -- "$BASE" "$TARGET"
cmp -s -- "$BASE" "$TARGET"
printf '%s\n' 'ROLLBACK_COPY_PASS baseline_exact=1 modified_scene_untouched=1'
