#!/bin/sh
# Builds ConnectedHearts.exe with Mono's compiler against the .NET Framework 4.8 reference assemblies (exact Windows API surface).
cd "$(dirname "$0")/.."
mcs -nologo -sdk:4.8 -target:winexe -platform:x64 -optimize+ -langversion:5 -win32icon:ui/icon.ico -out:ConnectedHearts.exe \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll \
  -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll \
  -r:lib/Microsoft.Web.WebView2.Core.dll -r:lib/Microsoft.Web.WebView2.WinForms.dll src/Program.cs

# Installer: embeds dist/connected-hearts-update.zip (build the package first with tools/make_release.py).
if [ -f dist/connected-hearts-update.zip ]; then
mcs -nologo -sdk:4.8 -target:winexe -platform:x64 -optimize+ -langversion:5 -win32icon:ui/icon.ico -out:dist/ConnectedHeartsSetup.exe \
  -resource:dist/connected-hearts-update.zip,payload.zip \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll src/Setup.cs
fi
