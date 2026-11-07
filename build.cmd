@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
"%CSC%" /nologo /target:winexe /platform:anycpu /out:DotNotes.exe /win32icon:DotNotes.ico /resource:DotNotes.ico,DotNotes.ico /reference:"%WPF%\PresentationCore.dll" /reference:"%WPF%\PresentationFramework.dll" /reference:"%WPF%\WindowsBase.dll" /reference:System.Xaml.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.VisualBasic.dll DotNotes.cs
pause
