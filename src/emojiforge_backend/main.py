"""
Protocol: newline-delimited JSON stdin/stdout.
"""

from __future__ import annotations

import importlib.metadata as importlib_metadata
import json
import sys
import threading
import time
import uuid
from pathlib import Path

from config import InitConfig
from emoji_catalog import get_all_emojis
from emoji_renderer import render_emoji_to_image
from generator import EmojiGenerator


class BackendState:
    def __init__(self) -> None:
        self.generator: EmojiGenerator | None = None
        self.font_path = InitConfig.font_path
        self._rembg_sessions: dict = {}
        self.cancel_requested = False
        self.current_index = 0
        self.total_items = 0
        self.current_emoji = ""
        self._run_lock = threading.Lock()
        self._worker_thread: threading.Thread | None = None

    def get_rembg_session(self, model_name: str):
        if model_name not in self._rembg_sessions:
            from rembg import new_session

            self._rembg_sessions[model_name] = new_session(model_name)
        return self._rembg_sessions[model_name]

    def is_busy(self) -> bool:
        worker = self._worker_thread
        return worker is not None and worker.is_alive()

    def try_begin_run(self) -> bool:
        return self._run_lock.acquire(blocking=False)

    def end_run(self) -> None:
        if self._run_lock.locked():
            self._run_lock.release()


def _emit(payload: dict) -> None:
    print(json.dumps(payload, ensure_ascii=False), flush=True)


def _safe_output_path(output_dir: str, emoji_char: str, seed: int, batch_index: int = 1, batch_size: int = 1) -> str:
    codepoints = "_".join(f"{ord(c):04X}" for c in emoji_char)
    suffix = f"_b{batch_index}" if batch_size > 1 else ""
    return str(Path(output_dir) / f"emoji_{codepoints}_s{seed}{suffix}.png")


def _normalize_settings(raw: dict | None) -> dict:
    if raw is None:
        return {}

    out = dict(raw)
    if "output_size_px" in out:
        out["output_size_px"] = int(out["output_size_px"])
    if "num_inference_steps" in out:
        out["num_inference_steps"] = int(out["num_inference_steps"])
    if "seed" in out:
        out["seed"] = int(out["seed"])
    if "batch_size" in out:
        out["batch_size"] = int(out["batch_size"])
    if "strength" in out:
        out["strength"] = float(out["strength"])
    if "guidance_scale" in out:
        out["guidance_scale"] = float(out["guidance_scale"])
    if "cfg_scale" in out:
        out["guidance_scale"] = float(out.pop("cfg_scale"))
    if "remove_background" in out:
        out["remove_background"] = bool(out["remove_background"])
    if "remove_background_strength" in out:
        out["remove_background_strength"] = float(out["remove_background_strength"])
    if "rembg_model" in out:
        out["rembg_model"] = str(out["rembg_model"])
    out.pop("same_seed", None)
    out.pop("max_blend_cap", None)

    return out


def _handle_generate(state: BackendState, cmd: dict) -> None:
    if state.generator is None:
        _emit({"type": "error", "job_id": cmd.get("job_id", ""), "message": "Generator not initialized."})
        return

    job_id = cmd.get("job_id", str(uuid.uuid4()))
    preserve_progress = bool(cmd.get("preserve_progress_state", False))
    if not preserve_progress:
        state.current_index = 1
        state.total_items = 1
        state.current_emoji = str(cmd.get("emoji", ""))
    settings = _normalize_settings(cmd.get("settings"))
    output_size = settings.pop("output_size_px", 512)
    remove_bg = settings.pop("remove_background", True)
    remove_bg_strength = float(settings.pop("remove_background_strength", 1.0))
    remove_bg_strength = max(0.0, min(1.0, remove_bg_strength))
    rembg_model = settings.pop("rembg_model", "birefnet-general")
    debug_conditioning = bool(settings.pop("debug_save_conditioning", False))

    try:
        t_start = time.perf_counter()
        base = render_emoji_to_image(cmd["emoji"], font_path=state.font_path, output_size=output_size)
        t_after_render = time.perf_counter()
        alpha_bbox = base.split()[3].getbbox() if base.mode == "RGBA" else "N/A"
        print(
            f"[main] rendered emoji '{cmd['emoji']}' -> "
            f"size={base.size} mode={base.mode} alpha_bbox={alpha_bbox}",
            file=sys.stderr,
            flush=True,
        )
        debug_conditioning_path = None
        debug_base_path = None
        if debug_conditioning:
            out_path = Path(cmd["output_path"])
            debug_base_path = out_path.with_name(f"{out_path.stem}.base_rgba.png")
            debug_base_path.parent.mkdir(parents=True, exist_ok=True)
            base.save(debug_base_path)
            print(f"[main] saved base RGBA image: {debug_base_path}", file=sys.stderr, flush=True)
            debug_conditioning_path = str(out_path.with_name(f"{out_path.stem}.conditioning.png"))

        result = state.generator.generate(
            base,
            cmd["prompt"],
            debug_conditioning_path=debug_conditioning_path,
            **settings,
        )
        t_after_generate = time.perf_counter()

        if remove_bg:
            from rembg import remove

            session = state.get_rembg_session(rembg_model)
            removed = remove(result, session=session)
            if remove_bg_strength < 1.0:
                removed = removed.convert("RGBA")
                r, g, b, alpha = removed.split()
                keep = 1.0 - remove_bg_strength
                alpha = alpha.point(lambda p: int((255 * keep) + (p * remove_bg_strength)))
                removed.putalpha(alpha)
                print(
                    f"[main] toned-down background removal applied (strength={remove_bg_strength:.2f})",
                    file=sys.stderr,
                    flush=True,
                )
            result = removed
        t_after_rembg = time.perf_counter()

        out_path = Path(cmd["output_path"])
        out_path.parent.mkdir(parents=True, exist_ok=True)
        result.save(out_path)
        t_after_save = time.perf_counter()

        # Debug artifacts are useful during generation but should not remain
        # in successful output directories.
        if debug_base_path and debug_base_path.exists():
            debug_base_path.unlink()
        if debug_conditioning_path:
            conditioning_file = Path(debug_conditioning_path)
            if conditioning_file.exists():
                conditioning_file.unlink()

        render_ms = (t_after_render - t_start) * 1000.0
        diffusion_ms = (t_after_generate - t_after_render) * 1000.0
        rembg_ms = (t_after_rembg - t_after_generate) * 1000.0
        save_ms = (t_after_save - t_after_rembg) * 1000.0
        total_ms = (t_after_save - t_start) * 1000.0
        print(
            f"[perf] emoji={cmd['emoji']} output_size={output_size} "
            f"remove_bg={remove_bg} steps={settings.get('num_inference_steps', 'n/a')} "
            f"guidance={settings.get('guidance_scale', 'n/a')} "
            f"render_ms={render_ms:.1f} diffusion_ms={diffusion_ms:.1f} "
            f"rembg_ms={rembg_ms:.1f} save_ms={save_ms:.1f} total_ms={total_ms:.1f}",
            file=sys.stderr,
            flush=True,
        )

        _emit(
            {
                "type": "result",
                "job_id": job_id,
                "emoji": cmd["emoji"],
                "output_path": str(out_path),
                "success": True,
                "skipped": False,
            }
        )
    except Exception as ex:
        _emit({"type": "error", "job_id": job_id, "message": str(ex)})


def _handle_generate_all(state: BackendState, cmd: dict) -> None:
    if state.generator is None:
        _emit({"type": "error", "job_id": "", "message": "Generator not initialized."})
        return

    state.cancel_requested = False
    emojis = get_all_emojis()
    base_settings = cmd.get("settings", {})
    batch_size = max(1, int(base_settings.get("batch_size", 1)))
    total = len(emojis) * batch_size
    state.total_items = total
    base_seed = int(base_settings.get("seed", 42))
    same_seed = bool(base_settings.get("same_seed", False))
    settings = _normalize_settings(base_settings)
    output_dir = Path(cmd["output_dir"])
    generation_ordinal = 0

    for batch_index in range(1, batch_size + 1):
        for item in emojis:
            if state.cancel_requested:
                print(
                    f"[main] cancel completed after {generation_ordinal}/{total} emojis.",
                    file=sys.stderr,
                    flush=True,
                )
                _emit(
                    {
                        "type": "canceled",
                        "current": generation_ordinal,
                        "total": total,
                        "message": "Generation canceled by user.",
                    }
                )
                state.current_index = 0
                state.total_items = 0
                state.current_emoji = ""
                return

            generation_ordinal += 1
            emoji_char = item["char"]
            state.current_index = generation_ordinal
            state.current_emoji = emoji_char
            per_seed = base_seed if same_seed else base_seed + (generation_ordinal - 1)
            out_path = _safe_output_path(
                str(output_dir),
                emoji_char,
                per_seed,
                batch_index=batch_index,
                batch_size=batch_size,
            )
            job_id = str(uuid.uuid4())

            _emit(
                {
                    "type": "progress",
                    "job_id": job_id,
                    "current": generation_ordinal,
                    "total": total,
                    "emoji": emoji_char,
                }
            )

            per_settings = dict(settings)
            per_settings["seed"] = per_seed
            _handle_generate(
                state,
                {
                    "cmd": "generate",
                    "job_id": job_id,
                    "emoji": emoji_char,
                    "prompt": cmd["prompt"],
                    "output_path": out_path,
                    "settings": per_settings,
                    "preserve_progress_state": True,
                },
            )

    state.current_index = 0
    state.total_items = 0
    state.current_emoji = ""


def _run_generate_all_worker(state: BackendState, cmd: dict) -> None:
    try:
        _handle_generate_all(state, cmd)
    except Exception as ex:
        _emit({"type": "error", "job_id": "", "message": str(ex)})
    finally:
        state.end_run()


def main() -> None:
    state = BackendState()

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            cmd = json.loads(line)
            cmd_type = cmd.get("cmd")
        except Exception:
            continue

        if cmd_type == "init":
            cfg = InitConfig(
                model_path=cmd.get("model_path", InitConfig.model_path),
                device=cmd.get("device", InitConfig.device),
                font_path=cmd.get("font_path", InitConfig.font_path),
                enable_cpu_offload=bool(cmd.get("enable_cpu_offload", False)),
            )
            state.font_path = cfg.font_path

            try:
                print(
                    f"[main] initializing generator: model={cfg.model_path} "
                    f"device={cfg.device} cpu_offload={cfg.enable_cpu_offload}",
                    file=sys.stderr,
                    flush=True,
                )
                state.generator = EmojiGenerator(
                    model_path=cfg.model_path,
                    device=cfg.device,
                    enable_cpu_offload=cfg.enable_cpu_offload,
                )
                try:
                    diffusers_version = importlib_metadata.version("diffusers")
                except Exception:
                    diffusers_version = "unknown"
                print(
                    f"[main] generator ready: mode={state.generator.mode} "
                    f"fallback={state.generator._fallback} "
                    f"python={sys.executable} diffusers={diffusers_version} "
                    f"backend_file={Path(__file__).resolve()}",
                    file=sys.stderr,
                    flush=True,
                )
                _emit(
                    {
                        "type": "ready",
                        "mode": state.generator.mode,
                        "fallback": bool(getattr(state.generator, "_fallback", False)),
                        "message": getattr(state.generator, "fallback_reason", ""),
                        "backend_file": str(Path(__file__).resolve()),
                        "python_executable": sys.executable,
                        "diffusers_version": diffusers_version,
                    }
                )
            except Exception as ex:
                _emit({"type": "error", "message": str(ex)})

        elif cmd_type == "list_emojis":
            _emit({"type": "emoji_list", "emojis": get_all_emojis()})

        elif cmd_type == "generate":
            if state.is_busy():
                _emit(
                    {
                        "type": "error",
                        "job_id": cmd.get("job_id", ""),
                        "message": "Generation already in progress.",
                    }
                )
                continue
            state.cancel_requested = False
            _handle_generate(state, cmd)

        elif cmd_type == "generate_all":
            if state.is_busy():
                _emit({"type": "error", "job_id": "", "message": "Generation already in progress."})
                continue
            if not state.try_begin_run():
                _emit({"type": "error", "job_id": "", "message": "Generation already in progress."})
                continue

            Path(cmd["output_dir"]).mkdir(parents=True, exist_ok=True)
            state._worker_thread = threading.Thread(
                target=_run_generate_all_worker,
                args=(state, cmd),
                daemon=True,
            )
            state._worker_thread.start()

        elif cmd_type == "cancel":
            state.cancel_requested = True
            if state.total_items > 0:
                print(
                    "[main] cancel requested by client; waiting for current item to finish "
                    f"({state.current_index}/{state.total_items}, emoji={state.current_emoji or '?'})...",
                    file=sys.stderr,
                    flush=True,
                )
            else:
                print("[main] cancel requested by client; stopping at next safe point...", file=sys.stderr, flush=True)

        elif cmd_type == "quit":
            state.cancel_requested = True
            break


if __name__ == "__main__":
    main()
