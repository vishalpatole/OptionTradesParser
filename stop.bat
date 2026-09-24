@echo off
setlocal

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo.
echo ============================================
echo   Stopping OptionTradesParser Stack
 echo ============================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$root = [Regex]::Escape('%ROOT%');" ^
  "$all = @(Get-CimInstance Win32_Process);" ^
  "$patterns = @(" ^
  "  $root + '.*OptionTradesParser\.csproj'," ^
  "  $root + '.*OptionTradesParser\.exe'," ^
  "  $root + '.*Dashboard\.Api'," ^
  "  $root + '.*dashboard-ui'," ^
  "  $root + '.*logs\\reconciliation\.log'," ^
  "  'OTP - OptionTradesParser'," ^
  "  'OTP - Dashboard API'," ^
  "  'OTP - React Dashboard'," ^
  "  'OTP - Reconciliation Log'" ^
  ");" ^
  "$matches = @($all | Where-Object {" ^
  "  if ($_.ProcessId -eq $PID -or -not $_.CommandLine) { $false }" ^
  "  else {" ^
  "    $matched = $false;" ^
  "    foreach ($pattern in $patterns) { if ($_.CommandLine -match $pattern) { $matched = $true; break } }" ^
  "    $matched" ^
  "  }" ^
  "});" ^
  "$listenerIds = @(5086,5173 | ForEach-Object { Get-NetTCPConnection -State Listen -LocalPort $_ -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique });" ^
  "$matches += @($all | Where-Object { $listenerIds -contains $_.ProcessId });" ^
  "if ($matches.Count -eq 0) { Write-Host 'No running stack processes found.'; exit 0 };" ^
  "$matchIds = @($matches.ProcessId);" ^
  "$roots = @($matches | Where-Object { $matchIds -notcontains $_.ParentProcessId });" ^
  "$stackRoots = New-Object System.Collections.Generic.List[object];" ^
  "foreach ($process in $roots) {" ^
  "  $ancestor = $process;" ^
  "  while ($ancestor.ParentProcessId) {" ^
  "    $parent = $all | Where-Object { $_.ProcessId -eq $ancestor.ParentProcessId } | Select-Object -First 1;" ^
  "    if (-not $parent -or -not $parent.CommandLine) { break }" ^
  "    if ($parent.Name -notin @('cmd.exe', 'powershell.exe', 'pwsh.exe', 'conhost.exe', 'WindowsTerminal.exe', 'wt.exe')) { break }" ^
  "    $ancestor = $parent;" ^
  "  }" ^
  "  if (-not ($stackRoots | Where-Object { $_.ProcessId -eq $ancestor.ProcessId })) { $stackRoots.Add($ancestor) | Out-Null }" ^
  "}" ^
  "foreach ($process in $stackRoots) {" ^
  "  Write-Host ('Stopping PID {0}: {1}' -f $process.ProcessId, $process.Name);" ^
  "  & taskkill.exe /PID $process.ProcessId /T /F | Out-Host" ^
  "}"

for %%P in (5086 5173) do (
    for /f "tokens=*" %%I in ('powershell -NoProfile -Command "Get-NetTCPConnection -State Listen -LocalPort %%P -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique"') do (
        echo Stopping remaining listener on port %%P, PID %%I...
        taskkill /PID %%I /T /F >nul 2>&1
    )
)

echo.
echo Stack stopped. SQLite connections should now be released.

endlocal
exit /b 0
