@echo off
setlocal
cd /d "%~dp0"

if exist testapp-publish.zip del testapp-publish.zip
if exist _publish rmdir /s /q _publish

dotnet publish TestApp -c Release -o _publish
if errorlevel 1 exit /b 1

copy .netrunner.publish _publish\.netrunner >nul

powershell -Command "Compress-Archive -Path _publish\* -DestinationPath testapp-publish.zip"

rmdir /s /q _publish

echo Created testapp-publish.zip (artifact mode)
