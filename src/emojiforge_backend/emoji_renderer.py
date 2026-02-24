from __future__ import annotations

import os
import shutil
import subprocess
import sys
from functools import lru_cache
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

DEFAULT_FONT_PATH = r"C:\Windows\Fonts\seguiemj.ttf"
DEFAULT_RENDER_SIZE = 128

_FONT_SEARCH_PATHS = [
    # WSL mount of Windows fonts
    "/mnt/c/Windows/Fonts/seguiemj.ttf",
    # Linux Noto Color Emoji (common distros)
    "/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf",
    "/usr/share/fonts/noto-color-emoji/NotoColorEmoji.ttf",
    "/usr/share/fonts/google-noto-color-emoji/NotoColorEmoji.ttf",
    "/usr/share/fonts/truetype/google/NotoColorEmoji.ttf",
]

_RENDER_TRACE = os.getenv("EMOJIFORGE_RENDER_TRACE", "").strip().lower() in ("1", "true", "yes", "on")
_logged_font_resolution: tuple[str, str | None] | None = None


@lru_cache(maxsize=8)
def _resolve_emoji_font(configured_path: str) -> str | None:
    """Try to find a usable color-emoji font, returning the first path that exists."""
    # 1. Try the configured path as-is
    if Path(configured_path).is_file():
        return configured_path

    # 2. Try known cross-platform paths
    for p in _FONT_SEARCH_PATHS:
        if Path(p).is_file():
            return p

    # 3. Try fc-match as a last resort
    if shutil.which("fc-match"):
        try:
            result = subprocess.run(
                ["fc-match", "--format=%{file}", "emoji"],
                capture_output=True,
                text=True,
                timeout=5,
            )
            candidate = result.stdout.strip()
            if candidate and Path(candidate).is_file():
                return candidate
        except Exception:
            pass

    return None


@lru_cache(maxsize=16)
def _load_font(font_path: str, render_size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    try:
        return ImageFont.truetype(font_path, size=int(render_size * 0.75))
    except Exception:
        return ImageFont.load_default()


def _validate_emoji_image(img: Image.Image, emoji_char: str) -> None:
    """Raise if the rendered emoji image has no visible pixels."""
    if img.mode != "RGBA":
        return
    alpha = img.split()[3]
    bbox = alpha.getbbox()
    if _RENDER_TRACE and bbox is not None:
        total = img.size[0] * img.size[1]
        nonzero = alpha.histogram()[1:]
        coverage = (sum(nonzero) / total) if total > 0 else 0.0
        print(
            f"[emoji_renderer] rendered '{emoji_char}' alpha_bbox={bbox} coverage={coverage:.6f}",
            file=sys.stderr,
            flush=True,
        )
    if bbox is None:
        raise RuntimeError(
            f"Rendered emoji '{emoji_char}' has no visible pixels — "
            "the font cannot render this emoji glyph. "
            "On Linux/WSL, install a color-emoji font: "
            "sudo apt install fonts-noto-color-emoji"
        )


def render_emoji_to_image(
    emoji_char: str,
    font_path: str = DEFAULT_FONT_PATH,
    output_size: int = 512,
    render_size: int = DEFAULT_RENDER_SIZE,
) -> Image.Image:
    """Render a Unicode emoji character to a square RGBA image."""
    global _logged_font_resolution

    resolved = _resolve_emoji_font(font_path)
    current_resolution = (font_path, resolved)
    if _logged_font_resolution != current_resolution:
        _logged_font_resolution = current_resolution
        if resolved and resolved != font_path:
            print(
                f"[emoji_renderer] configured font not found ({font_path}), "
                f"using resolved path: {resolved}",
                file=sys.stderr,
                flush=True,
            )
        elif resolved:
            print(f"[emoji_renderer] font resolved: {resolved}", file=sys.stderr, flush=True)
        else:
            print(
                f"[emoji_renderer] WARNING: no color-emoji font found (tried {font_path} "
                "and common system paths). Rendering will likely produce a blank image.",
                file=sys.stderr,
                flush=True,
            )

    img = Image.new("RGBA", (render_size, render_size), (255, 255, 255, 0))
    draw = ImageDraw.Draw(img)

    font = _load_font(resolved or font_path, render_size)

    # Center text using measured bounding box to keep emoji framed consistently.
    bbox = draw.textbbox((0, 0), emoji_char, font=font, embedded_color=True)
    text_w = bbox[2] - bbox[0]
    text_h = bbox[3] - bbox[1]
    x = (render_size - text_w) // 2 - bbox[0]
    y = (render_size - text_h) // 2 - bbox[1]

    draw.text((x, y), emoji_char, font=font, embedded_color=True)
    result = img.resize((output_size, output_size), Image.LANCZOS)

    _validate_emoji_image(result, emoji_char)

    return result
