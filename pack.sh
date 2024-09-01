rm -r Argentini.OllamaFarm/nupkg
source clean.sh
cd Argentini.OllamaFarm
dotnet pack --configuration Release
cd ..
