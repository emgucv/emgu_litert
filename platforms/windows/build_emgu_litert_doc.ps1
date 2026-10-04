<#
.SYNOPSIS
    PowerShell port of build_emgutf_doc.bat - convenience wrapper for
    build_emgu_litert.ps1's documentation-only build.

.EXAMPLE
    .\build_emgu_litert_doc.ps1
#>
& (Join-Path $PSScriptRoot "build_emgu_litert.ps1") doc
