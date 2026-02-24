# EmojiForge

Generate AI-transformed emoji sets locally using FLUX.2 [klein] 4B.

Author: `cskillzmartin`

## Features
- Unicode emoji rendering to base image, then prompt-guided image editing
- FLUX.2 [klein] 4B backend (`black-forest-labs/FLUX.2-klein-4B`)
- WinForms desktop app
- Batch generation for all emojis or selected emoji input
- Output naming by Unicode codepoints + seed, e.g. `emoji_1F600_s42.png`
- Optional background removal with `rembg`
- Settings UI includes model download from Hugging Face and local model folder selection
- Real-time preview grid and run logging in the WinForms app

## Repository Layout
```text
src/EmojiForge.WinForms/     # Desktop UI
src/emojiforge_backend/      # Python backend (diffusion + emoji rendering + IPC)
scripts/install_deps.bat     # One-click backend dependency setup
docs/SETUP.md                # Additional setup notes
```

## Requirements
- Windows 10/11 (WinForms target)
- Python 3.12+
- .NET 10 SDK
- NVIDIA GPU recommended (8 GB+ VRAM), CUDA-enabled PyTorch wheels are used by default
- `git` on PATH (required because `diffusers` is installed from GitHub source)

## Setup
1. Clone this repo.
2. Run:
   - `scripts\install_deps.bat`
3. This creates `src\emojiforge_backend\.venv` and installs backend dependencies:
   - `torch/torchvision/torchaudio` (CUDA 12.8 wheels)
   - `diffusers` (GitHub source)
   - `transformers`, `accelerate`, `Pillow`, `emoji`, `rembg`, and others

## Run
### WinForms
```powershell
dotnet run --project src/EmojiForge.WinForms
```

## Settings and Behavior
- Default model: `black-forest-labs/FLUX.2-klein-4B`
- Device: `cuda` by default, with optional CPU offload in settings
- Generation controls: strength, steps, CFG scale, seed, output size
- Successful generations clean up intermediate debug artifacts (`*.base_rgba.png`, `*.conditioning.png`)
- If generation fails, debug artifacts are preserved for troubleshooting

## Output
- Each run writes to a timestamped folder under your output directory.
- Generated files are named with Unicode codepoints and seed:
  - `emoji_1F600_s123456789.png`
- `run_metadata.json` tracks prompt, settings, progress, and failures.

## Troubleshooting
- If CUDA is unavailable, verify your Python environment has CUDA-enabled Torch.
- If you hit OOM, reduce output size/steps, enable CPU offload, or switch device to `cpu`.
- If emoji render appears blank, verify your emoji font path (default: `C:\Windows\Fonts\seguiemj.ttf`).

## License
- Project code: MIT (see `LICENSE`)
- Model weights: Apache 2.0 (FLUX.2 [klein] 4B)

## CI / Releases
- Workflow: `.github/workflows/windows-release.yml`
- Push/PR to `main`: builds and uploads Windows artifacts
- Tag `v*` (example `v1.0.0`): publishes a GitHub Release with WinForms packaged binary
