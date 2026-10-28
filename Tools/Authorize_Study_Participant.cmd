@echo off
rem Researcher Study setup: asks for project approvals once, then consent for one participant. Requires Python 3 and adb on PATH.
chcp 65001 >nul
python -X utf8 "%~dp0research_operator_authorize.py" wizard %*
echo.
pause
