# Windows: build Spare Hand (stops a running copy first, since it locks the exe).
Get-Process SpareHand -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build "$PSScriptRoot\..\src\SpareHand\SpareHand.csproj" -c Debug -p:Platform=x64
