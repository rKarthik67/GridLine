# Starts the Gridline API on http://localhost:5080 (.NET 10, picked by global.json in this folder).
# First time: copy src\Gridline.Api\appsettings.Development.private.example.json to
#             src\Gridline.Api\appsettings.Development.private.json and put your postgres password in it.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$private = 'src\Gridline.Api\appsettings.Development.private.json'
if (-not (Test-Path $private)) { throw "Create $private first (copy the .example.json next to it and fill in the password)." }
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src\Gridline.Api\Gridline.Api.csproj --no-launch-profile --urls http://localhost:5080
