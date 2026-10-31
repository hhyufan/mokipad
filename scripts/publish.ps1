param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
# Folder publishing keeps startup free of single-file extraction.
dotnet publish "$projectRoot\src\MokiPad\MokiPad.csproj" -c Release -r win-x64 --self-contained "$($SelfContained.IsPresent.ToString().ToLowerInvariant())" -p:PublishReadyToRun=true -o "$projectRoot\artifacts\app"
exit $LASTEXITCODE
