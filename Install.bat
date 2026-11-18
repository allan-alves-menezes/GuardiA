@echo off
:: ==============================================================================
:: Script de Instalacao Automatica do Servico GuardIA
:: ==============================================================================
title Instalador - GuardIA 24h

:: 1. Verifica se o script esta sendo executado como Administrador
NET SESSION >nul 2>&1
if %errorLevel% neq 0 (
    echo ==============================================================================
    echo [ERRO] Este script precisa ser executado como ADMINISTRADOR!
    echo.
    echo Por favor, clique com o botao direito no arquivo "install.bat"
    echo e selecione "Executar como administrador".
    echo ==============================================================================
    echo.
    pause
    exit /b
)

:: 2. Define o caminho exato do executavel compilado
set "EXE_PATH=%~dp0bin\Release\net8.0\win-x64\publish\GuardiAProjeto.exe"

:: 3. Verifica se o arquivo .exe existe antes de tentar instalar
if not exist "%EXE_PATH%" (
    echo ==============================================================================
    echo [ERRO] O executavel do GuardIA nao foi encontrado!
    echo.
    echo Caminho procurado:
    echo %EXE_PATH%
    echo.
    echo Certifique-se de ter executado o comando de publicacao antes:
    echo dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
    echo ==============================================================================
    echo.
    pause
    exit /b
)

echo ==============================================================================
echo                      Instalando o Servico GuardIA 24h
echo ==============================================================================
echo.

:: 4. Se o servico ja existir, interrompe e remove para reinstalar do zero
sc query GuardIA >nul 2>&1
if %errorLevel% equ 0 (
    echo [*] Servico anterior detectado. Parando e removendo...
    sc.exe stop GuardIA >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc.exe delete GuardIA >nul 2>&1
    timeout /t 2 /nobreak >nul
)

:: 5. Registra o novo Servico no Windows
echo [*] Registrando o servico no Windows...
sc.exe create GuardIA binPath= "%EXE_PATH%" start= auto displayname= "GuardIA - Protecao Ativa" >nul
if %errorLevel% neq 0 (
    echo [ERRO] Falha ao criar o servico do Windows.
    pause
    exit /b
)

:: 6. Inicia o Servico
echo [*] Iniciando o servico GuardIA...
sc.exe start GuardIA >nul
if %errorLevel% neq 0 (
    echo [ERRO] O servico foi registrado, mas falhou ao iniciar.
    pause
    exit /b
)

echo.
echo ==============================================================================
echo [SUCESSO] O GuardIA foi instalado e esta rodando em segundo plano!
echo.
echo - Status: Ativo e Executando
echo - Inicializacao: Automatica com o Windows
echo - Interface Swagger: http://localhost:5000
echo ==============================================================================
echo.

:: 7. Aguarda 3 segundos e abre a interface Swagger no navegador padrao
timeout /t 3
start http://localhost:5000

pause