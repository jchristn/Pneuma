@echo off
REM Pneuma stack update entry point. Tears the stack down, rebuilds images, brings it
REM back up detached, then lists all containers. Run from anywhere; paths are anchored
REM to this script's directory so Compose always uses docker\compose.yaml.
setlocal
pushd "%~dp0"

echo [Pneuma] Stopping the stack...
docker compose down --remove-orphans

REM Compose uses fixed container_name values, so a leftover container (from a crash, a
REM concurrent compose run, or the factory stack) can survive `down` and then collide on
REM `up` with "container name is already in use". Force-remove any such leftovers by the
REM names this compose file declares before starting.
echo [Pneuma] Clearing any leftover fixed-name containers...
for /f "usebackq delims=" %%c in (`powershell -NoProfile -Command "(docker compose config --format json | ConvertFrom-Json).services.PSObject.Properties.Value | ForEach-Object { $_.container_name } | Where-Object { $_ }" 2^>nul`) do docker rm -f %%c >nul 2>&1

echo [Pneuma] Building images...
docker compose build
if errorlevel 1 goto :error

echo [Pneuma] Starting the stack (detached)...
docker compose up -d --remove-orphans
if errorlevel 1 goto :error

echo [Pneuma] Containers:
docker ps -a

popd
endlocal
exit /b 0

:error
echo [Pneuma] Update failed with error code %errorlevel%.
popd
endlocal
exit /b 1
