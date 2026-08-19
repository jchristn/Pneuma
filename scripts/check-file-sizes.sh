#!/usr/bin/env bash
#
# File-size guardrail (WP-9). Fails when a source file exceeds its threshold, so oversized files are
# split before they land rather than discovered later. A small allowlist grandfathers pre-existing large
# backend files that are a single class organized with regions; adding a new file to the allowlist is a
# conscious, reviewable decision — not a silent exception.
#
# Thresholds:
#   - Backend  C#  (.cs):        500 lines   (allowlisted files are exempt)
#   - Frontend JSX/JS (.jsx/.js): 400 lines
#
# Run locally from the repo root:  bash scripts/check-file-sizes.sh

set -euo pipefail

BACKEND_LIMIT=500
FRONTEND_LIMIT=400

# Backend files grandfathered above the limit (single class + regions). Keep this list short and justified.
BACKEND_ALLOWLIST=(
  "src/Pneuma.Core/Integrations/Implementations/LiteGraphClient.cs"
  "src/Pneuma.Core/Integrations/Implementations/PartioClient.cs"
)

violations=0

is_allowlisted() {
  local candidate="$1"
  local entry
  for entry in "${BACKEND_ALLOWLIST[@]}"; do
    if [ "$entry" = "$candidate" ]; then
      return 0
    fi
  done
  return 1
}

echo "== Backend C# guardrail (> ${BACKEND_LIMIT} lines) =="
while IFS= read -r -d '' file; do
  # Normalize the leading ./ that find prepends so it matches the allowlist entries.
  rel="${file#./}"
  lines=$(wc -l < "$file")
  if [ "$lines" -gt "$BACKEND_LIMIT" ]; then
    if is_allowlisted "$rel"; then
      echo "  allow  ${lines}  ${rel}  (grandfathered)"
    else
      echo "  FAIL   ${lines}  ${rel}"
      violations=$((violations + 1))
    fi
  fi
done < <(find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0)

echo "== Frontend JSX/JS guardrail (> ${FRONTEND_LIMIT} lines) =="
while IFS= read -r -d '' file; do
  rel="${file#./}"
  lines=$(wc -l < "$file")
  if [ "$lines" -gt "$FRONTEND_LIMIT" ]; then
    echo "  FAIL   ${lines}  ${rel}"
    violations=$((violations + 1))
  fi
done < <(find admin-dashboard/src subject-dashboard/src user-dashboard/src \( -name '*.jsx' -o -name '*.js' \) -print0)

if [ "$violations" -gt 0 ]; then
  echo ""
  echo "File-size guardrail failed with ${violations} violation(s). Split the file(s), or — for a deliberate"
  echo "single-class backend file — add it to BACKEND_ALLOWLIST in scripts/check-file-sizes.sh."
  exit 1
fi

echo ""
echo "File-size guardrail passed."
