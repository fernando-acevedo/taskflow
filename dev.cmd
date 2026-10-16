@echo off
rem Double-click friendly wrapper: runs dev.ps1 without changing the execution policy.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
