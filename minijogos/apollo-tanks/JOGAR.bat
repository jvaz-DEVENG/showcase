@echo off
title APOLLO TANKS - servidor
cd /d "%~dp0"

where node >nul 2>nul
if errorlevel 1 (
  echo.
  echo  Node.js nao encontrado. Instale em https://nodejs.org e rode este arquivo de novo.
  echo.
  pause
  exit /b 1
)

if not exist node_modules (
  echo  Instalando dependencias pela primeira vez...
  call npm install --no-fund --no-audit
  if errorlevel 1 (
    echo  Falha ao instalar. Verifique sua internet.
    pause
    exit /b 1
  )
)

start "" http://localhost:8080
node server/index.js
pause
