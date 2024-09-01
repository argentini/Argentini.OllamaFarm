if (Test-Path ".\Argentini.OllamaFarm\nupkg") { Remove-Item ".\Argentini.OllamaFarm\nupkg" -Recurse -Force }
. ./clean.ps1
Set-Location Argentini.OllamaFarm
dotnet pack --configuration Release
Set-Location ..
