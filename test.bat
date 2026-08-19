@echo off
setlocal
echo === Automated suite (console runner) ===
dotnet run --project src\Test.Automated\Test.Automated.csproj -f net8.0
if errorlevel 1 exit /b 1

echo === xUnit (per-case) ===
dotnet test src\Test.Xunit\Test.Xunit.csproj -f net8.0
if errorlevel 1 exit /b 1

echo === NUnit (per-case) ===
dotnet test src\Test.Nunit\Test.Nunit.csproj -f net8.0
if errorlevel 1 exit /b 1

echo === Dashboard tests (user-dashboard vitest) ===
pushd user-dashboard
call npm run test
popd

echo Tests complete.
