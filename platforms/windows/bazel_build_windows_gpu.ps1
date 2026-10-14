#Requires -Version 5.1

<#
.SYNOPSIS
Builds matching Windows x64 LiteRT, WebGPU accelerator, and Dawn DLLs from source.
.DESCRIPTION
Stages pristine, pinned sources under .tmp/wgpu and applies gpu_source_patches there.
Does not edit submodules or install into the application's native runtime folder.
Requires Git, Bazel/Bazelisk, Python 3.11+, Visual Studio 2022/2026 C++ tools,
CMake, and a Windows SDK containing DXC, DXIL, and FXC. First run downloads dependencies.
.EXAMPLE
    .\bazel_build_windows_gpu.ps1
.EXAMPLE
    .\bazel_build_windows_gpu.ps1 -Jobs 8 -OutputDirectory E:\gpu-dlls
#>
[CmdletBinding()]
param(
    [string]$BuildRoot,
    [string]$OutputDirectory,
    [ValidateRange(1, 64)][int]$Jobs = 12,
    [string]$PythonExecutable,
    [string]$CMakeExecutable,
    [string]$WindowsSdkVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This script requires Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $BuildRoot) { $BuildRoot = Join-Path $repoRoot '.tmp/wgpu' }
$BuildRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($BuildRoot)
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.tmp')) + [IO.Path]::DirectorySeparatorChar
if (-not $BuildRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'BuildRoot must be a subdirectory of this repository''s .tmp folder.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $BuildRoot 'artifacts' }
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
foreach ($submoduleName in @('litert', 'LiteRT-LM')) {
    $submodulePath = Join-Path $repoRoot $submoduleName
    if ($OutputDirectory.Equals($submodulePath, [StringComparison]::OrdinalIgnoreCase) -or
        $OutputDirectory.StartsWith($submodulePath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory must not be inside a source submodule.'
    }
}
$litertRevision = '26895c9fbcc25c43faa8c1a98cd1fd28951602c3'
$lmRevision = 'b2f686e2ed4718fb84ec398a61dd59ca0f0aff27'
$driftRevision = '615fe9213315590e871e8474d7565d6a9c20a8e3'
$dawnTag = 'v20260720.160313'
$dawnArchiveHash = '8f2db59ad449043f5c80ac3d397963004c5ffa971bcc4ac50d53a0830aa485c1'
$patchDirectory = Join-Path $PSScriptRoot 'gpu_source_patches'
$logs = Join-Path $BuildRoot 'logs'
$cache = Join-Path $BuildRoot 'cache'
New-Item -ItemType Directory -Force -Path $BuildRoot, $logs, $cache | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-Utf8([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, $utf8)
}
function Find-Tool([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) { throw "Required tool not found: $Name" }
    return $command.Source
}
function Invoke-Tool([string]$Executable, [string[]]$ToolArguments, [string]$LogName) {
    $logPath = Join-Path $logs "$LogName.log"
    Write-Host "$LogName (log: $logPath)"
    # Windows PowerShell treats redirected native stderr as ErrorRecords. The exit code,
    # rather than warnings on stderr, determines whether a native build failed.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Executable @ToolArguments > $logPath 2>&1
        $toolExitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($toolExitCode -ne 0) { throw "$LogName failed (exit $toolExitCode). See $logPath" }
}
function Git-Text([string]$Repository, [string[]]$GitArguments) {
    $lines = & $git -C $Repository @GitArguments
    if ($LASTEXITCODE -ne 0) { throw "Git read failed in $Repository" }
    return ($lines -join "`n")
}
function Repository-Path([string]$Path) {
    return $Path.Substring($repoRoot.Length + 1).Replace('\', '/')
}
function Apply-SourcePatch([string]$Directory, [string]$PatchName, [switch]$CheckReverse) {
    $patchPath = Join-Path $patchDirectory $PatchName
    $patchArguments = @('-C', $repoRoot, 'apply', "--directory=$(Repository-Path $Directory)")
    if ($CheckReverse) {
        Invoke-Tool $git ($patchArguments + @('--check', '--reverse', $patchPath)) "check-$PatchName"
    } else {
        Invoke-Tool $git ($patchArguments + @('--check', $patchPath)) "check-$PatchName"
        Invoke-Tool $git ($patchArguments + @($patchPath)) "apply-$PatchName"
    }
}
function Prepare-Source([string]$Directory, [string]$Identity, [string]$PatchName, [scriptblock]$Extract) {
    $patchHash = if ($PatchName) { (Get-FileHash (Join-Path $patchDirectory $PatchName) -Algorithm SHA256).Hash } else { '' }
    $fingerprint = "staging-v1/$Identity/$patchHash"
    $statePath = Join-Path $Directory '.gpu-source-state.json'
    $state = $null
    if (Test-Path -LiteralPath $Directory) {
        if (-not (Test-Path -LiteralPath $statePath)) { throw "Unowned source folder: $Directory. Choose a new BuildRoot." }
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.fingerprint -ne $fingerprint) { throw "Source/patch version changed in $Directory. Choose a new BuildRoot." }
    } else {
        New-Item -ItemType Directory -Path $Directory | Out-Null
    }
    if ($state -and $state.ready) {
        if ($PatchName) { Apply-SourcePatch $Directory $PatchName -CheckReverse }
        return
    }
    Write-Utf8 $statePath (@{ fingerprint = $fingerprint; ready = $false } | ConvertTo-Json)
    # Re-extraction overwrites a partially prepared copy after an interrupted run.
    # Never delete/re-checkout the original submodules or a caller's source tree.
    & $Extract
    if ($PatchName) { Apply-SourcePatch $Directory $PatchName }
    Write-Utf8 $statePath (@{ fingerprint = $fingerprint; ready = $true } | ConvertTo-Json)
}
function Archive-GitSource([string]$Repository, [string]$Revision, [string]$Directory, [string]$Name) {
    $archivePath = Join-Path $cache "$Name.tar"
    Invoke-Tool $git @('-C', $Repository, 'archive', '--format=tar', "--output=$archivePath", $Revision) "archive-$Name"
    Invoke-Tool $tar @('-xf', $archivePath, '-C', $Directory) "extract-$Name"
}
function Assert-X64Dll([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 1024 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5A4D) { throw "Not a DLL: $Path" }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 24 -ge $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550 -or
        [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664 -or
        ([BitConverter]::ToUInt16($bytes, $peOffset + 22) -band 0x2000) -eq 0) { throw "Not an x64 PE DLL: $Path" }
}

$git = Find-Tool 'git.exe'
# Prefer the Windows tar executable: MSYS tar interprets E:/ paths as remote archives.
$tar = Join-Path $env:SystemRoot 'System32/tar.exe'
if (-not (Test-Path -LiteralPath $tar)) { $tar = Find-Tool 'tar.exe' }
$bazel = Find-Tool 'bazel.exe'
if (-not $PythonExecutable) { $PythonExecutable = Find-Tool 'python.exe' }
$PythonExecutable = (Get-Command $PythonExecutable -ErrorAction Stop).Source
$pythonVersion = & $PythonExecutable -c 'import sys; print(sys.version.split()[0])'
if ($LASTEXITCODE -ne 0 -or [version]$pythonVersion -lt [version]'3.11') { throw 'Python 3.11 or later is required.' }

foreach ($pin in @(@{ Folder = 'litert'; Revision = $litertRevision }, @{ Folder = 'LiteRT-LM'; Revision = $lmRevision })) {
    if ((Git-Text (Join-Path $repoRoot $pin.Folder) @('rev-parse', 'HEAD')) -ne $pin.Revision) {
        throw "$($pin.Folder) revision changed. Update and retest the GPU patches before building this version."
    }
}
$lmWorkspace = Git-Text (Join-Path $repoRoot 'LiteRT-LM') @('show', "${lmRevision}:WORKSPACE")
if ($lmWorkspace -notmatch ('LITERT_REF = "' + $litertRevision + '"')) { throw 'LiteRT-LM and LiteRT source pins do not match.' }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { $vswhere = Join-Path $repoRoot 'miscellaneous/vswhere.exe' }
$vsJson = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json
if ($LASTEXITCODE -ne 0) { throw 'Visual Studio discovery failed.' }
$visualStudios = @($vsJson -join "`n" | ConvertFrom-Json)
if ($visualStudios.Count -ne 1) { throw 'Install Visual Studio 2022/2026 with the Desktop development with C++ workload.' }
$visualStudio = $visualStudios[0]
$vsMajor = ([version]$visualStudio.installationVersion).Major
$generator = switch ($vsMajor) { 17 { 'Visual Studio 17 2022' } 18 { 'Visual Studio 18 2026' } default { throw "Unsupported Visual Studio major version: $vsMajor" } }
if (-not $CMakeExecutable) {
    $cmakeCommand = Get-Command 'cmake.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmakeCommand) { $CMakeExecutable = $cmakeCommand.Source }
    else { $CMakeExecutable = Join-Path $visualStudio.installationPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' }
}
if (-not (Test-Path -LiteralPath $CMakeExecutable)) { throw 'Install CMake or specify -CMakeExecutable.' }
$bash = Join-Path (Split-Path (Split-Path $git -Parent) -Parent) 'bin/bash.exe'
if (-not (Test-Path -LiteralPath $bash)) { throw 'Git for Windows bash.exe was not found beside Git.' }
$sdkBinRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
if (-not $WindowsSdkVersion) {
    $sdkVersions = @(Get-ChildItem -LiteralPath $sdkBinRoot -Directory | Where-Object {
        $_.Name -match '^10\.0\.\d+\.\d+$' -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'x64/dxcompiler.dll')) -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'x64/dxil.dll')) -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'x64/d3dcompiler_47.dll'))
    } | Sort-Object { [version]$_.Name } -Descending)
    if ($sdkVersions.Count -eq 0) { throw 'Install a Windows SDK containing x64 DXC, DXIL, and FXC compiler DLLs.' }
    $WindowsSdkVersion = $sdkVersions[0].Name
}
$sdkDirectory = Join-Path $sdkBinRoot "$WindowsSdkVersion/x64"
foreach ($compilerDll in @('dxcompiler.dll', 'dxil.dll', 'd3dcompiler_47.dll')) { Assert-X64Dll (Join-Path $sdkDirectory $compilerDll) }

$sourceLiteRt = Join-Path $BuildRoot 'litert'
$sourceLm = Join-Path $BuildRoot 'lm'
$sourceDrift = Join-Path $BuildRoot 'mld'
$sourceDawn = Join-Path $BuildRoot 'dawn'
$buildDawn = Join-Path $BuildRoot 'dawn-build'
$bundleDawn = Join-Path $BuildRoot 'dawn-bundle'
$outputBase = Join-Path $BuildRoot 'o'
$outputUserRoot = Join-Path $BuildRoot 'u'
$savedEnvironment = @{}
foreach ($name in @('BAZEL_VC', 'BAZEL_SH', 'PYTHON_BIN_PATH', 'HERMETIC_PYTHON_VERSION')) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $repoRoot
try {
    $env:BAZEL_VC = Join-Path $visualStudio.installationPath 'VC'
    $env:BAZEL_SH = $bash
    $env:PYTHON_BIN_PATH = $PythonExecutable
    $env:HERMETIC_PYTHON_VERSION = '3.13'
    Prepare-Source $sourceLiteRt $litertRevision 'litert.patch' { Archive-GitSource (Join-Path $repoRoot 'litert') $litertRevision $sourceLiteRt 'litert' }
    Prepare-Source $sourceLm $lmRevision '' { Archive-GitSource (Join-Path $repoRoot 'LiteRT-LM') $lmRevision $sourceLm 'lm' }

    $driftGit = Join-Path $cache 'ml-drift.git'
    if (-not (Test-Path -LiteralPath $driftGit)) { Invoke-Tool $git @('init', '--bare', $driftGit) 'init-ml-drift' }
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $git -C $driftGit cat-file -e "${driftRevision}^{commit}" 2>$null
        $haveDrift = $LASTEXITCODE -eq 0
    } finally { $ErrorActionPreference = $previousPreference }
    if (-not $haveDrift) { Invoke-Tool $git @('-C', $driftGit, 'fetch', '--depth=1', 'https://github.com/google-ai-edge/ml-drift.git', $driftRevision) 'fetch-ml-drift' }
    Prepare-Source $sourceDrift $driftRevision 'ml-drift.patch' { Archive-GitSource $driftGit $driftRevision $sourceDrift 'ml-drift' }
    Write-Utf8 (Join-Path $sourceDrift 'WORKSPACE') 'workspace(name = "ml_drift")'

    $dawnArchive = Join-Path $cache 'dawn.tar.gz'
    if (-not (Test-Path -LiteralPath $dawnArchive)) {
        Write-Host "Downloading Dawn $dawnTag"
        $previousTls = [Net.ServicePointManager]::SecurityProtocol
        $previousProgress = $ProgressPreference
        try {
            $ProgressPreference = 'SilentlyContinue'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/google/dawn/archive/$dawnTag.tar.gz" -OutFile "$dawnArchive.part" -TimeoutSec 300
            if ((Get-FileHash "$dawnArchive.part" -Algorithm SHA256).Hash -ne $dawnArchiveHash) { throw 'Dawn archive checksum mismatch.' }
            Move-Item -LiteralPath "$dawnArchive.part" -Destination $dawnArchive
        } finally {
            [Net.ServicePointManager]::SecurityProtocol = $previousTls
            $ProgressPreference = $previousProgress
        }
    }
    if ((Get-FileHash $dawnArchive -Algorithm SHA256).Hash -ne $dawnArchiveHash) { throw 'Cached Dawn archive checksum mismatch.' }
    Prepare-Source $sourceDawn "$dawnTag/$dawnArchiveHash" 'dawn-external-dxc.patch' { Invoke-Tool $tar @('-xf', $dawnArchive, '--strip-components=1', '-C', $sourceDawn) 'extract-dawn' }

    $dawnParallel = [Math]::Min(2, $Jobs)
    $dawnCompilerJobs = [Math]::Max(1, [Math]::Floor($Jobs / $dawnParallel))
    $cmakeArguments = @('-S', $sourceDawn, '-B', $buildDawn, '-G', $generator, '-A', 'x64',
        "-DCMAKE_SYSTEM_VERSION=$WindowsSdkVersion", "-DPython3_EXECUTABLE=$PythonExecutable", "-DCMAKE_CXX_FLAGS=/MP$dawnCompilerJobs /Zc:__cplusplus",
        '-DDAWN_FETCH_DEPENDENCIES=ON', '-DDAWN_BUILD_MONOLITHIC_LIBRARY=SHARED', '-DDAWN_ENABLE_D3D12=ON',
        '-DDAWN_USE_BUILT_DXC=ON', '-DDAWN_USE_EXTERNAL_DXC=ON', '-DDAWN_ENABLE_D3D11=OFF', '-DDAWN_ENABLE_VULKAN=OFF',
        '-DDAWN_ENABLE_DESKTOP_GL=OFF', '-DDAWN_ENABLE_OPENGLES=OFF', '-DDAWN_ENABLE_NULL=OFF', '-DDAWN_USE_GLFW=OFF',
        '-DDAWN_BUILD_TESTS=OFF', '-DDAWN_BUILD_SAMPLES=OFF', '-DDAWN_BUILD_PROTOBUF=OFF', '-DTINT_BUILD_TESTS=OFF', '-DTINT_BUILD_CMD_TOOLS=OFF')
    Invoke-Tool $CMakeExecutable $cmakeArguments 'configure-dawn'
    Invoke-Tool $CMakeExecutable @('--build', $buildDawn, '--config', 'Release', '--target', 'webgpu_dawn', '--parallel', "$dawnParallel") 'build-dawn'
    New-Item -ItemType Directory -Force -Path $bundleDawn | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceDawn 'include') -Destination $bundleDawn -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $buildDawn 'gen/include/dawn'), (Join-Path $buildDawn 'gen/include/webgpu') -Destination (Join-Path $bundleDawn 'include') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $buildDawn 'Release/webgpu_dawn.dll'), (Join-Path $buildDawn 'src/dawn/native/Release/webgpu_dawn.lib') -Destination $bundleDawn -Force
    Write-Utf8 (Join-Path $bundleDawn 'WORKSPACE') 'workspace(name = "dawn")'
    Write-Utf8 (Join-Path $bundleDawn 'BUILD') @'
package(default_visibility = ["//visibility:public"])
cc_import(name = "dll", interface_library = "webgpu_dawn.lib", shared_library = "webgpu_dawn.dll")
cc_library(name = "webgpu_dawn", hdrs = glob(["include/**/*.h"]), includes = ["include"], deps = [":dll"])
alias(name = "webgpu_headers", actual = ":webgpu_dawn")
alias(name = "dawn_headers", actual = ":webgpu_dawn")
alias(name = "libdawn_proc", actual = ":webgpu_dawn")
'@
    $driftPathLiteral = ConvertTo-Json -InputObject $sourceDrift.Replace('\', '/') -Compress
    $dawnPathLiteral = ConvertTo-Json -InputObject $bundleDawn.Replace('\', '/') -Compress
    Write-Utf8 (Join-Path $sourceLm 'WORKSPACE') ($lmWorkspace + "`nlocal_repository(name = `"ml_drift`", path = $driftPathLiteral, repo_mapping = {`"@fp16`": `"@FP16`"})`nlocal_repository(name = `"dawn`", path = $dawnPathLiteral)`n")
    $wrapper = Join-Path $sourceLm 'tfliteextern'
    New-Item -ItemType Directory -Force -Path $wrapper | Out-Null
    $wrapperBuild = [IO.File]::ReadAllText((Join-Path $repoRoot 'litertextern/tfliteextern/bazel/BUILD')).Replace('"//tflite', '"@litert//tflite').Replace('"//litert', '"@litert//litert')
    Write-Utf8 (Join-Path $wrapper 'BUILD') $wrapperBuild
    foreach ($wrapperSource in @('litertextern/tfliteextern/bazel/tfliteextern.def', 'litertextern/tfliteextern/bazel/litert_windows_exports.def', 'litertextern/tfliteextern/tfliteextern.cc', 'litertextern/tfliteextern/tfliteextern.h', 'litertextern/imgproc/imgproc.cc', 'litertextern/imgproc/imgproc.h')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $wrapperSource) -Destination $wrapper -Force
    }
    Set-Location $sourceLm
    $bazelVersion = & $bazel --version
    if ($LASTEXITCODE -ne 0 -or $bazelVersion -notmatch '^bazel 7\.6\.1$') { throw 'Bazel 7.6.1 is required. Use Bazelisk to honor LiteRT-LM/.bazelversion.' }
    $bazelArguments = @("--output_base=$outputBase", "--output_user_root=$outputUserRoot", 'build',
        "--override_repository=litert=$sourceLiteRt", '--config=windows', '--cpu=x64_windows', '--define=tflite_with_xnnpack=true',
        "--jobs=$Jobs", "--python_path=$PythonExecutable", '-c', 'opt', '//tfliteextern:libLiteRt',
        '@litert//litert/runtime/accelerators/gpu:libLiteRtWebGpuAccelerator', '--verbose_failures')
    Invoke-Tool $bazel $bazelArguments 'build-litert-gpu'

    $artifactSources = @(
        (Join-Path $sourceLm 'bazel-bin/tfliteextern/libLiteRt.dll'),
        (Join-Path $sourceLm 'bazel-bin/external/litert/litert/runtime/accelerators/gpu/libLiteRtWebGpuAccelerator.dll'),
        (Join-Path $bundleDawn 'webgpu_dawn.dll'),
        (Join-Path $sdkDirectory 'dxcompiler.dll'), (Join-Path $sdkDirectory 'dxil.dll'), (Join-Path $sdkDirectory 'd3dcompiler_47.dll'))
    foreach ($artifactSource in $artifactSources) { Assert-X64Dll $artifactSource }
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $artifacts = foreach ($artifactSource in $artifactSources) {
        $artifactName = Split-Path $artifactSource -Leaf
        $artifactDestination = Join-Path $OutputDirectory $artifactName
        Copy-Item -LiteralPath $artifactSource -Destination $artifactDestination -Force
        [ordered]@{ name = $artifactName; bytes = (Get-Item -LiteralPath $artifactDestination).Length; sha256 = (Get-FileHash -LiteralPath $artifactDestination -Algorithm SHA256).Hash }
    }
    $manifest = [ordered]@{
        builtAtUtc = [DateTime]::UtcNow.ToString('o'); platform = 'win-x64'
        litert = $litertRevision; litertLm = $lmRevision; mlDrift = $driftRevision; dawn = $dawnTag
        dawnArchiveSha256 = $dawnArchiveHash; visualStudio = $visualStudio.installationVersion
        windowsSdk = $WindowsSdkVersion; python = "$pythonVersion"; bazel = "$bazelVersion"
        patches = @(Get-ChildItem -LiteralPath $patchDirectory -Filter '*.patch' | ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
        artifacts = @($artifacts)
    }
    Write-Utf8 (Join-Path $OutputDirectory 'gpu-build.json') ($manifest | ConvertTo-Json -Depth 5)
    # Native source license notices are shipped alongside the DLL bundle.
    $licenseOutput = Join-Path $OutputDirectory 'licenses'
    New-Item -ItemType Directory -Force -Path $licenseOutput | Out-Null
    foreach ($notice in @(@($sourceLiteRt, 'LiteRT'), @($sourceDrift, 'ML-Drift'), @($sourceDawn, 'Dawn'))) {
        Copy-Item -LiteralPath (Join-Path $notice[0] 'LICENSE') -Destination (Join-Path $licenseOutput ($notice[1] + '.txt')) -Force
    }
    Copy-Item -LiteralPath (Join-Path $sourceDawn 'third_party/directx-shader-compiler/LICENSE') -Destination (Join-Path $licenseOutput 'DXC.txt') -Force
    Write-Host "Windows GPU build completed: $OutputDirectory"
} finally {
    Pop-Location
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
