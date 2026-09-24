@echo off
setlocal

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

set "PARSER_PROJECT=%ROOT%\OptionTradesParser.csproj"
set "API_PROJECT=%ROOT%\Dashboard.Api\OptionTradesParser.Dashboard.Api.csproj"
set "UI_DIR=%ROOT%\dashboard-ui"

echo.
echo ============================================
echo   OptionTradesParser Stack
echo   1) Discord + IBKR execution engine
echo   2) Dashboard API on port 5086
echo   3) React dashboard on port 5173
echo ============================================
echo.

if not exist "%PARSER_PROJECT%" (
    echo ERROR: Parser project not found: %PARSER_PROJECT%
    exit /b 1
)

if not exist "%API_PROJECT%" (
    echo ERROR: Dashboard API project not found: %API_PROJECT%
    exit /b 1
)

if not exist "%UI_DIR%\package.json" (
    echo ERROR: Dashboard UI not found: %UI_DIR%
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet was not found on PATH.
    exit /b 1
)

where npm >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm was not found on PATH.
    exit /b 1
)

set "PARSER_PID="
for /f "tokens=*" %%I in ('powershell -NoProfile -Command "$root='%ROOT%'; Get-CimInstance Win32_Process ^| Where-Object { $_.Name -eq 'OptionTradesParser.exe' -and $_.ExecutablePath -like ($root + '*') } ^| Select-Object -First 1 -ExpandProperty ProcessId"') do set "PARSER_PID=%%I"
if defined PARSER_PID (
    echo ERROR: OptionTradesParser is already running as PID %PARSER_PID%.
    echo        Run stop.bat before starting another instance.
    exit /b 1
)

echo [1/3] Starting OptionTradesParser...
echo       Parser console: OTP - OptionTradesParser
echo.

set "READY_FILE=%TEMP%\otp-parser-ready-%RANDOM%%RANDOM%.txt"
set "FAIL_FILE=%TEMP%\otp-parser-failed-%RANDOM%%RANDOM%.txt"
set "PARSER_LAUNCHER=%TEMP%\otp-parser-launch-%RANDOM%%RANDOM%.cmd"
set "RECON_LOG=%ROOT%\logs\reconciliation.log"
if exist "%READY_FILE%" del "%READY_FILE%" >nul 2>&1
if exist "%FAIL_FILE%" del "%FAIL_FILE%" >nul 2>&1
if exist "%PARSER_LAUNCHER%" del "%PARSER_LAUNCHER%" >nul 2>&1
if not exist "%ROOT%\logs" mkdir "%ROOT%\logs" >nul 2>&1
if not exist "%RECON_LOG%" type nul > "%RECON_LOG%"

> "%PARSER_LAUNCHER%" echo @echo off
>> "%PARSER_LAUNCHER%" echo set "OTP_STARTUP_READY_FILE=%READY_FILE%"
>> "%PARSER_LAUNCHER%" echo set "OTP_STARTUP_FAIL_FILE=%FAIL_FILE%"
>> "%PARSER_LAUNCHER%" echo set "OTP_RECON_LOG_FILE=%RECON_LOG%"
>> "%PARSER_LAUNCHER%" echo cd /d "%ROOT%"
>> "%PARSER_LAUNCHER%" echo dotnet run --project "%PARSER_PROJECT%"

start "OTP - Reconciliation Log" /D "%ROOT%" powershell -NoProfile -NoExit -Command "Write-Host 'Reconciliation log: %RECON_LOG%'; Get-Content -Path '%RECON_LOG%' -Wait -Tail 60"
start "OTP - OptionTradesParser" /D "%ROOT%" "%ComSpec%" /k ""%PARSER_LAUNCHER%""

echo       Waiting for parser IBKR readiness before starting dashboard services...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ready='%READY_FILE%';" ^
    "$failed='%FAIL_FILE%';" ^
    "$deadline=(Get-Date).AddMinutes(8);" ^
    "while((Get-Date) -lt $deadline) {" ^
    "  if(Test-Path $ready) { Write-Host '      Parser startup confirmed.'; exit 0 }" ^
    "  if(Test-Path $failed) { Write-Host ('      Parser startup failed: ' + (Get-Content $failed -Raw)); exit 2 }" ^
    "  Start-Sleep -Seconds 1" ^
    "};" ^
    "Write-Host '      Parser startup timed out while waiting for IBKR readiness.'; exit 3"
if errorlevel 1 (
        echo ERROR: OptionTradesParser did not become ready. Dashboard API and React dashboard will not start.
        echo        Check the OTP - OptionTradesParser window for IBKR/TWS details.
        exit /b 1
)

set "API_PID="
for /f "tokens=*" %%I in ('powershell -NoProfile -Command "Get-NetTCPConnection -State Listen -LocalPort 5086 -ErrorAction SilentlyContinue ^| Select-Object -First 1 -ExpandProperty OwningProcess"') do set "API_PID=%%I"
if defined API_PID (
    echo [2/3] Dashboard API already listening as PID %API_PID%. Reusing it.
) else (
    echo [2/3] Starting dashboard API...
    start "OTP - Dashboard API" /D "%ROOT%" "%ComSpec%" /k dotnet run --project "%API_PROJECT%"
)

set "UI_PID="
for /f "tokens=*" %%I in ('powershell -NoProfile -Command "Get-NetTCPConnection -State Listen -LocalPort 5173 -ErrorAction SilentlyContinue ^| Select-Object -First 1 -ExpandProperty OwningProcess"') do set "UI_PID=%%I"
if defined UI_PID (
    echo [3/3] React dashboard already listening as PID %UI_PID%. Reusing it.
) else (
    echo [3/3] Starting React dashboard...
    if not exist "%UI_DIR%\node_modules" (
        echo       Installing frontend dependencies in the UI terminal...
        start "OTP - React Dashboard" /D "%UI_DIR%" "%ComSpec%" /k npm install ^&^& npm run dev -- --host 127.0.0.1 --port 5173
    ) else (
        start "OTP - React Dashboard" /D "%UI_DIR%" "%ComSpec%" /k npm run dev -- --host 127.0.0.1 --port 5173
    )
)

echo Stack launch commands complete. Run stop.bat to close dashboard services.
endlocal
