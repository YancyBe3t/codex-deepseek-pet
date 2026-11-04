#!/usr/bin/env python3
"""把上游 assets/whale/dense-*.png 切成 native-wpf 用的独立帧。

用法（在 native-wpf/ 目录下）：
    python scripts/slice-frames.py

要点：同一状态的所有帧共用**同一个裁切框**（所有帧的并集 + 6px 留白），
这样站姿、坐姿、躺姿之间切换时人物比例不会跳。
"""

import os
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent.parent          # native-wpf/
REPO = HERE.parent                                      # 仓库根目录
SRC = REPO / "assets" / "whale"                         # 上游原图
OUT = HERE / "assets" / "frames"                        # 输出

# 每个状态取多少帧（列数固定 4）
STATES = {
    "idle": 16,
    "walk": 32,
    "wave": 16,
    "jump": 16,
    "eat": 16,
    "dance": 16,
    "sneak": 16,
    "sleep": 32,          # 上游运行版只用 16 帧，这里用到躺下为止的全部 32 帧
}

COLUMNS = 4
MAX_HEIGHT = 480          # 输出高度上限，兼顾清晰度和仓库体积
PADDING = 6


def slice_state(name: str, count: int) -> None:
    sheet_path = SRC / f"dense-{name}.png"
    if not sheet_path.exists():
        print(f"skip {name}: {sheet_path} 不存在")
        return

    sheet = Image.open(sheet_path).convert("RGBA")
    cell = sheet.width // COLUMNS
    frames = []
    for i in range(count):
        col, row = i % COLUMNS, i // COLUMNS
        frames.append(sheet.crop((col * cell, row * cell, (col + 1) * cell, (row + 1) * cell)))

    left, top, right, bottom = cell, cell, 0, 0
    for frame in frames:
        box = frame.getbbox()
        if not box:
            continue
        left = min(left, box[0]); top = min(top, box[1])
        right = max(right, box[2]); bottom = max(bottom, box[3])

    left = max(0, left - PADDING); top = max(0, top - PADDING)
    right = min(cell, right + PADDING); bottom = min(cell, bottom + PADDING)
    width, height = right - left, bottom - top

    scale = min(1.0, MAX_HEIGHT / height)
    out_w, out_h = max(1, round(width * scale)), max(1, round(height * scale))

    target = OUT / name
    target.mkdir(parents=True, exist_ok=True)
    for old in target.glob("*.png"):
        old.unlink()

    for index, frame in enumerate(frames):
        crop = frame.crop((left, top, right, bottom))
        if scale < 1.0:
            crop = crop.resize((out_w, out_h), Image.LANCZOS)
        crop.save(target / f"f{index:02d}.png", optimize=True)

    print(f"{name}: {len(frames)} 帧  {width}x{height} -> {out_w}x{out_h}")


def main() -> None:
    if not SRC.exists():
        raise SystemExit(f"找不到上游素材目录：{SRC}")
    OUT.mkdir(parents=True, exist_ok=True)
    for name, count in STATES.items():
        slice_state(name, count)


if __name__ == "__main__":
    main()
