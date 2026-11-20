@echo off
rem Bangun KunciKeyboard.exe pakai csc bawaan .NET Framework (tanpa instal apa pun).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REF=/r:System.Windows.Forms.dll /r:System.Drawing.dll

if not exist kunci.ico (
  "%CSC%" /nologo /target:winexe /optimize /out:KunciKeyboard.exe %REF% KunciKeyboard.cs || exit /b 1
  "%~dp0KunciKeyboard.exe" --ikon kunci.ico || exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize /out:KunciKeyboard.exe /win32icon:kunci.ico %REF% KunciKeyboard.cs || exit /b 1
echo OK: %~dp0KunciKeyboard.exe
schtasks /query /tn KunciKeyboard >nul 2>&1 && echo Mode admin aktif: jalankan "KunciKeyboard.exe --pasang" lagi supaya salinan di Program Files ikut baru.
exit /b 0
