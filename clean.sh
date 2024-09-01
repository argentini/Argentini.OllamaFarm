# Delete all build files and restore dependencies from nuget servers
# ------------------------------------------------------------------

rm -r Argentini.OllamaFarm/bin
rm -r Argentini.OllamaFarm/obj

dotnet restore Argentini.OllamaFarm/Argentini.OllamaFarm.csproj
