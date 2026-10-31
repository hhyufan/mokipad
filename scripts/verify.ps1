$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project "$projectRoot\tests\MokiPad.Verification\MokiPad.Verification.csproj" -c Release -- "$projectRoot\artifacts"
exit $LASTEXITCODE
