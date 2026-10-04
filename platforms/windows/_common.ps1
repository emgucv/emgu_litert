<#
.SYNOPSIS
    Shared helpers dot-sourced by the other platforms/windows/*.ps1 build scripts
    (build_emgu_litert.ps1, bazel_build_litert.ps1, cmake_build_litert.ps1): the MSVC toolchain
    discovery logic that was identical, copy-pasted code across the Bazel, CMake, and MSBuild
    build paths before being factored out here.

.DESCRIPTION
    Dot-source this near the top of a script, after capturing $ScriptDir = $PSScriptRoot and
    changing location to the repo root:
        . (Join-Path $ScriptDir "_common.ps1")
    This file only defines functions - it has no side effects of its own. The vswhere-backed
    functions use a relative path ("miscellaneous\vswhere.exe"), matching the original .bat
    scripts' own assumption that the current directory is already the repo root by the time
    they run (each caller does `Set-Location (Join-Path $ScriptDir "..\..")` before reaching
    this point).
#>

function Get-ProgramFilesPaths {
    <#
    Mirrors each .bat's:
        SET PROGRAMFILES_DIR_X86=%programfiles(x86)%
        if NOT EXIST "%PROGRAMFILES_DIR_X86%" SET PROGRAMFILES_DIR_X86=%programfiles%
        SET PROGRAMFILES_DIR=%programfiles%
    #>
    $x86 = ${env:ProgramFiles(x86)}
    if (-not (Test-Path $x86)) { $x86 = $env:ProgramFiles }
    [PSCustomObject]@{ X86 = $x86; Default = $env:ProgramFiles }
}

function Get-LatestVersionDir {
    <#
    Get-ChildItem's enumeration order is not guaranteed to be sorted, and even a sorted-by-name
    fallback would misorder version jumps like "14.9.x" vs "14.10.x" (lexicographic "9" > "1").
    Parse each directory name as a [version] and pick the numerically highest instead.
    #>
    param([string]$Path, [string]$Filter = "*")
    Get-ChildItem -Path $Path -Directory -Filter $Filter -ErrorAction SilentlyContinue |
        ForEach-Object {
            $parsedVersion = $null
            if ([version]::TryParse($_.Name, [ref]$parsedVersion)) {
                [PSCustomObject]@{ Dir = $_; Version = $parsedVersion }
            }
        } |
        Sort-Object Version |
        Select-Object -Last 1 -ExpandProperty Dir
}

function Find-VsWhereInstallPath {
    <#
    vswhere can print more than one line when multiple installations matching the same query
    are present (e.g. a Community edition and a BuildTools edition of the same VS year both
    installed). The original .bat's `FOR /F ... DO SET VAR=%%F` overwrites on each line, so the
    last line wins; `& vswhere.exe` instead captures multi-line output as a string array, and
    interpolating an array into "$Var\..." would silently join elements with a space into a
    garbled, nonexistent path. Select-Object -Last 1 reproduces the .bat's "last line wins"
    behavior and guarantees a single string.
    #>
    param([string[]]$VsWhereArgs)
    & "miscellaneous\vswhere.exe" @VsWhereArgs -property installationPath 2>$null | Select-Object -Last 1
}

function Find-VisualStudioDevenv {
    <#
    VS2017/VS2019/VS2022/VS2026 devenv.com discovery via vswhere - identical across
    build_emgu_litert.ps1, bazel_build_litert.ps1 and cmake_build_litert.ps1 (which only
    consumes the VS2017/VS2019/VS2022 members - its original .bat predates VS2026, so it never
    checks that one).
    #>
    $vs2017Dir = Find-VsWhereInstallPath -VsWhereArgs @("-version", "[15.0,16.0)")
    $vs2019Dir = Find-VsWhereInstallPath -VsWhereArgs @("-version", "[16.0,17.0)")
    $vs2022Dir = Find-VsWhereInstallPath -VsWhereArgs @("-version", "[17.0,18.0)")
    $vs2026Dir = Find-VsWhereInstallPath -VsWhereArgs @("-version", "[18.0,19.0)")

    [PSCustomObject]@{
        VS2017Dir = $vs2017Dir; VS2017 = "$vs2017Dir\Common7\IDE\devenv.com"
        VS2019Dir = $vs2019Dir; VS2019 = "$vs2019Dir\Common7\IDE\devenv.com"
        VS2022Dir = $vs2022Dir; VS2022 = "$vs2022Dir\Common7\IDE\devenv.com"
        VS2026Dir = $vs2026Dir; VS2026 = "$vs2026Dir\Common7\IDE\devenv.com"
    }
}

function Find-LegacyMSBuild {
    <#
    MSBuild 3.5 / 4.0 detection, shared verbatim by bazel_build_litert.ps1 and
    cmake_build_litert.ps1. Neither will exist on a modern machine; kept for .bat fidelity.
    #>
    $msbuild35 = $null
    $msbuild40 = $null
    if (Test-Path "$env:windir\Microsoft.NET\Framework\v3.5\MSBuild.exe") { $msbuild35 = "$env:windir\Microsoft.NET\Framework\v3.5\MSBuild.exe" }
    if (Test-Path "$env:windir\Microsoft.NET\Framework64\v3.5\MSBuild.exe") { $msbuild35 = "$env:windir\Microsoft.NET\Framework64\v3.5\MSBuild.exe" }
    if (Test-Path "$env:windir\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe") { $msbuild40 = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" }
    [PSCustomObject]@{ MSBuild35 = $msbuild35; MSBuild40 = $msbuild40 }
}
