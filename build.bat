@echo off
setlocal enabledelayedexpansion
echo === Backend build (Pneuma.sln) ===
dotnet build src\Pneuma.sln -c Release
if errorlevel 1 exit /b 1

for %%d in (admin-dashboard subject-dashboard user-dashboard) do (
  echo === Dashboard build: %%d ===
  pushd %%d
  call npm ci
  if errorlevel 1 ( popd & exit /b 1 )
  call npm run build
  if errorlevel 1 ( popd & exit /b 1 )
  popd
)

echo Build complete.
