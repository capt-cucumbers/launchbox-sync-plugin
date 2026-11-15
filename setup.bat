@echo off
setlocal

cd /d %~dp0

echo Copying default configuration files...
echo.

if not exist "config.json" (
    if exist "config.dist.json" (
        echo   Copying config.dist.json to config.json...
        copy "config.dist.json" "config.json" >nul
    )
)

echo Setup finished.
endlocal
