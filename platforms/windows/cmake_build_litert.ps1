<#
.SYNOPSIS
    PowerShell port of cmake_build_tflite_x86.bat - configures and builds the tfliteextern
    native library via CMake + MSBuild/devenv (the Windows CMake path, as opposed to Bazel).

.DESCRIPTION
    Mirrors cmake_build_tflite_x86.bat line-for-line, including its quirks: the legacy
    VS2005-VS2015 / MSBuild 3.5/4.0/14.0 detection branches that will never match on a modern
    machine but are kept for fidelity (mirroring bazel_build_litert.ps1's MSBuild35/40
    handling), the $VSBuildTools and $CompileProcessCount variables the .bat computes but
    never actually uses, and the .bat's case-sensitive argument comparisons (IF without /I).

.PARAMETER ArchFlag
    "64" / "ARM" / "ARM64" (case-sensitive) select that architecture; anything else (including
    omitted) builds 32-bit x86 - matches the .bat's %1.

.PARAMETER XnnFlag
    Pass "xnn" (case-sensitive) to enable XNNPACK; anything else disables it - matches the
    .bat's %2.

.PARAMETER ProcessCountFlag
    Optional MSBuild parallel compile process count (e.g. "4") - matches the .bat's %3.

.EXAMPLE
    .\cmake_build_litert.ps1 64 xnn
#>
param(
    [Parameter(Position = 0)][string]$ArchFlag = "",
    [Parameter(Position = 1)][string]$XnnFlag = "",
    [Parameter(Position = 2)][string]$ProcessCountFlag = ""
)

$ErrorActionPreference = "Continue"

# %~p0 in the .bat always refers to the invoking script's own directory, regardless of later
# `cd`s. $PSScriptRoot has the same property, so capture it before changing location.
$ScriptDir = $PSScriptRoot

# pushd %~p0 & cd ..\..
Push-Location $ScriptDir
Set-Location (Join-Path $ScriptDir "..\..")

$BuildFolder = "build"
$OsMode = ""
$BuildArch = @()

# The .bat's string comparisons (IF without /I) are case-sensitive, so e.g. "arm" would not
# match and would silently fall through to the x86 default below - mirrored with -CaseSensitive.
switch -CaseSensitive ($ArchFlag) {
    "64" {
        $BuildFolder = "${BuildFolder}_x64"
        $OsMode = " Win64"
        $BuildArch = @("-A", "x64")
        Write-Host "BUILDING 64bit solution in $BuildFolder"
    }
    "ARM" {
        $BuildFolder = "${BuildFolder}_ARM"
        $OsMode = " ARM"
        $BuildArch = @("-A", "ARM")
        Write-Host "BUILDING ARM solution in $BuildFolder"
    }
    "ARM64" {
        $BuildFolder = "${BuildFolder}_ARM64"
        $OsMode = " ARM64"
        $BuildArch = @("-A", "ARM64")
        Write-Host "BUILDING ARM64 solution in $BuildFolder"
    }
    default {
        $BuildFolder = "${BuildFolder}_x86"
        $OsMode = ""
        $BuildArch = @("-A", "Win32")
        Write-Host "BUILDING 32bit solution in $BuildFolder"
    }
}

$CmakeXnnFlags = @("-DTFLITE_ENABLE_XNNPACK:BOOL=OFF")
if ($XnnFlag -ceq "xnn") { $CmakeXnnFlags = @("-DTFLITE_ENABLE_XNNPACK:BOOL=ON") }

$Project = "tfliteextern"

# $CompileProcessCount is computed here exactly like the .bat's COMPILE_PROCESS_COUNT, but -
# same as the .bat - it's never actually referenced anywhere afterward. Kept for fidelity.
$CompileProcessCount = $null
$MsbuildMultiprocess = "/m"
if ($ProcessCountFlag -ne "") {
    $CompileProcessCount = $ProcessCountFlag
    $MsbuildMultiprocess = "/m:$ProcessCountFlag"
}

$ProgramFilesX86 = ${env:ProgramFiles(x86)}
if (-not (Test-Path $ProgramFilesX86)) { $ProgramFilesX86 = $env:ProgramFiles }
$ProgramFilesDir = $env:ProgramFiles

# Find Python executable
$PythonExecutable = "python.exe"
if (Test-Path (Join-Path $ProgramFilesX86 "Anaconda2\python.exe")) { $PythonExecutable = Join-Path $ProgramFilesX86 "Anaconda2\python.exe" }
if (Test-Path (Join-Path $ProgramFilesX86 "Anaconda3\python.exe")) { $PythonExecutable = Join-Path $ProgramFilesX86 "Anaconda3\python.exe" }
if (Test-Path (Join-Path $ProgramFilesDir "Anaconda2\python.exe")) { $PythonExecutable = Join-Path $ProgramFilesDir "Anaconda2\python.exe" }
if (Test-Path (Join-Path $ProgramFilesDir "Anaconda3\python.exe")) { $PythonExecutable = Join-Path $ProgramFilesDir "Anaconda3\python.exe" }
if (Test-Path (Join-Path $ProgramFilesX86 "Microsoft Visual Studio\Shared\Anaconda3_64\python.exe")) { $PythonExecutable = Join-Path $ProgramFilesX86 "Microsoft Visual Studio\Shared\Anaconda3_64\python.exe" }
if (Test-Path "C:\Python38") { $PythonExecutable = "C:\Python38\python.exe" }
if (Test-Path "C:\Python310") { $PythonExecutable = "C:\Python310\python.exe" }

# Find Visual Studio or MSBuild
$VS2005 = "$($env:VS80COMNTOOLS)..\IDE\devenv.com"
$VS2008 = "$($env:VS90COMNTOOLS)..\IDE\devenv.com"
$VS2010 = "$($env:VS100COMNTOOLS)..\IDE\devenv.com"
$VS2012 = "$($env:VS110COMNTOOLS)..\IDE\devenv.com"
$VS2013 = "$($env:VS120COMNTOOLS)..\IDE\devenv.com"
$VS2015 = "$($env:VS140COMNTOOLS)..\IDE\devenv.com"

# vswhere can print more than one line when multiple installations of the same VS year are
# present (e.g. a Community edition and a BuildTools edition both installed). The original
# .bat's `FOR /F ... DO SET VAR=%%F` overwrites on each line, so the last line wins; `& vswhere`
# instead captures multi-line output as a string array, and interpolating an array into
# "$Var\..." below would silently join elements with a space into a garbled, nonexistent path.
# Select-Object -Last 1 reproduces the .bat's "last line wins" behavior and guarantees a single
# string (same fix applied in build_emgu_litert.ps1 / bazel_build_litert.ps1).
$VS2017Dir = & "miscellaneous\vswhere.exe" -version "[15.0,16.0)" -property installationPath 2>$null | Select-Object -Last 1
$VS2017 = "$VS2017Dir\Common7\IDE\devenv.com"

$VS2019Dir = & "miscellaneous\vswhere.exe" -version "[16.0,17.0)" -property installationPath 2>$null | Select-Object -Last 1
$VS2019 = "$VS2019Dir\Common7\IDE\devenv.com"

$VS2022Dir = & "miscellaneous\vswhere.exe" -version "[17.0,18.0)" -property installationPath 2>$null | Select-Object -Last 1
$VS2022 = "$VS2022Dir\Common7\IDE\devenv.com"

# $VSBuildTools is computed here exactly like the .bat's VS_BUILDTOOLS, but - same as the .bat -
# it's never actually referenced anywhere afterward. Kept for fidelity.
$VSBuildTools = & "miscellaneous\vswhere.exe" -products "*" -property installationPath 2>$null | Select-Object -Last 1

# Find CMake
$Cmake = "cmake.exe"
if (Test-Path (Join-Path $VS2022Dir "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe")) {
    $Cmake = Join-Path $VS2022Dir "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
}
if (Test-Path (Join-Path $ProgramFilesX86 "CMake 2.8\bin\cmake.exe")) { $Cmake = Join-Path $ProgramFilesX86 "CMake 2.8\bin\cmake.exe" }
if (Test-Path (Join-Path $ProgramFilesX86 "CMake\bin\cmake.exe")) { $Cmake = Join-Path $ProgramFilesX86 "CMake\bin\cmake.exe" }
if (Test-Path (Join-Path $ProgramFilesDir "CMake\bin\cmake.exe")) { $Cmake = Join-Path $ProgramFilesDir "CMake\bin\cmake.exe" }
if ($env:ProgramW6432 -and (Test-Path (Join-Path $env:ProgramW6432 "CMake\bin\cmake.exe"))) { $Cmake = Join-Path $env:ProgramW6432 "CMake\bin\cmake.exe" }

$MSBuild35 = $null
$MSBuild40 = $null
$MSBuild140 = $null
$MSBuild150 = $null
if (Test-Path "$env:windir\Microsoft.NET\Framework\v3.5\MSBuild.exe") { $MSBuild35 = "$env:windir\Microsoft.NET\Framework\v3.5\MSBuild.exe" }
if (Test-Path "$env:windir\Microsoft.NET\Framework64\v3.5\MSBuild.exe") { $MSBuild35 = "$env:windir\Microsoft.NET\Framework64\v3.5\MSBuild.exe" }
if (Test-Path "$env:windir\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe") { $MSBuild40 = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" }
if (Test-Path (Join-Path $ProgramFilesX86 "MSBuild\14.0\bin\MSBuild.exe")) { $MSBuild140 = Join-Path $ProgramFilesX86 "MSBuild\14.0\bin\MSBuild.exe" }
if (Test-Path (Join-Path $VS2017Dir "MSBuild\15.0\Bin\MSBuild.exe")) { $MSBuild150 = Join-Path $VS2017Dir "MSBuild\15.0\Bin\MSBuild.exe" }

# Each check overwrites unconditionally if it matches - same cascading "last/highest found
# wins" behavior as the .bat's sequence of independent IF EXIST statements (not elseif). Note
# the order (legacy MSBuild checked before the devenv.com paths, VS2005 through VS2022 in
# ascending order) matches the .bat exactly.
$Devenv = $null
if ($MSBuild35 -and (Test-Path $MSBuild35)) { $Devenv = $MSBuild35 }
if ($MSBuild40 -and (Test-Path $MSBuild40)) { $Devenv = $MSBuild40 }
if ($MSBuild140 -and (Test-Path $MSBuild140)) { $Devenv = $MSBuild140 }
if ($MSBuild150 -and (Test-Path $MSBuild150)) { $Devenv = $MSBuild150 }
if (Test-Path $VS2005) { $Devenv = $VS2005 }
if (Test-Path $VS2008) { $Devenv = $VS2008 }
if (Test-Path $VS2010) { $Devenv = $VS2010 }
if (Test-Path $VS2012) { $Devenv = $VS2012 }
if (Test-Path $VS2013) { $Devenv = $VS2013 }
if (Test-Path $VS2015) { $Devenv = $VS2015 }
if (Test-Path $VS2017) { $Devenv = $VS2017 }
if (Test-Path $VS2019) { $Devenv = $VS2019 }
if (Test-Path $VS2022) { $Devenv = $VS2022 }

# SET_BUILD_TYPE
$BuildTypeArgs = @()
if ($Devenv -eq $MSBuild35) { $BuildTypeArgs = @("/property:Configuration=Release", "/p:PreferredToolArchitecture=x64", $MsbuildMultiprocess, "/t:$Project") }
if ($Devenv -eq $MSBuild40) { $BuildTypeArgs = @("/property:Configuration=Release", "/p:PreferredToolArchitecture=x64", $MsbuildMultiprocess, "/t:$Project") }
if ($Devenv -eq $MSBuild140) { $BuildTypeArgs = @("/property:Configuration=Release", "/p:PreferredToolArchitecture=x64", $MsbuildMultiprocess, "/t:$Project") }
if ($Devenv -eq $MSBuild150) { $BuildTypeArgs = @("/property:Configuration=Release", "/p:PreferredToolArchitecture=x64", $MsbuildMultiprocess, "/t:$Project") }
if ($Devenv -eq $VS2005) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2008) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2010) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2012) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2013) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2015) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2017) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2019) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }
if ($Devenv -eq $VS2022) { $BuildTypeArgs = @("/Build", "Release", "/project", $Project) }

# SET_CLEAN_TYPE - $CleanTypeArgs is computed here exactly like the .bat's CLEAN_TYPE, but -
# same as the .bat, whose protobuf clean/rebuild steps that would have used it are commented
# out - it's never actually referenced anywhere afterward. Kept for fidelity.
$CleanTypeArgs = @()
if ($Devenv -eq $MSBuild35) { $CleanTypeArgs = @("/property:Configuration=Release", "/t:clean", $MsbuildMultiprocess) }
if ($Devenv -eq $MSBuild40) { $CleanTypeArgs = @("/property:Configuration=Release", "/t:clean", $MsbuildMultiprocess) }
if ($Devenv -eq $MSBuild140) { $CleanTypeArgs = @("/property:Configuration=Release", "/t:clean", $MsbuildMultiprocess) }
if ($Devenv -eq $MSBuild150) { $CleanTypeArgs = @("/property:Configuration=Release", "/t:clean", $MsbuildMultiprocess) }
if ($Devenv -eq $VS2005) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2008) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2010) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2012) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2013) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2015) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2017) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2019) { $CleanTypeArgs = @("/Clean", "Release") }
if ($Devenv -eq $VS2022) { $CleanTypeArgs = @("/Clean", "Release") }

# CMAKE_CONF: legacy devenv/MSBuild generators use the single-string "Visual Studio NN[ mode]"
# form; VS2019/2022 use the modern form (a plain generator name plus a separate -A <arch> flag)
# - kept as an array either way so it splats correctly into the cmake command line below.
$CmakeConf = @()
if ($Devenv -eq $MSBuild35) { $CmakeConf = @("Visual Studio 12$OsMode") }
if ($Devenv -eq $MSBuild40) { $CmakeConf = @("Visual Studio 12$OsMode") }
if ($Devenv -eq $MSBuild140) { $CmakeConf = @("Visual Studio 14$OsMode") }
if ($Devenv -eq $MSBuild150) { $CmakeConf = @("Visual Studio 15$OsMode") }
if ($Devenv -eq $VS2005) { $CmakeConf = @("Visual Studio 8 2005$OsMode") }
if ($Devenv -eq $VS2008) { $CmakeConf = @("Visual Studio 9 2008$OsMode") }
if ($Devenv -eq $VS2010) { $CmakeConf = @("Visual Studio 10$OsMode") }
if ($Devenv -eq $VS2012) { $CmakeConf = @("Visual Studio 11$OsMode") }
if ($Devenv -eq $VS2013) { $CmakeConf = @("Visual Studio 12$OsMode") }
if ($Devenv -eq $VS2015) { $CmakeConf = @("Visual Studio 14$OsMode") }
if ($Devenv -eq $VS2017) { $CmakeConf = @("Visual Studio 15$OsMode") }
if ($Devenv -eq $VS2019) { $CmakeConf = @("Visual Studio 16") + $BuildArch }
if ($Devenv -eq $VS2022) { $CmakeConf = @("Visual Studio 17") + $BuildArch }

Set-Location "litertextern\tfliteextern"

if (-not (Test-Path $BuildFolder)) { New-Item -ItemType Directory -Path $BuildFolder | Out-Null }
Set-Location $BuildFolder

& $Cmake ".." "-DCMAKE_BUILD_TYPE=Release" "-G" @CmakeConf @CmakeXnnFlags "-DPYTHON_EXECUTABLE=$PythonExecutable" "-DTFLITE_ENABLE_MMAP:BOOL=OFF" "-DTFLITE_ENABLE_NNAPI:BOOL=OFF" "-Dtensorflow_BUILD_PYTHON_BINDINGS:BOOL=OFF"

# build tfliteextern
& $Devenv @BuildTypeArgs "tfliteextern.sln"

Set-Location "..\..\.."

Pop-Location
