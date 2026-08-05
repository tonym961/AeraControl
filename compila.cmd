@echo off
rem Compila i tre eseguibili e impacchetta l'installatore.
rem
rem Tutto quello che serve sta in questa cartella e solo qui: sorgenti
rem in src\, icona accanto a questo file, risultati in build\. Prima
rem l'icona e il PowerShell del server venivano presi da una cartella
rem di lavoro temporanea, e cosi' il numero di versione di quel
rem PowerShell era rimasto indietro di sei versioni senza che nessuno
rem se ne accorgesse.
rem
rem I quattro sorgenti portano lo stesso numero ed escono insieme: se
rem non coincidono non si compila niente.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "R=%~dp0"
set "D=%~dp0build"

if not exist "%D%" mkdir "%D%"

for /f "tokens=6" %%a in ('findstr /c:"public const string Numero" "%R%src\AeraControl.cs"') do set N1=%%a
for /f "tokens=6" %%a in ('findstr /c:"public const string Numero" "%R%src\AeraTray.cs"')    do set N2=%%a
for /f "tokens=6" %%a in ('findstr /c:"public const string Numero" "%R%src\SetupAera.cs"')   do set N3=%%a
for /f "tokens=3" %%a in ('findstr /c:"$VersioneServer = " "%R%src\ServerSetup.ps1"')        do set N4=%%a
set N1=%N1:"=%
set N2=%N2:"=%
set N3=%N3:"=%
set N4=%N4:"=%
set N1=%N1:;=%
set N2=%N2:;=%
set N3=%N3:;=%

if not "%N1%"=="%N2%" goto :diverse
if not "%N1%"=="%N3%" goto :diverse
if not "%N1%"=="%N4%" goto :diverse
echo --- versione %N1% ---

echo --- console (client) ---
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:"%R%icona.ico" ^
  /out:"%D%\AeraControl.exe" /reference:System.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Security.dll ^
  /reference:System.Management.dll "%R%src\AeraControl.cs"
if errorlevel 1 exit /b 1

echo --- segnalatore (server) ---
"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:"%D%\AeraTray.exe" /reference:System.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll "%R%src\AeraTray.cs"
if errorlevel 1 exit /b 1

rem Il manifesto pretende l'amministratore: senza, la finestra si
rem aprirebbe anche non elevata limitandosi ad avvisare.
echo --- installatore ---
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:"%R%icona.ico" ^
  /win32manifest:"%R%src\setup.manifest" ^
  /out:"%D%\Setup-AeraControl.exe" ^
  /resource:"%D%\AeraControl.exe",AeraControl.exe ^
  /resource:"%D%\AeraTray.exe",AeraTray.exe ^
  /resource:"%R%icona.ico",icona.ico ^
  /resource:"%R%src\ServerSetup.ps1",ServerSetup.ps1 ^
  /reference:System.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll "%R%src\SetupAera.cs"
if errorlevel 1 exit /b 1

echo TUTTO OK, versione %N1%, in build\
exit /b 0

:diverse
echo ERRORE: i sorgenti non portano lo stesso numero di versione.
echo    AeraControl.cs   %N1%
echo    AeraTray.cs      %N2%
echo    SetupAera.cs     %N3%
echo    ServerSetup.ps1  %N4%
exit /b 1
