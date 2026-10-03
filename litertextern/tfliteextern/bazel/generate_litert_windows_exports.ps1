<#
.SYNOPSIS
    Regenerates litert_windows_exports.def by scanning the static libraries Bazel actually links
    into //tfliteextern:libLiteRt for public LiteRt*/TfLite* C API symbols.

.DESCRIPTION
    Windows .def files can't use the wildcard patterns (LiteRt*, TfLite*) that
    litert_exported_symbols_darwin.lds/litert_exported_symbols_linux.lds use on macOS/Linux -
    the EXPORTS section needs an explicit, exact symbol list. Since hand-maintaining ~200 names
    isn't practical, this script derives that list the same way a human would verify it: ask
    Bazel (via `aquery`) exactly which /WHOLEARCHIVE-linked static libraries feed the
    //tfliteextern:libLiteRt link action, dump every public symbol from each one, and keep the
    ones matching the LiteRt/TfLite/kLiteRtRuntimeBuiltin prefixes (same prefixes as the
    macOS/Linux export lists).

    This must query the exact link action rather than glob every .lib under bazel-bin: that
    output tree accumulates artifacts from whatever else has been built against the same
    --output_base (other targets, other configurations), most of which aren't part of
    libLiteRt's own dependency closure at all. Scanning those too silently inflates the symbol
    list with names that never actually get linked into libLiteRt.dll - the .def file will list
    them, but MSVC drops a .def entry with no definition in the final link quietly, with no
    error to catch and no warning Bazel's build:short_logs config doesn't already suppress; the
    bug is invisible until you count dumpbin /exports entries and compare.

    Also note: libLiteRt.dll's own export table is the correct comparison point, not a
    dumpbin /linkermember scan of these same .lib files - if the real count ever stops matching
    dumpbin /exports on the built .dll after regenerating this file, something about the link
    (not this script) needs investigating.

    Only needs to be re-run when the pinned litert submodule version changes (or if linking
    //tfliteextern:libLiteRt ever fails with an unresolved external referencing a symbol not in
    the existing .def - a sign the API surface grew).

.EXAMPLE
    cd platforms/windows
    .\bazel_build_litert.ps1 64
    cd ..\..\litertextern\tfliteextern\bazel
    .\generate_litert_windows_exports.ps1
#>
param(
    [string]$LitertDir = "$PSScriptRoot\..\..\..\litert",
    [string]$OutputBaseDir = "$PSScriptRoot\..\..\..\platforms\windows\output_base",
    [string]$OutputUserRootDir = "$PSScriptRoot\..\..\..\platforms\windows\output_user_root",
    [string]$OutFile = "$PSScriptRoot\litert_windows_exports.def"
)

$ErrorActionPreference = "Stop"

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsInstall = & $vswhere -latest -products * -property installationPath
$dumpbinCandidates = Get-ChildItem -Path "$vsInstall\VC\Tools\MSVC" -Directory |
    ForEach-Object { Join-Path $_.FullName "bin\HostX64\x64\dumpbin.exe" } |
    Where-Object { Test-Path $_ }
if (-not $dumpbinCandidates) { throw "Could not find dumpbin.exe under $vsInstall\VC\Tools\MSVC\*\bin\HostX64\x64" }
$dumpbin = $dumpbinCandidates | Select-Object -Last 1
Write-Host "Using dumpbin: $dumpbin"

$BazelLlvm = Join-Path $vsInstall "VC\Tools\Llvm\x64"
$BazelCommand = "bazel.exe"
$MsysBin = "C:\msys64\usr\bin"
if (Test-Path (Join-Path $MsysBin "bazel.exe")) { $BazelCommand = Join-Path $MsysBin "bazel.exe" }

Push-Location $LitertDir
try {
    # Must match the same flags bazel_build_litert.ps1 uses to build //tfliteextern:libLiteRt -
    # querying with different flags (e.g. a different XNNPACK define) queries a different,
    # not-yet-built configuration's action graph.
    $aqueryArgs = @(
        "--output_base=$OutputBaseDir",
        "--output_user_root=$OutputUserRootDir",
        "aquery",
        "--repo_env=BAZEL_LLVM=$BazelLlvm",
        "--config=windows",
        "--define", "tflite_with_xnnpack=true",
        "-c", "opt",
        "mnemonic('CppLink', //tfliteextern:libLiteRt)",
        "--output=jsonproto"
    )
    Write-Host "Querying the //tfliteextern:libLiteRt link action..."
    $aqueryOut = & $BazelCommand @aqueryArgs 2>$null
} finally {
    Pop-Location
}

$libPaths = New-Object System.Collections.Generic.HashSet[string]
foreach ($line in $aqueryOut) {
    foreach ($m in [regex]::Matches($line, '/WHOLEARCHIVE:([^"\\]*(?:\\.[^"\\]*)*\.lib)')) {
        [void]$libPaths.Add($m.Groups[1].Value)
    }
}
if ($libPaths.Count -eq 0) { throw "No /WHOLEARCHIVE library inputs found for //tfliteextern:libLiteRt - did the bazel aquery flags drift from bazel_build_litert.ps1's, or did the link action's structure change?" }
Write-Host "Found $($libPaths.Count) whole-archived library inputs"

$symbols = New-Object System.Collections.Generic.HashSet[string]
foreach ($relPath in $libPaths) {
    $fullPath = Join-Path $LitertDir $relPath
    if (-not (Test-Path $fullPath)) { throw "Link input not found on disk: $fullPath (run bazel_build_litert.ps1 first so it's actually built)" }
    $out = & $dumpbin /linkermember:1 $fullPath 2>$null
    foreach ($line in $out) {
        # Public symbols listed by /linkermember:1 look like "<hex offset>  <symbol>". extern "C"
        # names (what the LiteRt/TfLite C APIs use) appear unmangled; C++-mangled names (which
        # start with '?') are skipped implicitly since they never match these prefixes.
        if ($line -match '^\s+[0-9A-Fa-f]+\s+((?:LiteRt|TfLite|kLiteRtRuntimeBuiltin)[A-Za-z0-9_]*)\s*$') {
            [void]$symbols.Add($matches[1])
        }
    }
}

Write-Host "Matched $($symbols.Count) LiteRt*/TfLite* symbols"

$sorted = $symbols | Sort-Object
$lines = @(
    "; Generated by generate_litert_windows_exports.ps1 - do not hand-edit.",
    ";",
    "; Export list for the Windows libLiteRt.dll built by //tfliteextern:libLiteRt: LiteRT's own",
    "; C API plus the TensorFlow Lite C API, which tfliteextern links against. Windows .def files",
    "; have no equivalent of the wildcard patterns",
    "; (LiteRt*/TfLite*/kLiteRtRuntimeBuiltin) that litert_exported_symbols_darwin.lds/",
    "; litert_exported_symbols_linux.lds use for the same combined API surface on macOS/Linux,",
    "; so this list is generated by scanning actual build output instead - see",
    "; generate_litert_windows_exports.ps1.",
    "",
    "EXPORTS"
) + ($sorted | ForEach-Object { "    $_" })

Set-Content -Path $OutFile -Value $lines
Write-Host "Wrote $OutFile ($($sorted.Count) symbols)"
