#Requires -Version 5.1

<#
.SYNOPSIS
Builds the Windows x64 WebGPU bundle: LiteRT, its WebGPU accelerator and Dawn, plus the shader compilers.
.DESCRIPTION
Since LiteRT-LM v0.19.0 (LiteRT dcb657e8) the accelerator builds from the pinned sources unpatched, in the
same Bazel invocation as liblitert-lm.dll: bazel_build_litert.ps1 64 -Gpu. That matters, because the accelerator
reads private TFLite C++ members across the DLL boundary, so it has to be compiled with the compiler and flags
of the TFLite objects it is handed (those inside liblitert-lm.dll for the embedding models); Google's prebuilt
accelerator, built differently, crashes there (SEHException in litert_lm_embedding_engine_create). Dawn is
LiteRT-LM's prebuilt webgpu_dawn.dll (Direct3D 12, DXC), which bazel_build_litert.ps1 copies next to
liblitert-lm.dll. Before v0.19.0 this script staged patched sources under .tmp/wgpu and built Dawn with CMake;
see INVESTIGATION.md.

This script runs that build (unless -SkipBuild), then collects the six DLLs - libLiteRt.dll,
libLiteRtWebGpuAccelerator.dll, webgpu_dawn.dll and the Windows SDK's dxcompiler.dll, dxil.dll and
d3dcompiler_47.dll - into the output folder with gpu-build.json (the revisions and hashes that
validate_windows_gpu.ps1 and CMake check) and the licence notices. It does not edit the submodules.
Needs everything bazel_build_litert.ps1 needs, plus a Windows SDK with the three compiler DLLs.
.EXAMPLE
    .\bazel_build_windows_gpu.ps1
.EXAMPLE
    .\bazel_build_windows_gpu.ps1 -SkipBuild -OutputDirectory E:\gpu-dlls
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$WindowsSdkVersion,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This script requires Windows.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.tmp'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $temporaryRoot 'wgpu/artifacts' }
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
foreach ($submoduleName in @('litert', 'LiteRT-LM')) {
    $submodulePath = Join-Path $repoRoot $submoduleName
    if ($OutputDirectory.Equals($submodulePath, [StringComparison]::OrdinalIgnoreCase) -or
        $OutputDirectory.StartsWith($submodulePath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory must not be inside a source submodule.'
    }
}
$nativeDirectory = Join-Path $repoRoot 'lib/runtimes/win-x64/native'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Assert-X64Dll([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 1024 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5A4D) { throw "Not a DLL: $Path" }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 24 -ge $bytes.Length -or
        [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550 -or
        [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664 -or
        ([BitConverter]::ToUInt16($bytes, $peOffset + 22) -band 0x2000) -eq 0) { throw "Not an x64 PE DLL: $Path" }
}
function Git-Revision([string]$Folder) {
    $revision = & git -C (Join-Path $repoRoot $Folder) rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw "Git read failed in $Folder" }
    return "$revision".Trim()
}

if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'bazel_build_litert.ps1') 64 -Gpu
    if ($LASTEXITCODE -ne 0) { throw "bazel_build_litert.ps1 -Gpu failed (exit $LASTEXITCODE)." }
}

# The Windows SDK's shader compilers (the Direct3D 12 backend fails to create a device without FXC, and rejects
# "enable f16" shaders without DXC).
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

$artifactSources = @(
    (Join-Path $nativeDirectory 'libLiteRt.dll'),
    (Join-Path $nativeDirectory 'libLiteRtWebGpuAccelerator.dll'),
    (Join-Path $nativeDirectory 'webgpu_dawn.dll'),
    (Join-Path $sdkDirectory 'dxcompiler.dll'), (Join-Path $sdkDirectory 'dxil.dll'), (Join-Path $sdkDirectory 'd3dcompiler_47.dll'))
foreach ($artifactSource in $artifactSources) {
    if (-not (Test-Path -LiteralPath $artifactSource)) { throw "Missing $artifactSource (run without -SkipBuild)." }
    Assert-X64Dll $artifactSource
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$artifacts = foreach ($artifactSource in $artifactSources) {
    $artifactName = Split-Path $artifactSource -Leaf
    $artifactDestination = Join-Path $OutputDirectory $artifactName
    if (Test-Path -LiteralPath $artifactDestination) { (Get-Item -LiteralPath $artifactDestination).IsReadOnly = $false }
    Copy-Item -LiteralPath $artifactSource -Destination $artifactDestination -Force
    (Get-Item -LiteralPath $artifactDestination).IsReadOnly = $false
    [ordered]@{ name = $artifactName; bytes = (Get-Item -LiteralPath $artifactDestination).Length; sha256 = (Get-FileHash -LiteralPath $artifactDestination -Algorithm SHA256).Hash }
}
$manifest = [ordered]@{
    builtAtUtc = [DateTime]::UtcNow.ToString('o'); platform = 'win-x64'
    litert = Git-Revision 'litert'; litertLm = Git-Revision 'LiteRT-LM'
    dawn = "LiteRT-LM prebuilt/windows_x86_64/webgpu_dawn.dll"; windowsSdk = $WindowsSdkVersion
    patches = @()
    artifacts = @($artifacts)
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'gpu-build.json'), ($manifest | ConvertTo-Json -Depth 5), $utf8)

# Native source license notices are shipped alongside the DLL bundle: LiteRT's own, and those of the sources Bazel
# downloaded for the accelerator (ML Drift) and Dawn, under the LiteRT-LM build's output base.
$licenseOutput = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $licenseOutput | Out-Null
$externalRoots = @((Join-Path $repoRoot 'o/external'))
if ($env:LITERT_LM_BAZEL_ROOT) { $externalRoots = @((Join-Path $env:LITERT_LM_BAZEL_ROOT 'o/external')) + $externalRoots }
function Find-External([string]$RelativePath) {
    foreach ($root in $externalRoots) {
        $candidate = Join-Path $root $RelativePath
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw "Bazel external source not found: $RelativePath (searched $($externalRoots -join ', '))."
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'litert/LICENSE') -Destination (Join-Path $licenseOutput 'LiteRT.txt') -Force
Copy-Item -LiteralPath (Find-External 'ml_drift/LICENSE') -Destination (Join-Path $licenseOutput 'ML-Drift.txt') -Force
Copy-Item -LiteralPath (Find-External 'dawn/LICENSE') -Destination (Join-Path $licenseOutput 'Dawn.txt') -Force
Copy-Item -LiteralPath (Find-External 'dawn/third_party/directx-shader-compiler/LICENSE') -Destination (Join-Path $licenseOutput 'DXC.txt') -Force
Write-Host "Windows GPU bundle completed: $OutputDirectory"
