#Requires -Version 5.1
param([Parameter(Mandatory = $true)][string]$BundleDirectory)
$ErrorActionPreference = 'Stop'
try {
    $manifest = Get-Content -LiteralPath (Join-Path $BundleDirectory 'gpu-build.json') -Raw | ConvertFrom-Json
    if ($manifest.platform -ne 'win-x64') { throw 'The GPU bundle must target win-x64.' }
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    foreach ($source in @(@('litert', $manifest.litert), @('LiteRT-LM', $manifest.litertLm))) {
        $revision = & git -C (Join-Path $repoRoot $source[0]) rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $revision -ne $source[1]) { throw "GPU bundle does not match $($source[0]); rebuild it with bazel_build_windows_gpu.ps1." }
    }
    foreach ($name in @('libLiteRt.dll', 'libLiteRtWebGpuAccelerator.dll', 'webgpu_dawn.dll', 'dxcompiler.dll', 'dxil.dll', 'd3dcompiler_47.dll')) {
        $entry = @($manifest.artifacts | Where-Object { $_.name -eq $name })
        if ($entry.Count -ne 1) { throw "Missing or duplicate manifest entry: $name" }
        $file = Join-Path $BundleDirectory $name
        $stream = [IO.File]::OpenRead($file)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ((Get-Item -LiteralPath $file).Length -ne $entry[0].bytes -or $hash -ne $entry[0].sha256) {
            throw "GPU bundle checksum mismatch: $name. Rebuild the bundle."
        }
    }
    Write-Host 'Validated matching Windows WebGPU runtime bundle.'
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
