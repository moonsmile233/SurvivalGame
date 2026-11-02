#!/usr/bin/env python
"""Regenerate the Unity UI font subset from the full Noto Sans SC (OFL).

Why this exists
---------------
The repo ships `Assets/Fonts/NotoSansSC-UI-Subset.otf` (a small subset) and the
WebGL build bakes a TMP SDF atlas from exactly those glyphs. When new UI text
adds a character that is not in the subset, TMP refuses to bake it and
`FontAssetBuilder` fails the build with a missing-character list.

The full font (8.3 MB) is NOT committed (license/size); only this derived subset is.
Use the source font from the shared art assets folder or download the official
Noto Sans SC from Google Fonts (OFL; `Assets/Fonts/NotoSansSC-OFL.txt`).

Usage
-----
  python tools/font-subset.py \
      --source "D:/agent使用学习/技术栈补充/agent制作游戏相关/assets/fonts/NotoSansSC-Regular.otf" \
      --chars  Assets/Fonts/ui-chars.txt \
      --output Assets/Fonts/NotoSansSC-UI-Subset.otf

Source path used on 2026-11-01 (not committed):
  D:\\agent使用学习\\技术栈补充\\agent制作游戏相关\\assets\\fonts\\NotoSansSC-Regular.otf
  fontTools 4.57.0; full font has 30,890 glyphs; 71 code points (incl. 1 space; 70 visible) ≈ 18 KB
  （2026-11-02 清理后按 UiStrings.AllCharacters 重新生成，实际字符数见构建日志）。

Keep `Assets/Fonts/ui-chars.txt` in sync with `UiStrings.AllCharacters` -- the
TMP builder unions both, but the subset OTF must contain every one of them.
"""

import argparse
import sys

from fontTools import subset
from fontTools.ttLib import TTFont


def read_chars(path):
    with open(path, encoding="utf-8") as handle:
        return "".join(ch for ch in handle.read() if not ch.isspace() or ch == " ")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, help="full Noto Sans SC OTF/TTF")
    parser.add_argument("--chars", required=True, help="ui-chars.txt (UTF-8)")
    parser.add_argument("--output", required=True, help="subset OTF to overwrite")
    args = parser.parse_args()

    chars = read_chars(args.chars)
    if not chars:
        raise SystemExit("character list is empty: " + args.chars)

    # --name-IDs=* keeps the font name and OFL copyright/license strings.
    subset.main(
        [
            args.source,
            "--text-file=" + args.chars,
            "--output-file=" + args.output,
            "--name-IDs=*",
            "--name-languages=*",
            "--notdef-glyph",
            "--notdef-outline",
            "--recommended-glyphs",
            "--drop-tables+=DSIG",
        ]
    )

    font = TTFont(args.output)
    covered = set()
    for table in font["cmap"].tables:
        covered.update(table.cmap.keys())

    missing = [ch for ch in chars if ord(ch) not in covered]
    if missing:
        raise SystemExit("subset is missing characters: " + "".join(missing))

    print(f"subset ok: {args.output} ({len(chars)} requested chars, {len(covered)} glyphs)")


if __name__ == "__main__":
    sys.exit(main())
