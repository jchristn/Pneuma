@echo off
setlocal enabledelayedexpansion

REM ==========================================================================
REM reset.bat - Reset the Pneuma docker environment to factory defaults.
REM
REM Destroys all runtime docker data (Pneuma Postgres, LiteGraph, Less3, DocumentAtom,
REM Partio, RecallDB, Ollama, Prometheus, Grafana, and Tempo volumes), clears
REM logs/blobs/backups, restores factory config files (including every subordinate
REM service config), and leaves the stack ready for a fresh "docker compose up".
REM ==========================================================================

set "SCRIPT_DIR=%~dp0"
set "DOCKER_DIR=%SCRIPT_DIR%..\"
set "FACTORY_DIR=%SCRIPT_DIR%"
set "REPO_DIR=%DOCKER_DIR%..\"

echo.
echo ==========================================================
echo   Pneuma - Reset to Factory Defaults
echo ==========================================================
echo.
echo WARNING: This is DESTRUCTIVE. The following are deleted:
echo   - Pneuma PostgreSQL data volume (tenants, users, jobs, and the
echo     less3/litegraph/partio/recalldb databases)
echo   - LiteGraph, Less3, DocumentAtom, Partio, RecallDB, Ollama data volumes
echo   - Prometheus, Grafana, and Tempo data volumes
echo   - Pneuma logs, blobs, and backups
echo   - Config edits to pneuma.json, compose.yaml, prometheus.yaml, tempo.yaml,
echo     Grafana, and every subordinate service config (less3, litegraph,
echo     documentatom, partio, recalldb, postgres init)
echo.
echo A fresh "docker compose up -d" will re-seed the default administrator
echo (admin@pneuma / password).
echo.
set /p "CONFIRM=Type 'RESET' to confirm: "
echo.

if not "%CONFIRM%"=="RESET" (
    echo Aborted. No changes were made.
    exit /b 1
)

echo [1/4] Stopping containers and removing volumes...
pushd "%DOCKER_DIR%"
docker compose down -v 2>nul
popd

echo [2/4] Clearing runtime directories...
rd /s /q "%DOCKER_DIR%logs" 2>nul
rd /s /q "%DOCKER_DIR%blobs" 2>nul
rd /s /q "%DOCKER_DIR%backups" 2>nul
mkdir "%DOCKER_DIR%logs\pneuma" 2>nul
mkdir "%DOCKER_DIR%blobs" 2>nul
mkdir "%DOCKER_DIR%backups" 2>nul

echo [3/4] Restoring factory configuration...
copy /y "%FACTORY_DIR%pneuma.json" "%DOCKER_DIR%pneuma.json" >nul
copy /y "%FACTORY_DIR%prometheus.yaml" "%DOCKER_DIR%prometheus.yaml" >nul
copy /y "%FACTORY_DIR%tempo.yaml" "%DOCKER_DIR%tempo.yaml" >nul
copy /y "%FACTORY_DIR%compose.yaml" "%DOCKER_DIR%compose.yaml" >nul
mkdir "%DOCKER_DIR%less3" 2>nul
copy /y "%FACTORY_DIR%less3\system.json" "%DOCKER_DIR%less3\system.json" >nul
mkdir "%DOCKER_DIR%documentatom" 2>nul
copy /y "%FACTORY_DIR%documentatom\documentatom.json" "%DOCKER_DIR%documentatom\documentatom.json" >nul
mkdir "%DOCKER_DIR%litegraph" 2>nul
copy /y "%FACTORY_DIR%litegraph\litegraph.json" "%DOCKER_DIR%litegraph\litegraph.json" >nul
mkdir "%DOCKER_DIR%partio" 2>nul
copy /y "%FACTORY_DIR%partio\partio.json" "%DOCKER_DIR%partio\partio.json" >nul
mkdir "%DOCKER_DIR%recalldb" 2>nul
copy /y "%FACTORY_DIR%recalldb\recalldb.json" "%DOCKER_DIR%recalldb\recalldb.json" >nul
mkdir "%DOCKER_DIR%postgres\init" 2>nul
copy /y "%FACTORY_DIR%postgres\Dockerfile" "%DOCKER_DIR%postgres\Dockerfile" >nul
copy /y "%FACTORY_DIR%postgres\init\01-create-databases.sql" "%DOCKER_DIR%postgres\init\01-create-databases.sql" >nul
copy /y "%FACTORY_DIR%postgres\init\02-recalldb-extensions.sql" "%DOCKER_DIR%postgres\init\02-recalldb-extensions.sql" >nul
mkdir "%DOCKER_DIR%grafana\provisioning\datasources" 2>nul
mkdir "%DOCKER_DIR%grafana\provisioning\dashboards" 2>nul
copy /y "%FACTORY_DIR%grafana\provisioning\datasources\pneuma-datasources.yml" "%DOCKER_DIR%grafana\provisioning\datasources\pneuma-datasources.yml" >nul
copy /y "%FACTORY_DIR%grafana\provisioning\dashboards\pneuma-dashboards.yml" "%DOCKER_DIR%grafana\provisioning\dashboards\pneuma-dashboards.yml" >nul
mkdir "%REPO_DIR%assets\grafana" 2>nul
copy /y "%FACTORY_DIR%assets\grafana\pneuma-observability-dashboard.json" "%REPO_DIR%assets\grafana\pneuma-observability-dashboard.json" >nul

echo [4/4] Factory reset complete.
echo.
echo To start the environment:
echo   cd %DOCKER_DIR%
echo   docker compose up -d
echo.

endlocal
