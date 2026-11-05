@echo off
title Compilando ClipHistorico
cd /d "%~dp0"

rem Usa o compilador C# que ja vem com o Windows (.NET Framework 4)
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Nao encontrei o compilador do .NET Framework neste PC.
    pause
    exit /b 1
)

set "ICONE="
if exist "icone.ico" set "ICONE=/win32icon:icone.ico"

if not exist "emojis.gz" (
    echo Falta o arquivo emojis.gz nesta pasta. Extraia o zip inteiro.
    pause
    exit /b 1
)

rem x86 = versao 32 bits, que usa menos memoria
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /debug- %ICONE% /resource:emojis.gz,emojis.gz /out:ClipHistorico.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll ClipHistorico.cs

if errorlevel 1 (
    echo.
    echo Deu erro ao compilar. Tire um print desta janela e mande.
    pause
    exit /b 1
)

echo.
echo Pronto! ClipHistorico.exe foi criado nesta pasta.
echo Pode abrir ele com dois cliques.
pause
