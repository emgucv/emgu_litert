<#
.SYNOPSIS
    PowerShell port of bazel_build_tflite_x86_64.bat - builds tfliteextern for Windows via Bazel.

.DESCRIPTION
    Mirrors bazel_build_tflite_x86_64.bat line-for-line, including its quirks (the VS2022/VS2026
    redist fall-through, and the dead BUILDTOOLS branch that compares against a variable the
    original .bat never actually sets - see the comments at each spot below). The one genuine
    functional addition is importing the vcvars*.bat environment into this process: a plain
    PowerShell `&` call to a .bat file does not persist environment changes back to the caller
    the way `call` does within another .bat file, so that import is done explicitly via a
    `cmd.exe /c "<vcvars> && set"` + parse, immediately after the point where the original does
    `call %ENV_SETUP_SCRIPT%`.

    Builds in the LiteRT-LM submodule's Bazel workspace rather than the `litert` submodule's -
    the same switch already made for bazel_build_tflite_macos and
    platforms/android/bazel_build_tflite_android, and for the same reason: LiteRT-LM's WORKSPACE
    fetches LiteRT as the external repository @litert, pinned to a newer commit than the `litert`
    submodule's own v2.2.0 tag.

.PARAMETER Arch
    "32", "64" (default), "ARM", or "ARM64" - matches the .bat's %1.

.PARAMETER XnnFlag
    Pass "xnn" to enable XNNPACK - matches the .bat's %2.

.PARAMETER DockerFlag
    Pass "docker" to build via the Bazel docker/remote executor - matches the .bat's %3.

.EXAMPLE
    .\bazel_build_litert.ps1 64 xnn
#>
param(
    [Parameter(Position = 0)][string]$Arch = "64",
    [Parameter(Position = 1)][string]$XnnFlag = "",
    [Parameter(Position = 2)][string]$DockerFlag = ""
)

$ErrorActionPreference = "Continue"

# %~dp0 in the .bat always refers to the invoking script's own directory, regardless of later
# `cd`s. $PSScriptRoot has the same property, so capture it before changing location.
$ScriptDir = $PSScriptRoot

# Get-LatestVersionDir, Get-ProgramFilesPaths, Find-VisualStudioDevenv, Find-LegacyMSBuild -
# shared with build_emgu_litert.ps1 and cmake_build_litert.ps1 (see _common.ps1).
. (Join-Path $ScriptDir "_common.ps1")

# pushd %~p0 & cd ../..
Push-Location $ScriptDir
Set-Location (Join-Path $ScriptDir "..\..")
$RepoRoot = Get-Location

$BuildFolder = "build_$Arch"
$BuildToolsFolder = "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools"

$CpuFlags = @()
$EnvSetupScript = $null

switch ($Arch) {
    "32" {
        Write-Host "BUILDING 32bit solution in $BuildFolder"
        $vcvars32 = Join-Path $BuildToolsFolder "vc\Auxiliary\Build\vcvars32.bat"
        if (Test-Path $vcvars32) { $EnvSetupScript = $vcvars32 }
        $CpuFlags = @("--host_cpu=x64_windows", "--cpu=x64_x86_windows", "--copt=/arch:IA32", "--linkopt=/MACHINE:x86", "--compiler=msvc-cl")
    }
    "64" {
        Write-Host "BUILDING 64bit solution in $BuildFolder"
        $vcvars64 = Join-Path $BuildToolsFolder "vc\Auxiliary\Build\vcvars64.bat"
        if (Test-Path $vcvars64) { $EnvSetupScript = $vcvars64 }
        # litert's .bazelrc has no win_clang config (only build:windows, targeting MSVC directly,
        # auto-applied via --enable_platform_specific_config). --config=windows is passed
        # explicitly for clarity even though Bazel would select it automatically on this host.
        $CpuFlags = @("--config=windows")
    }
    "ARM" {
        Write-Host "BUILDING ARM solution in $BuildFolder"
        $vcvarsArm = Join-Path $BuildToolsFolder "vc\Auxiliary\Build\vcvarsamd64_arm.bat"
        if (Test-Path $vcvarsArm) { $EnvSetupScript = $vcvarsArm }
        $CpuFlags = @("--config=win_clang")
    }
    "ARM64" {
        Write-Host "BUILDING ARM64 solution in $BuildFolder"
        $vcvarsArm64 = Join-Path $BuildToolsFolder "vc\Auxiliary\Build\vcvarsamd64_arm64.bat"
        if (Test-Path $vcvarsArm64) { $EnvSetupScript = $vcvarsArm64 }
        $CpuFlags = @("--host_cpu=x64_windows", "--cpu=x64_arm64_windows", "--linkopt=/MACHINE:ARM64", "--compiler=msvc-cl")
    }
    default {
        # Unrecognized arch: the .bat falls straight through to ENV_END with CPU_FLAGS empty
        # and no env setup script. Mirror that rather than erroring out.
    }
}

# `call %ENV_SETUP_SCRIPT%` - import the vcvars environment (PATH/INCLUDE/LIB/etc.) into this
# process. A bare `& $EnvSetupScript` would not do this: cmd.exe's `call` only propagates
# environment changes back to its *own* caller when that caller is itself a .bat file, and even
# then only because both share the same cmd.exe instance. Route through cmd.exe here and apply
# its resulting environment to this PowerShell process instead.
if ($EnvSetupScript) {
    cmd.exe /c "`"$EnvSetupScript`" && set" | ForEach-Object {
        if ($_ -match '^([^=]+)=(.*)$') {
            [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2])
        }
    }
}

$TfliteWithXnnpack = "false"
if ($XnnFlag -eq "xnn") { $TfliteWithXnnpack = "true" }
$BazelXnnFlags = @("--define", "tflite_with_xnnpack=$TfliteWithXnnpack")

if ($DockerFlag -eq "docker") {
    $DockerFlags = @("--define=EXECUTOR=remote", "--experimental_docker_verbose", "--experimental_enable_docker_sandbox")
    $OutputUserRootDir = "c:\bazel_output_user_root"
    $OutputBaseDir = "c:\bazel_output_base"
} else {
    $DockerFlags = @()
    $OutputUserRootDir = Join-Path $ScriptDir "output_user_root"
    $OutputBaseDir = Join-Path $ScriptDir "output_base"
}

if (-not (Test-Path $OutputUserRootDir)) { New-Item -ItemType Directory -Path $OutputUserRootDir | Out-Null }
if (-not (Test-Path $OutputBaseDir)) { New-Item -ItemType Directory -Path $OutputBaseDir | Out-Null }

$ProgramFiles = Get-ProgramFilesPaths
$ProgramFilesX86 = $ProgramFiles.X86
$ProgramFilesDir = $ProgramFiles.Default

# Find Visual Studio or Msbuild
$Vs = Find-VisualStudioDevenv
$VS2017Dir = $Vs.VS2017Dir; $VS2017 = $Vs.VS2017
$VS2019Dir = $Vs.VS2019Dir; $VS2019 = $Vs.VS2019
$VS2022Dir = $Vs.VS2022Dir; $VS2022 = $Vs.VS2022
$VS2026Dir = $Vs.VS2026Dir; $VS2026 = $Vs.VS2026

$Legacy = Find-LegacyMSBuild
$MSBuild35 = $Legacy.MSBuild35
$MSBuild40 = $Legacy.MSBuild40

# Each check overwrites unconditionally if it matches - same cascading "last/highest found wins"
# behavior as the .bat's sequence of independent IF EXIST statements (not elseif).
$Devenv = $null
if ($MSBuild35 -and (Test-Path $MSBuild35)) { $Devenv = $MSBuild35 }
if ($MSBuild40 -and (Test-Path $MSBuild40)) { $Devenv = $MSBuild40 }
if (Test-Path $VS2017) { $Devenv = $VS2017 }
if (Test-Path $VS2019) { $Devenv = $VS2019 }
if (Test-Path $VS2022) { $Devenv = $VS2022 }
if (Test-Path $VS2026) { $Devenv = $VS2026 }
if (Test-Path $BuildToolsFolder) { $Devenv = $BuildToolsFolder }

# Get full path to the running Python executable
$PyExe = (& python -c "import sys; print(sys.executable)" 2>$null)
# Split-Path throws on $null/empty input (unlike the .bat's FOR /F, which just leaves PYEXE/
# PYTHON_BASE_PATH blank when python isn't found) - guard it so a missing python doesn't abort
# the whole script here.
$PythonBasePath = if ($PyExe) { Split-Path $PyExe -Parent } else { "" }
$HermeticPythonVersion = $null

if (Test-Path "$ProgramFilesX86\Microsoft Visual Studio\Shared\Python37_64") { $PythonBasePath = "$ProgramFilesX86\Microsoft Visual Studio\Shared\Python37_64" }
if (Test-Path "C:\Python312") { $PythonBasePath = "C:\Python312"; $HermeticPythonVersion = "3.12" }
if (Test-Path "C:\python-virt\python312") { $PythonBasePath = "C:\python-virt\python312"; $HermeticPythonVersion = "3.12" }

Write-Host "PYTHON_BASE_PATH=$PythonBasePath"

$PythonBinPath = Join-Path $PythonBasePath "python.exe"
if (-not (Test-Path $PythonBinPath) -and (Test-Path (Join-Path $PythonBasePath "Scripts\python.exe"))) {
    $PythonBinPath = Join-Path $PythonBasePath "Scripts\python.exe"
}
$PythonLibPath = Join-Path $PythonBasePath "lib\site-packages"

$PythonBasePath = $PythonBasePath -replace '\\', '/'
$PythonBinPath = $PythonBinPath -replace '\\', '/'
$PythonLibPath = $PythonLibPath -replace '\\', '/'

# The .bat's SET PYTHON_BASE_PATH=... etc. create real process environment variables that
# bazel.exe (a child process in the same cmd.exe session) inherits automatically. Plain
# PowerShell `$Var = ...` assignments are local script variables only and have no effect on
# child processes, so they must be mirrored into $env: explicitly to actually reach Bazel.
$env:PYTHON_BASE_PATH = $PythonBasePath
$env:PYTHON_BIN_PATH = $PythonBinPath
$env:PYTHON_LIB_PATH = $PythonLibPath
if ($HermeticPythonVersion) { $env:HERMETIC_PYTHON_VERSION = $HermeticPythonVersion }

# SET_BAZEL_VS_VC
$BazelVs = $null
if ($Devenv -eq $VS2017) { $BazelVs = $VS2017 -replace '\\Common7\\IDE\\devenv\.com$', '' }
if ($Devenv -eq $VS2019) { $BazelVs = $VS2019 -replace '\\Common7\\IDE\\devenv\.com$', '' }
if ($Devenv -eq $VS2022) { $BazelVs = $VS2022 -replace '\\Common7\\IDE\\devenv\.com$', '' }
if ($Devenv -eq $VS2026) { $BazelVs = $VS2026 -replace '\\Common7\\IDE\\devenv\.com$', '' }
if ($Devenv -eq $BuildToolsFolder) { $BazelVs = $BuildToolsFolder }

$BazelVc = $null
if ($BazelVs) { $BazelVc = Join-Path $BazelVs "VC" }
Write-Host "Using BAZEL_VC=$BazelVc"

$BazelLlvm = $null
if (Test-Path "C:\Program Files\LLVM\bin") { $BazelLlvm = "C:\Program Files\LLVM" }
if ($BazelVc) { $BazelLlvm = Join-Path $BazelVc "Tools\Llvm\x64" }
Write-Host "Using BAZEL_LLVM=$BazelLlvm"

# Build in the LiteRT-LM submodule's Bazel workspace, against the LiteRT it pins (its WORKSPACE
# fetches LiteRT as the external repository @litert) - same as bazel_build_tflite_macos and
# platforms/android/bazel_build_tflite_android. LiteRT-LM is a pristine third-party submodule (we
# don't own/patch it upstream), so the tfliteextern Bazel package can't live there in git either:
# stage our tracked source into it here instead, each run. Our BUILD file is written for the
# litert workspace, where LiteRT's packages are local (//tflite/..., //litert/...); rewrite those
# labels to @litert//... while staging, the same way the macOS/Android scripts do via sed.
$WorkspaceDir = "LiteRT-LM"
$TfliteexternStageDir = Join-Path $WorkspaceDir "tfliteextern"
if (-not (Test-Path $TfliteexternStageDir)) { New-Item -ItemType Directory -Path $TfliteexternStageDir | Out-Null }
(Get-Content "litertextern\tfliteextern\bazel\BUILD" -Raw) `
    -replace '"//tflite', '"@litert//tflite' `
    -replace '"//litert', '"@litert//litert' |
    Set-Content -Path (Join-Path $TfliteexternStageDir "BUILD") -NoNewline
Copy-Item "litertextern\tfliteextern\bazel\tfliteextern.def" (Join-Path $TfliteexternStageDir "tfliteextern.def") -Force
Copy-Item "litertextern\tfliteextern\bazel\litert_windows_exports.def" (Join-Path $TfliteexternStageDir "litert_windows_exports.def") -Force
Copy-Item "litertextern\tfliteextern\tfliteextern.cc" (Join-Path $TfliteexternStageDir "tfliteextern.cc") -Force
Copy-Item "litertextern\tfliteextern\tfliteextern.h" (Join-Path $TfliteexternStageDir "tfliteextern.h") -Force
Copy-Item "litertextern\imgproc\imgproc.cc" (Join-Path $TfliteexternStageDir "imgproc.cc") -Force
Copy-Item "litertextern\imgproc\imgproc.h" (Join-Path $TfliteexternStageDir "imgproc.h") -Force

Set-Location $WorkspaceDir

$BazelCommand = "bazel.exe"
$MsysPath = "C:\msys64"
$MsysBin = Join-Path $MsysPath "usr\bin"
if (Test-Path (Join-Path $MsysBin "bazel.exe")) { $BazelCommand = Join-Path $MsysBin "bazel.exe" }

$CommonBazelArgs = @(
    "--output_base=$OutputBaseDir",
    "--output_user_root=$OutputUserRootDir",
    "build",
    "--repo_env=BAZEL_LLVM=$BazelLlvm"
) + $CpuFlags + $BazelXnnFlags + $DockerFlags + @("-c", "opt")

& $BazelCommand @CommonBazelArgs "@litert//tflite:version" "--verbose_failures"

& $BazelCommand @CommonBazelArgs "//tfliteextern:tfliteextern" "--verbose_failures"

# LiteRT's own runtime (LiteRT's C API plus the TensorFlow Lite C API), P/Invoked directly by the
# Emgu.LiteRT classes - same purpose as libLiteRt.dylib/.so on macOS/Android. tfliteextern.dll
# itself still links the TFLite C API statically on Windows (see litertextern/tfliteextern/
# bazel/BUILD's comment on the ":libLiteRt" target for why), so this is an independent artifact,
# not something tfliteextern.dll depends on here.
& $BazelCommand @CommonBazelArgs "//tfliteextern:libLiteRt" "--verbose_failures"

Set-Location ".."

$NativeOutDir = "lib\runtimes\win-x64\native"
if (-not (Test-Path $NativeOutDir)) { New-Item -ItemType Directory -Path $NativeOutDir -Force | Out-Null }
Copy-Item (Join-Path $WorkspaceDir "bazel-bin\tfliteextern\tfliteextern.dll") "$NativeOutDir\tfliteextern.dll" -Force
Copy-Item (Join-Path $WorkspaceDir "bazel-bin\tfliteextern\libLiteRt.dll") "$NativeOutDir\libLiteRt.dll" -Force
# Record the tflite_with_xnnpack define next to the dll. The top level CMakeLists.txt reads it
# to set EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK, and it travels with the binary in the zip package.
Set-Content -Path "$NativeOutDir\tflite_with_xnnpack.txt" -Value $TfliteWithXnnpack -NoNewline:$false

# START_OF_MSVC_DEPENDENCY
if ($BazelVc) {
    if ($Devenv -eq $VS2017) {
        $latestDir = Get-LatestVersionDir -Path (Join-Path $BazelVc "Redist\MSVC")
        $vs2017Redist = if ($latestDir) { Join-Path $latestDir.FullName "x64\Microsoft.VC141.CRT" }
        if ($vs2017Redist) {
            Copy-Item (Join-Path $vs2017Redist "*140.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
            Copy-Item (Join-Path $vs2017Redist "*140_1.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
            Copy-Item (Join-Path $vs2017Redist "*140_2.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
        }
    }
    elseif ($Devenv -eq $VS2019) {
        $latestDir = Get-LatestVersionDir -Path (Join-Path $BazelVc "Redist\MSVC") -Filter "14*"
        $vs2019Redist = if ($latestDir) { Join-Path $latestDir.FullName "x64\Microsoft.VC142.CRT" }
        if ($vs2019Redist) {
            Copy-Item (Join-Path $vs2019Redist "*.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
        }
    }
    elseif ($Devenv -eq $VS2022) {
        # No GOTO between the VS2022 and VS2026 blocks in the original .bat - VS2022 falls
        # through and copies the VS2026 (VC145.CRT) redist too. Mirrored, not fixed.
        $latestDir = Get-LatestVersionDir -Path (Join-Path $BazelVc "Redist\MSVC") -Filter "14*"
        $vs2022Redist = if ($latestDir) { Join-Path $latestDir.FullName "x64\Microsoft.VC143.CRT" }
        if ($vs2022Redist) {
            Copy-Item (Join-Path $vs2022Redist "*.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
        }
        $vs2026Redist = if ($latestDir) { Join-Path $latestDir.FullName "x64\Microsoft.VC145.CRT" }
        if ($vs2026Redist) {
            Copy-Item (Join-Path $vs2026Redist "*.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
        }
    }
    elseif ($Devenv -eq $VS2026) {
        $latestDir = Get-LatestVersionDir -Path (Join-Path $BazelVc "Redist\MSVC") -Filter "14*"
        $vs2026Redist = if ($latestDir) { Join-Path $latestDir.FullName "x64\Microsoft.VC145.CRT" }
        if ($vs2026Redist) {
            Copy-Item (Join-Path $vs2026Redist "*.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
        }
    }
    elseif ($Devenv -eq $BuildTools) {
        # The .bat's last check here is "IF %DEVENV%==%BUILDTOOLS% GOTO VS2019_DEPENDENCY", but
        # %BUILDTOOLS% is never actually set anywhere in it (the real variable is
        # %BUILD_TOOLS_FOLDER%), so that comparison is always against an empty string and this
        # branch is dead in the original too. $BuildTools is deliberately left unassigned
        # (always $null) here to mirror that exactly, rather than "fixing" it.
        $latestDir = Get-LatestVersionDir -Path (Join-Path $BazelVc "Redist\MSVC") -Filter "14*"
        $vs2019Redist = if ($latestDir) { Join-Path $latestDir.FullName "x64\Microsoft.VC142.CRT" }
        if ($vs2019Redist) {
            Copy-Item (Join-Path $vs2019Redist "*.dll") $NativeOutDir -Force -ErrorAction SilentlyContinue
        }
    }
    # else: DEVENV matched none of the above -> no-op, same as GOTO END_OF_MSVC_DEPENDENCY
}
# END_OF_MSVC_DEPENDENCY

Pop-Location
