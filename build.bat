@echo off
rem Builds ConnectedHearts.exe from src\Program.cs with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" ( echo csc.exe not found. Install .NET Framework 4.8. & exit /b 1 )
if not exist lib\Microsoft.Web.WebView2.WinForms.dll ( echo lib\Microsoft.Web.WebView2.WinForms.dll missing. & exit /b 1 )
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /out:ConnectedHearts.exe ^
  /win32icon:ui\icon.ico /win32manifest:src\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  src\Program.cs
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )
echo Built ConnectedHearts.exe
