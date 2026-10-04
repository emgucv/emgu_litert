pushd %~p0
call powershell.exe -NoProfile -ExecutionPolicy Bypass -File cmake_build_litert.ps1 64 xnn
popd
