@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

"%CSC%" /nologo /target:winexe /optimize+ /unsafe /out:FoggyRain.scr /r:System.Windows.Forms.dll /r:System.Drawing.dll FoggyRain.cs
if errorlevel 1 goto failed

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
