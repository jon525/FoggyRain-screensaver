@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

rem Stop the background snapshot helper so FoggyRain.scr can be replaced.
taskkill /f /im FoggyRainHelper.exe >nul 2>&1
taskkill /f /im FoggyRain.scr >nul 2>&1
timeout /t 1 /nobreak >nul

"%CSC%" /nologo /target:winexe /optimize+ /unsafe /out:FoggyRain.scr /r:System.Windows.Forms.dll /r:System.Drawing.dll FoggyRain.cs
if errorlevel 1 goto failed

rem Start the snapshot helper from an .exe copy (it exits straight away if it's turned off in the settings).
if not exist "%LOCALAPPDATA%\FoggyRain" mkdir "%LOCALAPPDATA%\FoggyRain"
copy /y "%~dp0FoggyRain.scr" "%LOCALAPPDATA%\FoggyRain\FoggyRainHelper.exe" >nul
start "" "%LOCALAPPDATA%\FoggyRain\FoggyRainHelper.exe" /watch

echo.
echo Built FoggyRain.scr
echo Right-click FoggyRain.scr and choose Install to make it your screen saver.
echo Keep it in this folder - Windows runs it from here.
echo.
pause
exit /b 0

:nocsc
echo Could not find csc.exe - the .NET Framework 4 C# compiler that normally ships with Windows.
pause
exit /b 1

:failed
echo.
echo Build failed - see the errors above.
pause
exit /b 1
