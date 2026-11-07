@echo off
rem Rebuilds Uninstall.exe with the .NET Framework compiler that ships with Windows (no extra tools needed).
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -platform:x64 -optimize+ -out:Uninstall.exe -win32icon:..\CyberOptix\Assets\CyberOptix.ico -win32manifest:uninstall.manifest -reference:System.Windows.Forms.dll -reference:System.dll -reference:System.Drawing.dll Uninstall.cs
