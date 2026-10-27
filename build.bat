@echo off
rem Builds ClassicVolumeMixer.exe with the C# compiler that ships inside Windows.
rem No Visual Studio or SDK needed.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x
  exit /b 1
)

if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /out:dist\ClassicVolumeMixer.exe ^
  /win32icon:res\app.ico ^
  /resource:res\app.ico,ClassicMixer.app.ico ^
  /resource:res\muted.ico,ClassicMixer.muted.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs

if errorlevel 1 (
  echo.
  echo Build FAILED
  exit /b 1
)
echo.
echo Built dist\ClassicVolumeMixer.exe
endlocal
