# go to the folder of the current script
Push-Location (Join-Path $PSScriptRoot "..")

git clean -d -fx "." -e .claude -e .codex
git submodule foreach --recursive git clean -fx "." -e .claude -e .codex

Pop-Location
