#!/usr/bin/env python
"""生成应用图标与托盘图标（描边方框母题）。

母图是 tools/assets/icon-master.png（256px 描边方框，黑色透明底；从旧版
icon.ico 的 256 帧提取，作为唯一真源固化下来）。脚本用形态学膨胀把描边整体
加粗（默认 +25%），再把外接框缩放回原尺寸，保证「线条更粗、占位不变」。

母图与产物分离，脚本因此可重复执行：多次运行得到同一结果，不会逐次累加加粗。

产物：
  src/PerfMonitor.App/Resources/icon.ico                 16/24/32/48/256 五尺寸
  src/PerfMonitor.App/Resources/tray-light-{16,20,24,28,32}.png   黑色托盘图
  src/PerfMonitor.App/Resources/tray-dark-{16,20,24,28,32}.png    白色托盘图

依赖：Pillow（pip install pillow）
用法：python tools/gen-icon.py [--stroke-scale 1.25] [--dry-run] [--source PATH]

--dry-run 只打印测量值（加粗前后描边宽度）并把预览写到 .scratch/icon-preview/，不改动资产。
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageChops

REPO = Path(__file__).resolve().parent.parent
RESOURCES = REPO / "src" / "PerfMonitor.App" / "Resources"
DEFAULT_MASTER = REPO / "tools" / "assets" / "icon-master.png"

ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (256, 256)]
TRAY_SIZES = [16, 20, 24, 28, 32]
SS = 4  # 超采样倍率：在 256*SS=1024 画布上做膨胀，再降采样，保证边缘抗锯齿


def load_master_alpha(path: Path) -> Image.Image:
    """取母图的 alpha 通道并二值化（>128 视为实心）。"""
    img = Image.open(path)
    if getattr(img, "n_frames", 1) > 1 or img.size[0] != 256:
        best, best_size = None, 0
        for i in range(getattr(img, "n_frames", 1)):
            img.seek(i)
            fr = img.convert("RGBA")
            if fr.size[0] > best_size:
                best, best_size = fr.copy(), fr.size[0]
        img = best
    else:
        img = img.convert("RGBA")
    if img.size[0] != 256:
        img = img.resize((256, 256), Image.LANCZOS)
    return img.split()[-1].point(lambda a: 255 if a > 128 else 0)


def disk_offsets(radius: int):
    r2 = radius * radius + radius  # 略放宽，避免圆盘边缘出现台阶
    return [
        (dx, dy)
        for dy in range(-radius, radius + 1)
        for dx in range(-radius, radius + 1)
        if dx * dx + dy * dy <= r2
    ]


def dilate(mask: Image.Image, radius: int) -> Image.Image:
    """按圆盘结构元膨胀（灰度取最大值）。"""
    if radius <= 0:
        return mask.copy()
    out = mask.copy()
    for dx, dy in disk_offsets(radius):
        if dx == 0 and dy == 0:
            continue
        shifted = mask.transform(
            mask.size, Image.AFFINE, (1, 0, -dx, 0, 1, -dy), resample=Image.NEAREST
        )
        out = ImageChops.lighter(out, shifted)
    return out


def runs(mask: Image.Image, index: int, axis: str) -> list[tuple[int, int]]:
    """某一行/列上实心像素的连续段 [(起, 止])，止为开区间。"""
    px = mask.load()
    w, h = mask.size
    out, start = [], None
    n = w if axis == "row" else h
    for i in range(n):
        a = px[i, index][0] if False else (px[i, index] if axis == "row" else px[index, i])
        on = a > 128
        if on and start is None:
            start = i
        elif not on and start is not None:
            out.append((start, i))
            start = None
    if start is not None:
        out.append((start, n))
    return out


def measure(mask: Image.Image) -> dict:
    """测量母图描边宽度：框（取上部空白处横扫）与框内横线（取左侧竖扫）。"""
    px = mask.load()
    w, h = mask.size
    bbox = mask.getbbox()
    # 框描边：在图形上方、框内的空白带里横扫，取左右两段
    probe_row = int(bbox[1] + (bbox[3] - bbox[1]) * 0.16)
    frame = [b - a for a, b in runs(mask, probe_row, "row")]
    # 框内横线：在图形左端竖扫，取中间那段
    probe_col = int(bbox[0] + (bbox[2] - bbox[0]) * 0.22)
    glyph = [b - a for a, b in runs(mask, probe_col, "col")]
    return {
        "bbox": bbox,
        "probe_row": probe_row,
        "frame_runs": frame,
        "probe_col": probe_col,
        "glyph_runs": glyph,
    }


def build(master: Image.Image, stroke_scale: float, verbose: bool = True) -> Image.Image:
    """加粗母图，返回 1024 画布的 alpha 图（占位与原图一致）。"""
    bbox = master.getbbox()
    w0, h0 = bbox[2] - bbox[0], bbox[3] - bbox[1]

    # 估测当前描边宽度：取框描边测量值
    m = measure(master)
    frame_w = min(m["frame_runs"]) if m["frame_runs"] else 24
    radius_256 = max(1, round(frame_w * (stroke_scale - 1.0) / 2.0))
    if verbose:
        print(f"母图 bbox={bbox} 尺寸={w0}x{h0}")
        print(f"加粗前：框描边≈{frame_w}px @256；探测行 y={m['probe_row']} 段={m['frame_runs']}")
        print(f"        框内图形竖扫段={m['glyph_runs']} (x={m['probe_col']})")
        print(f"膨胀半径 r={radius_256}px @256（目标 ×{stroke_scale}）")

    up = master.resize((256 * SS, 256 * SS), Image.NEAREST)
    dil = dilate(up, radius_256 * SS)
    bb = dil.getbbox()

    # 缩放回原占位：裁到膨胀后的外接框，再拉回 w0×h0，放回原位置
    crop = dil.crop(bb).resize((w0 * SS, h0 * SS), Image.LANCZOS)
    work = Image.new("L", (256 * SS, 256 * SS), 0)
    work.paste(crop, (bbox[0] * SS, bbox[1] * SS))

    if verbose:
        after = measure(work.resize((256, 256), Image.LANCZOS))
        frame_after = min(after["frame_runs"]) if after["frame_runs"] else 0
        print(f"加粗后：框描边≈{frame_after}px @256（×{frame_after / frame_w:.2f}），占位 {w0}x{h0} 不变")
    return work


def colorize(alpha: Image.Image, rgb: tuple[int, int, int]) -> Image.Image:
    img = Image.new("RGBA", alpha.size, rgb + (0,))
    img.putalpha(alpha)
    return img


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--stroke-scale", type=float, default=1.25)
    ap.add_argument("--source", default=str(DEFAULT_MASTER))
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    src = Path(args.source)
    if not src.exists():
        print(f"母图不存在：{src}", file=sys.stderr)
        return 1

    master = load_master_alpha(src)
    work = build(master, args.stroke_scale)

    out_dir = RESOURCES if not args.dry_run else REPO / ".scratch" / "icon-preview"
    out_dir.mkdir(parents=True, exist_ok=True)

    ico = colorize(work.resize((256, 256), Image.LANCZOS), (0, 0, 0))
    ico.save(out_dir / "icon.ico", sizes=ICO_SIZES)
    for size in TRAY_SIZES:
        a = work.resize((size, size), Image.LANCZOS)
        colorize(a, (0, 0, 0)).save(out_dir / f"tray-light-{size}.png")
        colorize(a, (255, 255, 255)).save(out_dir / f"tray-dark-{size}.png")

    print(f"产物目录：{out_dir}")
    for size in TRAY_SIZES:
        print(f"  tray-light-{size}.png / tray-dark-{size}.png  ({size}px)")
    print(f"  icon.ico  {'/'.join(str(s[0]) for s in ICO_SIZES)}")
    if args.dry_run:
        print("dry-run：未改动 src 资产")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
