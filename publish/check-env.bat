@echo off
chcp 65001 >nul
title DMADC 运行环境检测
REM 双击运行，或把此文件和 DMADC.exe 放在同一目录

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0check-env.ps1"
pause
