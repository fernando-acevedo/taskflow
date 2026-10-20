@echo off
rem Double-click friendly wrapper: runs dev.ps1 without changing the execution policy.
rem Arguments are passed through, for example: dev.cmd -Database LocalDb -ApiDocs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
rem Keep the window open on failure so the message can be read.
if errorlevel 1 pause
