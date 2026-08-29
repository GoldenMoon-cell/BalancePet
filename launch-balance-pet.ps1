$ErrorActionPreference = "Stop"
$launcher = Join-Path $PSScriptRoot "launch-balance-pet-csharp.bat"
if (-not (Test-Path -LiteralPath $launcher)) {
  throw "C# launcher was not found"
}
Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "`"$launcher`"" -WorkingDirectory $PSScriptRoot
