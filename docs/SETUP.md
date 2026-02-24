# EmojiForge Setup

## Prerequisites
- Windows 10/11 64-bit
- Python 3.12+
- .NET 10 SDK/runtime
- `git` on PATH (required because `diffusers` is installed from GitHub source)
- NVIDIA GPU with CUDA support recommended (8GB+ VRAM preferred)

## Install backend dependencies

Preferred:
```bat
scripts\install_deps.bat
```

Alternative:
```bat
cd src\emojiforge_backend
setup.bat
```

The setup installs:
- CUDA 12.8 PyTorch wheels (`torch`, `torchvision`, `torchaudio`)
- `diffusers` from GitHub
- `transformers`, `accelerate`, `Pillow`, `emoji`, `rembg`, and related packages

## Run WinForms app
```powershell
dotnet run --project src/EmojiForge.WinForms
```

## Notes
- First model load may download large weights from Hugging Face.
- Default model is `black-forest-labs/FLUX.2-klein-4B`.
- If CUDA is unavailable, verify CUDA-enabled torch is installed in the selected Python environment.
- If you hit CUDA OOM, reduce output size/steps, enable CPU offload, or switch device to `cpu`.
