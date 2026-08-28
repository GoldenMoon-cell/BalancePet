$ErrorActionPreference = "Stop"
$pythonwPath = Join-Path $env:LocalAppData "Programs\Python\Python314\pythonw.exe"
if (-not (Test-Path -LiteralPath $pythonwPath)) {
  $pythonwPath = (Get-Command pythonw.exe -ErrorAction SilentlyContinue).Source
}
if (-not $pythonwPath -or -not (Test-Path -LiteralPath $pythonwPath)) {
  throw "Python 3.14 with PySide6 was not found"
}
Start-Process -FilePath $pythonwPath -ArgumentList "`"$PSScriptRoot\versions\python\balance_pet_qt.py`" --follow-codex" -WorkingDirectory (Join-Path $PSScriptRoot "versions\python")
