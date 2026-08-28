@echo off
cd /d "%~dp0versions\csharp-wpf\bin\Release\net8.0-windows"
if not exist "BalancePet.Wpf.exe" (
  echo C# version has not been built yet. Run: dotnet build ..\..\..\BalancePet.Wpf.csproj
  pause
  exit /b 1
)
start "BalancePet C#" "BalancePet.Wpf.exe"
