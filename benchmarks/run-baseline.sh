#!/usr/bin/env bash
# Run the standard Pneuma benchmark suite against the bench stack (see README.md for setup).
#   benchmarks/run-baseline.sh [label]          retrieval + answer + ingest + load (no API spend)
#   AGENT=1 benchmarks/run-baseline.sh [label]  also runs the agent benchmark (spends API credits)
#   REINGEST=1 ...                               rebuild every subject first
set -u
here="$(cd "$(dirname "$0")" && pwd)"
cd "$here/.."
label="${1:-baseline}"
B="dotnet run --project src/Test.Benchmark -c Release --no-build --"
dotnet build src/Test.Benchmark -c Release >/dev/null
extra=(--label "$label")
if [[ "${REINGEST:-0}" == "1" ]]; then extra+=(--reingest); fi

$B retrieval --dataset benchmarks/datasets/pneuma-live.json "${extra[@]}"
$B retrieval --dataset benchmarks/datasets/meridian.json --profile lean "${extra[@]}"
$B retrieval --dataset benchmarks/datasets/atlas.json --profile lean "${extra[@]}"
if [[ -f benchmarks/data/multihoprag-300.json ]]; then $B retrieval --dataset benchmarks/data/multihoprag-300.json --profile lean "${extra[@]}"; fi
if [[ -f benchmarks/data/scifact.json ]]; then $B retrieval --dataset benchmarks/data/scifact.json --profile lean "${extra[@]}"; fi
if [[ -f benchmarks/data/nfcorpus.json ]]; then $B retrieval --dataset benchmarks/data/nfcorpus.json --profile lean "${extra[@]}"; fi

$B answer --dataset benchmarks/datasets/pneuma-live.json "${extra[@]}"
$B ingest --dataset benchmarks/datasets/pneuma-live.json "${extra[@]}"
$B load --stub --dataset benchmarks/datasets/meridian.json --concurrency 1,4,16,64 --duration 30 "${extra[@]}"

if [[ "${AGENT:-0}" == "1" ]]; then
  $B agent --tasks benchmarks/agent/tasks-pneuma.json --model "${AGENT_MODEL:-haiku}" "${extra[@]}"
fi
