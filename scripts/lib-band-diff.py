#!/usr/bin/env python
"""两张桌面截图在"矩形边框带"上的差分像素数。

用法: lib-band-diff.py A.png B.png x y w h [band=3] [thr=24]

只比较矩形四周 band 像素宽的环带（含内外各 band），矩形内部不参与——
这样桌面内容（时钟/动图壁纸）变化不会污染结论，只有"边框在不在"会影响结果。
输出: 差异像素数（整数）。
"""
import sys
from PIL import Image


def main():
    if len(sys.argv) < 7:
        print("usage: lib-band-diff.py A.png B.png x y w h [band] [thr]", file=sys.stderr)
        return 2
    a = Image.open(sys.argv[1]).convert("RGB")
    b = Image.open(sys.argv[2]).convert("RGB")
    x, y, w, h = (int(v) for v in sys.argv[3:7])
    band = int(sys.argv[7]) if len(sys.argv) > 7 else 3
    thr = int(sys.argv[8]) if len(sys.argv) > 8 else 24

    x0, y0 = max(0, x - band), max(0, y - band)
    x1, y1 = min(min(a.width, b.width), x + w + band), min(min(a.height, b.height), y + h + band)
    # 环带 = 外扩矩形 减去 内缩矩形
    ix0, iy0, ix1, iy1 = x + band, y + band, x + w - band, y + h - band

    pa, pb = a.load(), b.load()
    diff = 0
    for py in range(y0, y1):
        for px in range(x0, x1):
            if ix0 <= px < ix1 and iy0 <= py < iy1:
                continue  # 矩形内部忽略
            ca, cb = pa[px, py], pb[px, py]
            if max(abs(ca[0] - cb[0]), abs(ca[1] - cb[1]), abs(ca[2] - cb[2])) > thr:
                diff += 1
    print(diff)
    return 0


if __name__ == "__main__":
    sys.exit(main())
