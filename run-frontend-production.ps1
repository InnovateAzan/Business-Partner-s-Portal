Set-Location "$PSScriptRoot\frontend"

$env:FRONTEND_HOST = "0.0.0.0"
$env:FRONTEND_PORT = "8088"

npm ci
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

npm run build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

npm run start:prod
