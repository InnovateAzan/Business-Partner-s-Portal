Set-Location "$PSScriptRoot\frontend"
if (-not (Test-Path ".env")) { Copy-Item ".env.example" ".env" }
npm install
$env:FRONTEND_HOST = "0.0.0.0"
$env:FRONTEND_PORT = "8088"
npm run dev -- --host $env:FRONTEND_HOST --port $env:FRONTEND_PORT
