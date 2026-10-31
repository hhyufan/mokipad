param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Files)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project "$projectRoot\src\MokiPad\MokiPad.csproj" -- @Files
exit $LASTEXITCODE
