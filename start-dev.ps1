$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Starting Business Partner's Portal..." -ForegroundColor Green
Start-Process powershell -ArgumentList '-NoExit','-Command',"& `"$root\run-backend.ps1`""
Start-Sleep -Seconds 2
Start-Process powershell -ArgumentList '-NoExit','-Command',"cd `"$root\frontend`"; npm install; npm run dev"
Write-Host "Backend and frontend terminals started." -ForegroundColor Green
