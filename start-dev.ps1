$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Starting Business Partner's Portal..." -ForegroundColor Green
Start-Process powershell -ArgumentList '-NoExit','-Command',"cd `"$root\backend\BusinessPartnerPortal.Api`"; dotnet run"
Start-Sleep -Seconds 2
Start-Process powershell -ArgumentList '-NoExit','-Command',"cd `"$root\frontend`"; npm install; npm run dev"
Write-Host "Backend and frontend terminals started." -ForegroundColor Green
