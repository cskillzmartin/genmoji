from __future__ import annotations

import os
import sys
from dataclasses import dataclass
import inspect

from pathlib import Path

import torch
from PIL import Image, ImageStat


@dataclass
class GenerateSettings:
    strength: float = 0.1
    num_inference_steps: int = 30
    guidance_scale: float = 30.0
    seed: int = 42


class EmojiGenerator:
    def __init__(
        self,
        model_path: str = "black-forest-labs/FLUX.2-klein-4B",
        device: str = "cuda",
        enable_cpu_offload: bool = False,
    ) -> None:
        self.device = device
        self._is_cuda = device.startswith("cuda")
        self._trace = os.getenv("EMOJIFORGE_TRACE", "").strip().lower() in ("1", "true", "yes", "on")
        self._fallback = False
        self.mode = "unknown"
        self.fallback_reason = ""
        self._init_exception: Exception | None = None
        self._supported_call_params: set[str] = set()
        is_local = Path(model_path).is_dir()

        if self._is_cuda and not torch.cuda.is_available():
            self._fallback = True
            self.pipe = None
            self.mode = "fallback"
            self.fallback_reason = (
                "Device is set to CUDA, but torch.cuda.is_available() is False. "
                "Install CUDA-enabled Torch wheels (cu128) in this Python environment, "
                "or switch Device to cpu in Settings."
            )
            return

        loader_errors: list[str] = []
        dtype = torch.bfloat16 if self._is_cuda else torch.float32
        candidates = [("flux2klein", "Flux2KleinPipeline")]

        for mode_name, class_name in candidates:
            try:
                diffusers = __import__("diffusers", fromlist=[class_name])
                pipeline_cls = getattr(diffusers, class_name)
                pipe = pipeline_cls.from_pretrained(
                    model_path,
                    torch_dtype=dtype,
                    local_files_only=is_local,
                )
                if enable_cpu_offload:
                    pipe.enable_model_cpu_offload()
                else:
                    pipe = pipe.to(device)
                    if self._is_cuda and torch.cuda.is_available():
                        torch.backends.cuda.matmul.allow_tf32 = True

                supported = set(inspect.signature(pipe.__call__).parameters)
                if "image" not in supported:
                    raise RuntimeError(
                        f"{class_name} missing required image input: {sorted(supported)}"
                    )

                self.pipe = pipe
                self.mode = mode_name
                self._supported_call_params = supported
                print(
                    f"[generator] selected {class_name} ({mode_name}) with params: {sorted(self._supported_call_params)}",
                    file=sys.stderr,
                    flush=True,
                )
                break
            except Exception as ex:
                loader_errors.append(f"{class_name} error: {type(ex).__name__}: {ex}")

        if self.mode == "unknown":
            self._fallback = True
            self.pipe = None
            self.mode = "fallback"
            self.fallback_reason = (
                "Unable to initialize diffusion pipeline with true image-to-image conditioning.\n"
                + "\n".join(loader_errors)
                + "\nCheck diffusers version compatibility and model path/repo."
            )

    def generate(self, base_image: Image.Image, prompt: str, **kwargs) -> Image.Image:
        debug_conditioning_path = kwargs.pop("debug_conditioning_path", None)
        diagnostic_stats = bool(kwargs.pop("diagnostic_stats", False)) or bool(debug_conditioning_path) or self._trace
        settings = GenerateSettings(**{k: v for k, v in kwargs.items() if k in GenerateSettings.__annotations__})

        if self._fallback or self.pipe is None:
            raise RuntimeError(f"Diffusion pipeline unavailable: {self.fallback_reason}")

        max_seed = (1 << 63) - 1
        seed = int(settings.seed) % max_seed
        generator = torch.Generator(device=self.device).manual_seed(seed)

        # Flatten RGBA onto a white background so the diffusion pipeline
        # receives a clear RGB image instead of a mostly-transparent one
        # (transparent pixels get converted to black by the VAE preprocessor,
        # which effectively erases the base emoji from conditioning).
        if base_image.mode == "RGBA":
            background = Image.new("RGB", base_image.size, (255, 255, 255))
            background.paste(base_image, mask=base_image.split()[3])
            conditioning_image = background
        else:
            conditioning_image = base_image.convert("RGB")

        if debug_conditioning_path:
            Path(debug_conditioning_path).parent.mkdir(parents=True, exist_ok=True)
            conditioning_image.save(debug_conditioning_path)
            print(
                f"[generator] saved conditioning image: {debug_conditioning_path}",
                file=sys.stderr,
                flush=True,
            )

        non_white_ratio = -1.0
        mean_l = -1.0
        stddev_l = -1.0
        extrema = (-1, -1)
        if diagnostic_stats:
            # Diagnostic stats to detect ineffective conditioning (blank/near-uniform inputs).
            gray = conditioning_image.convert("L")
            stat = ImageStat.Stat(gray)
            mean_l = stat.mean[0] if stat.mean else 0.0
            stddev_l = stat.stddev[0] if stat.stddev else 0.0
            extrema = stat.extrema[0] if stat.extrema else (0, 0)
            hist = gray.histogram()
            total_px = conditioning_image.size[0] * conditioning_image.size[1]
            non_white_px = sum(hist[:251]) if hist else 0
            non_white_ratio = (non_white_px / total_px) if total_px > 0 else 0.0
            if non_white_ratio < 0.001 or stddev_l < 1.0:
                print(
                    "[generator] WARNING: weak conditioning image detected "
                    f"(non_white_ratio={non_white_ratio:.6f}, mean_l={mean_l:.2f}, "
                    f"stddev_l={stddev_l:.2f}, extrema={extrema}). "
                    "Output may follow prompt more than emoji shape.",
                    file=sys.stderr,
                    flush=True,
                )

        # Flux2KleinPipeline expects reference images as a list for
        # single-reference editing mode (image latents concatenated with noise).
        image_value = [conditioning_image] if self.mode == "flux2klein" else conditioning_image

        pipe_kwargs: dict = {
            "prompt": prompt,
            "image": image_value,
            "num_inference_steps": settings.num_inference_steps,
            "guidance_scale": settings.guidance_scale,
            "generator": generator,
        }
        if "strength" in self._supported_call_params:
            pipe_kwargs["strength"] = settings.strength

        # Only pass parameters the pipeline actually accepts.
        pipe_kwargs = {k: v for k, v in pipe_kwargs.items() if k in self._supported_call_params}

        conditioning_type = "reference" if self.mode == "flux2klein" else "img2img"
        trace = (
            "[generator] img2img run "
            f"mode={self.mode} "
            f"conditioning={conditioning_type} "
            f"image_size={conditioning_image.size} "
            f"supports_image={'image' in self._supported_call_params} "
            f"supports_strength={'strength' in self._supported_call_params} "
            f"strength={settings.strength:.3f} "
            f"guidance={settings.guidance_scale:.3f} "
            f"steps={settings.num_inference_steps} "
            f"pipe_kwargs={sorted(pipe_kwargs.keys())}"
        )
        if diagnostic_stats:
            trace += (
                f" non_white_ratio={non_white_ratio:.6f} "
                f"mean_l={mean_l:.2f} "
                f"stddev_l={stddev_l:.2f} "
                f"extrema={extrema}"
            )
        print(trace, file=sys.stderr, flush=True)
        if "strength" not in self._supported_call_params:
            print(
                "[generator] warning: pipeline does not support native strength; "
                "strength slider has reduced effect.",
                file=sys.stderr,
                flush=True,
            )

        with torch.inference_mode():
            return self.pipe(**pipe_kwargs).images[0]
