<#
.SYNOPSIS
    PowerShell port of cmake_build_tflite_x86_64.bat - convenience wrapper for
    cmake_build_litert.ps1's 64-bit XNNPACK build.

.EXAMPLE
    .\cmake_build_litert_x86_64.ps1
#>
& (Join-Path $PSScriptRoot "cmake_build_litert.ps1") 64 xnn
