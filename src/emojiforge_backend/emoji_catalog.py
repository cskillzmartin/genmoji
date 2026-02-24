from __future__ import annotations

import emoji


def get_all_emojis() -> list[dict[str, str]]:
    """Return all unicode emojis with metadata."""
    result: list[dict[str, str]] = []
    for char, data in emoji.EMOJI_DATA.items():
        result.append(
            {
                "char": char,
                "name": data.get("en", "Unknown").strip(":").replace("_", " ").title(),
                "category": data.get("status", ""),
                "codepoints": "_".join(f"{ord(c):04X}" for c in char),
            }
        )

    return sorted(result, key=lambda item: item["name"])
