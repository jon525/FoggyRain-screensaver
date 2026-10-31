@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

rem Stop the background snapshot helper so FoggyRain.scr can be replaced.
taskkill /f /im FoggyRain.scr >nul 2>&1
if not errorlevel 1 timeout /t 1 /nobreak >nul

"%CSC%" /nologo /target:winexe /optimize+ /unsafe /out:FoggyRain.scr /r:System.Windows.Forms.dll /r:System.Drawing.dll FoggyRain.cs
if errorlevel 1 goto failed

rem Start the snapshot helper (it exits straight away if it's turned off in the settings).
start "" "%~dp0FoggyRain.scr" /watch

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
