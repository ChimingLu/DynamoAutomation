@echo off
rem Double-click to install DynamoAutomation for all Revit 2020-2027 on this computer.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
pause
