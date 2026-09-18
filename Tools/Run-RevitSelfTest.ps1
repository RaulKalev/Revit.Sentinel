<#
.SYNOPSIS
    Runs the Sentinel in-Revit self-test (Sentinel.Diagnostics.SelfTest) in a fresh Revit session.

.DESCRIPTION
    1. Writes a temporary per-user add-in manifest pointing at the built Sentinel.dll.
    2. Starts Revit with SENTINEL_SELFTEST_OUTPUT set. The self-test builds its own test models in the output folder
       (it never opens your projects), runs, writes selftest-results.txt and closes Revit.
    3. Removes the temporary manifest and prints the results.

    Revit asks once whether to load the unsigned Sentinel add-in – choose "Load Once" (or "Always Load").
    Close other Revit sessions of the same version first if you want to avoid confusion (not required).

.EXAMPLE
    ./Tools/Run-RevitSelfTest.ps1 -RevitVersion 2026 -RevitExe "<path to Revit.exe>"
#>
param(
    [ValidateSet("2024", "2026")] [string] $RevitVersion = "2026",
    [string] $RevitExe,
    [string] $Output = (Join-Path $env:TEMP "SentinelSelfTest"),
    [int] $TimeoutMinutes = 20
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$framework = if ($RevitVersion -eq "2024") { "net48" } else { "net8.0-windows" }
$dll = Join-Path $repo "Sentinel\bin\Release\$framework\Sentinel.dll"
if (-not (Test-Path $dll)) {
    throw "Build first: dotnet build Sentinel/Sentinel.csproj -c Release -p:SentinelUseDefaultOutput=true (missing $dll)"
}

if (-not $RevitExe) {
    $candidates = @(
        "C:\Program Files\Autodesk\Revit $RevitVersion\Revit.exe",
        "D:\Revit $RevitVersion\Revit.exe",
        "E:\Revit $RevitVersion\Revit.exe")
    $RevitExe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $RevitExe) { throw "Revit.exe not found; pass -RevitExe." }
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$results = Join-Path $Output "selftest-results.txt"
if (Test-Path $results) { Remove-Item $results }

$addinDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
New-Item -ItemType Directory -Force -Path $addinDir | Out-Null
$manifest = Join-Path $addinDir "Sentinel.SelfTest.addin"
@"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Sentinel (self-test)</Name>
    <Assembly>$dll</Assembly>
    <FullClassName>Sentinel.App</FullClassName>
    <AddInId>8E2B7C41-5A93-4F0D-B6E1-3C9D2A7F5E18</AddInId>
    <VendorId>RaulKalev</VendorId>
    <VendorDescription>Raul Kalev (RK Tools)</VendorDescription>
  </AddIn>
</RevitAddIns>
"@ | Set-Content -Path $manifest -Encoding UTF8

try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo $RevitExe
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["SENTINEL_SELFTEST_OUTPUT"] = $Output
    $psi.EnvironmentVariables["SENTINEL_SELFTEST_EXIT"] = "1"
    Write-Host "Starting $RevitExe (self-test output: $Output) ..."
    $proc = [System.Diagnostics.Process]::Start($psi)

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while (-not (Test-Path $results) -and (Get-Date) -lt $deadline -and -not $proc.HasExited) { Start-Sleep -Seconds 5 }

    if (Test-Path $results) {
        Get-Content $results
        if (-not $proc.WaitForExit(120000)) { Write-Warning "Revit did not exit by itself; close it manually." }
    } else {
        Write-Warning "No results after $TimeoutMinutes min (was the unsigned add-in prompt answered?). Revit left running."
    }
}
finally {
    Remove-Item $manifest -ErrorAction SilentlyContinue
}
