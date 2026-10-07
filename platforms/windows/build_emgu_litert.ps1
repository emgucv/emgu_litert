<#
.SYNOPSIS
    PowerShell port of build_emgutf.bat - configures and builds the Emgu.LiteRT Windows
    solution via CMake/MSBuild (the .NET side, docs, NuGet packages, and the CPack zip).

.DESCRIPTION
    Mirrors build_emgutf.bat line-for-line, including its quirks (documented at each spot
    below): the BUILD_TYPE variable it computes but never actually uses, and the
    "MOVE_*_SCRIPT" naming for what are actually copy (not move) steps.

.PARAMETER DocFlag
    Pass "doc" to also build documentation (Emgu.TF.Lite.Document) - matches the .bat's %1.

.PARAMETER NugetFlag
    Pass "nuget" to also build the NuGet package - matches the .bat's %2.

.PARAMETER PackageFlag
    Pass "package" to also build the CPack zip package - matches the .bat's %3.

.EXAMPLE
    .\build_emgu_litert.ps1 doc nuget package
#>
param(
    [Parameter(Position = 0)][string]$DocFlag = "",
    [Parameter(Position = 1)][string]$NugetFlag = "",
    [Parameter(Position = 2)][string]$PackageFlag = ""
)

$ErrorActionPreference = "Continue"

# %~p0 in the .bat always refers to the invoking script's own directory, regardless of later
# `cd`s. $PSScriptRoot has the same property, so capture it before changing location.
$ScriptDir = $PSScriptRoot

# Get-LatestVersionDir, Get-ProgramFilesPaths, Find-VisualStudioDevenv - shared with
# bazel_build_litert.ps1 and cmake_build_litert.ps1 (see _common.ps1).
. (Join-Path $ScriptDir "_common.ps1")

# pushd %~p0 & cd ..\..
Push-Location $ScriptDir
Set-Location (Join-Path $ScriptDir "..\..")

$HasTfLite = (Test-Path "lib\runtimes\win-x86\native\tfliteextern.dll") -or (Test-Path "lib\runtimes\win-x64\native\tfliteextern.dll")
$HasLitertLm = Test-Path "lib\runtimes\win-x64\native\liblitert-lm.dll"

# If we're a 32-bit process on 64-bit Windows, PROCESSOR_ARCHITEW6432 is set.
$Arch = $env:PROCESSOR_ARCHITECTURE
if ($env:PROCESSOR_ARCHITEW6432) { $Arch = $env:PROCESSOR_ARCHITEW6432 }

switch ($Arch.ToUpperInvariant()) {
    "AMD64" { Write-Host "Generating 64bit solution" }
    "ARM"   { Write-Host "Generating ARM solution" }
    "X86"   { Write-Host "Generating 32bit solution" }
    "ARM64" { Write-Host "Generating ARM64 solution" }
}

$OsMode = ""
switch ($Arch.ToUpperInvariant()) {
    "AMD64" { $OsMode = " Win64" }
    "ARM"   { $OsMode = " ARM" }
}

$BuildArch = @()
switch ($Arch.ToUpperInvariant()) {
    "AMD64" { $BuildArch = @("-A", "x64") }
    "X86"   { $BuildArch = @("-A", "Win32") }
    "ARM"   { $BuildArch = @("-A", "ARM") }
    "ARM64" { $BuildArch = @("-A", "ARM64") }
}

$ProgramFiles = Get-ProgramFilesPaths
$ProgramFilesX86 = $ProgramFiles.X86
$ProgramFilesDir = $ProgramFiles.Default

$BuildTools2019Folder = "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools"

# Find Visual Studio or Msbuild
$Vs = Find-VisualStudioDevenv
$VS2017Dir = $Vs.VS2017Dir; $VS2017 = $Vs.VS2017
$VS2019Dir = $Vs.VS2019Dir; $VS2019 = $Vs.VS2019
$VS2022Dir = $Vs.VS2022Dir; $VS2022 = $Vs.VS2022
$VS2026Dir = $Vs.VS2026Dir; $VS2026 = $Vs.VS2026

$MSBuildBuildTools2019 = $null
if (Test-Path (Join-Path $BuildTools2019Folder "MSBuild\Current\Bin\MSBuild.exe")) {
    $MSBuildBuildTools2019 = Join-Path $BuildTools2019Folder "MSBuild\Current\Bin\MSBuild.exe"
}

# Each check overwrites unconditionally if it matches - same cascading "last/highest found
# wins" behavior as the .bat's sequence of independent IF EXIST statements (not elseif). Note
# the order (2019 BuildTools checked between VS2019 and VS2022) matches the .bat exactly.
$Devenv = $null
if (Test-Path $VS2017) { $Devenv = $VS2017 }
if (Test-Path $VS2019) { $Devenv = $VS2019 }
if ($MSBuildBuildTools2019 -and (Test-Path $MSBuildBuildTools2019)) { $Devenv = $MSBuildBuildTools2019 }
if (Test-Path $VS2022) { $Devenv = $VS2022 }
if (Test-Path $VS2026) { $Devenv = $VS2026 }

# Find CMake
$Cmake = "cmake.exe"
if (Test-Path (Join-Path $ProgramFilesX86 "CMake\bin\cmake.exe")) { $Cmake = Join-Path $ProgramFilesX86 "CMake\bin\cmake.exe" }
if (Test-Path (Join-Path $ProgramFilesDir "CMake\bin\cmake.exe")) { $Cmake = Join-Path $ProgramFilesDir "CMake\bin\cmake.exe" }
if ($env:ProgramW6432 -and (Test-Path (Join-Path $env:ProgramW6432 "CMake\bin\cmake.exe"))) { $Cmake = Join-Path $env:ProgramW6432 "CMake\bin\cmake.exe" }
if (Test-Path (Join-Path $VS2022Dir "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe")) {
    $Cmake = Join-Path $VS2022Dir "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
}
if (Test-Path (Join-Path $VS2026Dir "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe")) {
    $Cmake = Join-Path $VS2026Dir "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
}

# SET_BUILD_TYPE
# $BuildType is computed here exactly like the .bat's BUILD_TYPE, but - same as the .bat -
# it's never actually referenced anywhere afterward. Kept for fidelity, not functionally used.
$BuildType = $null
if ($Devenv -eq $VS2017) { $BuildType = "/Build Release" }
if ($Devenv -eq $VS2019) { $BuildType = "/Build Release" }
if ($Devenv -eq $VS2022) { $BuildType = "/Build Release" }
if ($Devenv -eq $VS2026) { $BuildType = "/Build Release" }
if ($Devenv -eq $MSBuildBuildTools2019) { $BuildType = "/property:Configuration=Release" }

# CMAKE_CONF: VS2017 uses the legacy single-generator-name form ("Visual Studio 15 2017 Win64"),
# VS2019+ use the modern form (a plain generator name plus a separate -A <arch> flag) - kept as
# an array either way so it splats correctly into the cmake command line below.
$CmakeConf = @()
if ($Devenv -eq $VS2017) { $CmakeConf = @("Visual Studio 15$OsMode") }
if ($Devenv -eq $VS2019) { $CmakeConf = @("Visual Studio 16") + $BuildArch }
if ($Devenv -eq $MSBuildBuildTools2019) { $CmakeConf = @("Visual Studio 16") + $BuildArch }
if ($Devenv -eq $VS2022) { $CmakeConf = @("Visual Studio 17") + $BuildArch }
if ($Devenv -eq $VS2026) { $CmakeConf = @("Visual Studio 18") + $BuildArch }

# build EMGU TF
$CmakeConfFlags = @()
if ($DocFlag -eq "doc") { $CmakeConfFlags += "-DEMGU_TF_DOCUMENTATION_BUILD:BOOL=TRUE" }

if (-not (Test-Path "b")) { New-Item -ItemType Directory -Path "b" | Out-Null }
if (-not (Test-Path "package")) { New-Item -ItemType Directory -Path "package" | Out-Null }
Set-Location "b"

& $Cmake ".." "-G" @CmakeConf @CmakeConfFlags

# ALL_BUILD (which includes the Emgu.TF.Lite.Document custom target, marked ALL) is built in
# its own separate cmake --build invocation below, rather than being bundled into one
# multi-target call together with PACKAGE. MSBuild gives no ordering guarantee between
# unrelated top-level targets requested in a single invocation, and PACKAGE's CPack script
# installs the .chm by its exact file path with no CMake-level dependency forcing it to wait
# for the doc target - letting them run as one combined build caused an intermittent
# "file INSTALL cannot find ...Documentation.chm: File exists" CPack error when PACKAGE's
# install raced the doc target's SHFB compile.
$CmakeBuildTarget2 = @()
$DoMoveZip = $false
$DoMoveExe = $false
if ($PackageFlag -eq "package") {
    $CmakeBuildTarget2 += "PACKAGE"
    $DoMoveZip = $true
    $DoMoveExe = $true
}

$DoZipHelp = $false
if ($DocFlag -eq "doc") {
    # Emgu.TF.Lite.Document is already built (marked ALL) during the ALL_BUILD step above.
    # Requesting it again here, alongside PACKAGE, would rebuild it in the same MSBuild
    # invocation as CPack's install of the .chm - reintroducing the same race the
    # ALL_BUILD/PACKAGE split above was meant to fix (see comment there).
    if ($HasTfLite) { $DoZipHelp = $true }
}

$DoMoveNuget = $false
if ($NugetFlag -eq "nuget") {
    if ($HasTfLite) {
        $CmakeBuildTarget2 += "Emgu.LiteRT.runtime.windows.nuget"
        # tfliteextern.dll's own package, depending on the libLiteRt one above.
        $CmakeBuildTarget2 += "Emgu.TF.Lite.runtime.windows.nuget"
        $DoMoveNuget = $true
    }
    if ($HasLitertLm) {
        # liblitert-lm.dll's own package, depending on the libLiteRt one above. Unlike
        # Emgu.TF.Lite.runtime.windows.nuget (always defined whenever $HasTfLite, since
        # tfliteextern.dll is virtually guaranteed), Emgu.LiteRT.LM.runtime.windows.nuget's
        # CMakeLists.txt only defines this target at all when liblitert-lm.dll exists - so it's
        # only added to $CmakeBuildTarget2 under the same condition, to avoid cmake --build
        # erroring on an undefined target.
        $CmakeBuildTarget2 += "Emgu.LiteRT.LM.runtime.windows.nuget"
        $DoMoveNuget = $true
    }
}

Write-Host "BUILDING TARGET: ALL_BUILD"
& $Cmake "--build" "." "--config" "Release" "--target" "ALL_BUILD"

if ($CmakeBuildTarget2.Count -gt 0) {
    Write-Host "BUILDING TARGETS: $($CmakeBuildTarget2 -join ' ')"
    & $Cmake "--build" "." "--config" "Release" "--target" @CmakeBuildTarget2
}

if ($DoMoveZip) { Copy-Item "*.zip" "..\package" -Force -ErrorAction SilentlyContinue }
if ($DoMoveExe) { Copy-Item "*.exe" "..\package" -Force -ErrorAction SilentlyContinue }
Set-Location ".."
if ($DoMoveNuget) { Copy-Item "platforms\nuget\*.nupkg" "package" -Force -ErrorAction SilentlyContinue }
if ($DoZipHelp) { & zip "package\Help.zip" "-r" "Help" }

Pop-Location
