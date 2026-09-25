@echo off
rem Run the standard Pneuma benchmark suite against the bench stack (see README.md for setup).
rem   benchmarks\run-baseline.bat [label]      retrieval + answer + ingest + load (no API spend)
rem   set AGENT=1 to also run the agent benchmark (spends API credits); set REINGEST=1 to rebuild subjects.
setlocal
set HERE=%~dp0
pushd "%HERE%.."
set LABEL=%1
if "%LABEL%"=="" set LABEL=baseline
set EXTRA=--label %LABEL%
if "%REINGEST%"=="1" set EXTRA=%EXTRA% --reingest
set B=dotnet run --project src\Test.Benchmark -c Release --no-build --
dotnet build src\Test.Benchmark -c Release >nul || exit /b 1
%B% retrieval --dataset benchmarks\datasets\pneuma-live.json %EXTRA%
%B% retrieval --dataset benchmarks\datasets\meridian.json --profile lean %EXTRA%
%B% retrieval --dataset benchmarks\datasets\atlas.json --profile lean %EXTRA%
if exist benchmarks\data\multihoprag-300.json %B% retrieval --dataset benchmarks\data\multihoprag-300.json --profile lean %EXTRA%
if exist benchmarks\data\scifact.json %B% retrieval --dataset benchmarks\data\scifact.json --profile lean %EXTRA%
if exist benchmarks\data\nfcorpus.json %B% retrieval --dataset benchmarks\data\nfcorpus.json --profile lean %EXTRA%
%B% answer --dataset benchmarks\datasets\pneuma-live.json %EXTRA%
%B% ingest --dataset benchmarks\datasets\pneuma-live.json %EXTRA%
%B% load --stub --dataset benchmarks\datasets\meridian.json --concurrency 1,4,16,64 --duration 30 %EXTRA%
if "%AGENT%"=="1" %B% agent --tasks benchmarks\agent\tasks-pneuma.json --model haiku %EXTRA%
popd
