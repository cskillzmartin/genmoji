from __future__ import annotations

from dataclasses import dataclass


@dataclass
class InitConfig:
    model_path: str = "black-forest-labs/FLUX.2-klein-4B"
    device: str = "cuda"
    font_path: str = r"C:\Windows\Fonts\seguiemj.ttf"
    enable_cpu_offload: bool = False
