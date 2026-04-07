@echo off
setlocal
cd /d "%~dp0"

if exist testapp-source.zip del testapp-source.zip
if exist _source rmdir /s /q _source

:: Copy source without bin/obj
xcopy TestApp _source\TestApp\ /s /e /exclude:pack-exclude.txt >nul
copy .netrunner.source _source\.netrunner >nul

powershell -Command "Compress-Archive -Path _source\* -DestinationPath testapp-source.zip"

rmdir /s /q _source

echo Created testapp-source.zip (source mode)
