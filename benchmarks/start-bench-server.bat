@echo off
rem Run Pneuma from the working tree against the bench stack (benchmarks\docker\compose.yaml).
rem REST + /metrics on http://127.0.0.1:28080. Logs, blobs, and the run log go to benchmarks\.run\.
setlocal
set HERE=%~dp0
if not exist "%HERE%.run" mkdir "%HERE%.run"
dotnet build "%HERE%..\src\Pneuma.Server\Pneuma.Server.csproj" -c Release -f net10.0 >nul || exit /b 1
pushd "%HERE%.run"
dotnet "%HERE%..\src\Pneuma.Server\bin\Release\net10.0\Pneuma.Server.dll" --config "%HERE%docker\pneuma.bench.json"
popd
