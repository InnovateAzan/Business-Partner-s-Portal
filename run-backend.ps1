Set-Location "$PSScriptRoot\backend\BusinessPartnerPortal.Api"
if (-not (Test-Path ".env")) { Copy-Item ".env.example" ".env" }
$listener = netstat -ano | Select-String '0\.0\.0\.0:5044\s+0\.0\.0\.0:0\s+LISTENING' | Select-Object -First 1
if ($listener) {
  $ownerPid = [int](($listener.ToString().Trim() -split '\s+')[-1])
  $process = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
  $name = if ($process) { $process.ProcessName } else { "unknown" }
  throw "Port 5044 is already in use by PID $ownerPid ($name). Stop that backend instance before starting another one."
}
dotnet restore
dotnet run
