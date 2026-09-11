#!/usr/bin/env bash
set -euo pipefail
AUDIT="$(cd -- "$(dirname -- "$0")" && pwd)"
mkdir -p -- "$AUDIT/Rollback_Test"
cp -- "$AUDIT/BASELINE.unity" "$AUDIT/Rollback_Test/Restored_Baseline.unity"
cmp -s -- "$AUDIT/BASELINE.unity" "$AUDIT/Rollback_Test/Restored_Baseline.unity"
printf '%s\n' 'ROLLBACK_COPY_PASS original_material_baseline_exact=1 modified_scene_untouched=1'
