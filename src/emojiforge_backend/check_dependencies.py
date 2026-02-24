from __future__ import annotations

import argparse
import importlib
import importlib.metadata as importlib_metadata
import subprocess
import sys
from dataclasses import dataclass


@dataclass(frozen=True)
class Requirement:
    module: str
    package: str
    version_predicate: str | None = None


REQUIREMENTS: tuple[Requirement, ...] = (
    Requirement("torch", "torch"),
    Requirement("torchvision", "torchvision"),
    Requirement("torchaudio", "torchaudio"),
    Requirement("diffusers", "diffusers", "has_flux2"),
    Requirement("transformers", "transformers>=4.51.0,<5", "gte4.51_lt5"),
    Requirement("accelerate", "accelerate"),
    Requirement("safetensors", "safetensors"),
    Requirement("huggingface_hub", "huggingface_hub"),
    Requirement("PIL", "Pillow"),
    Requirement("emoji", "emoji"),
    Requirement("bitsandbytes", "bitsandbytes"),
    Requirement("sentencepiece", "sentencepiece"),
    Requirement("google.protobuf", "protobuf"),
    Requirement("onnxruntime", "onnxruntime"),
    Requirement("rembg", "rembg"),
)


def find_missing(require_cuda: bool = False) -> list[Requirement]:
    missing: list[Requirement] = []
    cuda_ok = True
    if require_cuda:
        try:
            import torch  # type: ignore

            cuda_ok = bool(torch.cuda.is_available())
        except Exception:
            cuda_ok = False

    for req in REQUIREMENTS:
        try:
            importlib.import_module(req.module)
            if not version_compatible(req):
                missing.append(req)
        except Exception as exc:
            print(f"  Import failed for {req.module}: {exc}", flush=True)
            missing.append(req)

    if require_cuda and not cuda_ok:
        for req in REQUIREMENTS:
            if req.module in {"torch", "torchvision", "torchaudio"} and req not in missing:
                missing.append(req)
    return missing


def version_compatible(req: Requirement) -> bool:
    if req.version_predicate is None:
        return True

    try:
        version_text = importlib_metadata.version(req.module if req.module != "PIL" else "Pillow")
    except Exception:
        return False

    if req.version_predicate == "lt5":
        major = int(version_text.split(".", 1)[0])
        return major < 5

    if req.version_predicate == "gte4.51_lt5":
        parts = version_text.split(".")
        major, minor = int(parts[0]), int(parts[1]) if len(parts) > 1 else 0
        return major == 4 and minor >= 51 or major > 4 and major < 5

    if req.version_predicate == "has_flux2":
        try:
            from diffusers import Flux2KleinPipeline  # noqa: F401
            return True
        except ImportError:
            pass
        try:
            from diffusers import Flux2Pipeline  # noqa: F401
            return True
        except ImportError:
            return False

    return True


def install_missing(missing: list[Requirement], require_cuda: bool = False) -> int:
    if not missing:
        return 0

    packages = sorted({req.package for req in missing})
    print("Installing missing packages:", ", ".join(packages), flush=True)

    torch_related = {"torch", "torchvision", "torchaudio"}
    missing_torch = [pkg for pkg in packages if pkg in torch_related]
    other = [pkg for pkg in packages if pkg not in torch_related and pkg != "diffusers"]
    needs_diffusers = "diffusers" in packages

    if missing_torch:
        # Prefer CUDA 12.4 wheels for local GPU inference.
        cmd = [
            sys.executable,
            "-m",
            "pip",
            "install",
            "--upgrade",
            *missing_torch,
            "--index-url",
            "https://download.pytorch.org/whl/cu128",
        ]
        if require_cuda:
            cmd.insert(5, "--force-reinstall")
        rc = subprocess.call(cmd)
        if rc != 0:
            return rc

    if needs_diffusers:
        cmd = [
            sys.executable,
            "-m",
            "pip",
            "install",
            "diffusers @ git+https://github.com/huggingface/diffusers.git",
        ]
        rc = subprocess.call(cmd)
        if rc != 0:
            return rc

    if other:
        cmd = [sys.executable, "-m", "pip", "install", *other]
        rc = subprocess.call(cmd)
        if rc != 0:
            return rc

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Check EmojiForge Python dependencies.")
    parser.add_argument("--install", action="store_true", help="Install missing dependencies.")
    parser.add_argument(
        "--require-cuda",
        action="store_true",
        help="Require CUDA-enabled torch in this environment.",
    )
    args = parser.parse_args()

    missing = find_missing(require_cuda=args.require_cuda)
    if not missing:
        print("All required Python dependencies are installed.", flush=True)
        return 0

    print("Missing dependencies:", ", ".join(req.package for req in missing), flush=True)

    if args.install:
        rc = install_missing(missing, require_cuda=args.require_cuda)
        if rc != 0:
            return rc

        missing_after = find_missing(require_cuda=args.require_cuda)
        if missing_after:
            print(
                "Still missing after install:",
                ", ".join(req.package for req in missing_after),
                flush=True,
            )
            return 1

        print("Missing dependencies installed successfully.", flush=True)
        return 0

    return 1


if __name__ == "__main__":
    raise SystemExit(main())
