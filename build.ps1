# Builds CyberOptix from source. Run from the repo root:  powershell -ExecutionPolicy Bypass -File .\build.ps1
# Needs: Windows 11, the .NET 8 SDK, and the .NET Framework 4 compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host '1/2  Uninstaller'
Push-Location .\src\Uninstall
cmd /c ".\build.cmd"
Pop-Location

Write-Host '2/2  CyberOptix (Glass Cursor is built in as part of it)'
dotnet publish .\src\CyberOptix\CyberOptix.csproj -c Release -r win-x64 --self-contained -o .\publish
Write-Host 'Done. Output: .\publish\CyberOptix.exe'
