@echo off
REM Pneuma environment reset entry point. Delegates to the factory reset script,
REM which restores clean assets from docker\factory\ and wipes runtime data.
call "%~dp0factory\reset.bat" %*
