"""两帧截图差分：量化「40 只敌人围住静止玩家」时的持续微抖（M6 拥挤验收）。

用法：
    python tools/e2e/jitter-diff.py a.png b.png out.png --box 380,200,900,520

判定口径：玩家静止 + 站位成型时，两帧的敌人像素应该基本一致（只有 HUD 时间/分数在变，
所以默认裁掉 HUD 区域）。输出平均差异、变化像素比例、最大差异；差异热力图写 out.png 当证据。

注意：只看裁剪区。整屏差分会被时间文字/分数变化污染，不能代表敌人抖动。
"""

import argparse
import json
import sys

import numpy as np
from PIL import Image


def parse_box(text):
    parts = [int(part) for part in text.split(",")]
    if len(parts) != 4:
        raise ValueError("--box 需要 x0,y0,x1,y1")
    return tuple(parts)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("a")
    parser.add_argument("b")
    parser.add_argument("out")
    parser.add_argument("--box", default="380,200,900,520")
    parser.add_argument("--threshold", type=int, default=8, help="单像素绝对差超过该值算变化")
    args = parser.parse_args()

    box = parse_box(args.box)
    a = Image.open(args.a).convert("RGB").crop(box)
    b = Image.open(args.b).convert("RGB").crop(box)
    if a.size != b.size:
        print("尺寸不一致", a.size, b.size, file=sys.stderr)
        return 2

    na = np.asarray(a).astype(np.int16)
    nb = np.asarray(b).astype(np.int16)
    diff = np.abs(na - nb).max(axis=2)

    changed = diff > args.threshold
    result = {
        "box": box,
        "size": {"width": a.size[0], "height": a.size[1]},
        "meanAbsDiff": round(float(diff.mean()), 3),
        "p99AbsDiff": int(np.percentile(diff, 99)),
        "maxAbsDiff": int(diff.max()),
        "changedPixels": int(changed.sum()),
        "changedRatio": round(float(changed.mean()), 5),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))

    heat = np.clip(diff * 6, 0, 255).astype(np.uint8)
    Image.fromarray(heat).save(args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
