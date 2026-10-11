#!/bin/sh
# Builds ConnectedHearts.exe with Mono's compiler against the .NET Framework 4.8 reference assemblies (exact Windows API surface).
# The file details (name, version, publisher) come from version.txt through a generated AssemblyInfo.
cd "$(dirname "$0")/.."
V=$(tr -d '\r\n ' < version.txt)
mkdir -p build
cat > build/AssemblyInfo.cs <<CS
using System.Reflection;
[assembly: AssemblyTitle("Connected Hearts")]
[assembly: AssemblyDescription("Connected Hearts launcher: online co-op for KINGDOM HEARTS II FINAL MIX")]
[assembly: AssemblyCompany("Connected Hearts")]
[assembly: AssemblyProduct("Connected Hearts")]
[assembly: AssemblyCopyright("Connected Hearts contributors, GPL-3.0")]
[assembly: AssemblyVersion("$V.0")]
[assembly: AssemblyFileVersion("$V.0")]
[assembly: AssemblyInformationalVersion("$V")]
CS
sed -e 's/AssemblyTitle("Connected Hearts")/AssemblyTitle("Connected Hearts Setup")/' \
    -e 's/AssemblyDescription("[^"]*")/AssemblyDescription("Connected Hearts installer")/' build/AssemblyInfo.cs > build/SetupInfo.cs
mcs -nologo -sdk:4.8 -target:winexe -platform:x64 -optimize+ -langversion:5 -win32icon:ui/icon.ico -out:ConnectedHearts.exe \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll \
  -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll \
  -r:lib/Microsoft.Web.WebView2.Core.dll -r:lib/Microsoft.Web.WebView2.WinForms.dll src/Program.cs build/AssemblyInfo.cs

# Installer: embeds dist/connected-hearts-update.zip (build the package first with tools/make_release.py).
if [ -f dist/connected-hearts-update.zip ]; then
mcs -nologo -sdk:4.8 -target:winexe -platform:x64 -optimize+ -langversion:5 -win32icon:ui/icon.ico -out:dist/ConnectedHeartsSetup.exe \
  -resource:dist/connected-hearts-update.zip,payload.zip \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll \
  src/Setup.cs build/SetupInfo.cs
fi
