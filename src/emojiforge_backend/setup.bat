@echo off
setlocal

echo === EmojiForge Backend Setup ===

where git >nul 2>nul
if errorlevel 1 (
    echo ERROR: git is not found on PATH. It is required to install diffusers from source.
    goto :fail
)

python -m venv .venv
call .venv\Scripts\activate.bat
if errorlevel 1 goto :fail

python -m pip install --upgrade pip
if errorlevel 1 goto :fail

python -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu128
if errorlevel 1 goto :fail

python -m pip install -r requirements.txt
if errorlevel 1 goto :fail

python check_dependencies.py --install
if errorlevel 1 goto :fail

echo Setup complete.
endlocal
exit /b 0

:fail
echo.
echo Setup failed. Review the errors above.
endlocal
exit /b 1
