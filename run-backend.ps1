Set-Location "$PSScriptRoot\backend\BusinessPartnerPortal.Api"
if (-not (Test-Path ".env")) { Copy-Item ".env.example" ".env" }
dotnet restore
dotnet run
