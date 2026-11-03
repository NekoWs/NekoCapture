"""生成 NekoCapture 应用图标：取景框四角 + 中心快门点，强调色直接落在透明底上。

强调色 #4CC2FF 与 Theme.Accent 同值；深浅两种桌面都靠描边本身立形。
多尺寸 .ico 一次生成，exe / 开始菜单 / 「应用和功能」共用同一份源图。
小尺寸（16/20/24/32）按目标像素网格单独重画：比例缩小到那几个像素时描边会细成一圈灰边、
中心点会和四角粘成一坨，所以这几档用整数像素的内缩、臂长、描边与点半径重新排一遍。
同一份图同时写 assets/ 与 site/assets/，网页 favicon 与 exe 图标同源不分叉。
托盘图标由 Services/TrayService.Icons.cs 按物理像素绘制，共用取景框和中心点造型。
"""
import io
import struct
from PIL import Image, ImageDraw

S = 1024                      # 源图边长，所有尺寸由它等比降采样
ACCENT = (76, 194, 255, 255)  # Fluent 强调色 #4CC2FF，与 Theme.Accent 同值
INSET = 150                   # 取景框四角到画布边的距离
ARM = 250                     # 每条角标的臂长
STROKE = 96                   # 角标描边粗细
DOT_R = 150                   # 中心快门点半径

# 供托盘图标换算的比例（占源图边长的百分比）
RATIOS = dict(inset=INSET / S, arm=ARM / S, stroke=STROKE / S, dot=2 * DOT_R / S)

# 小尺寸的像素级版面：内缩、臂长、描边、中心点半径（目标像素）。
# 四角与中心点之间至少留 1 像素，否则两团颜色会在小尺寸上糊成一片。
TINY = {
    16: dict(inset=1, arm=4, stroke=2, dot=0),   # 16 只留四角：多一个点就认不出取景框
    20: dict(inset=1, arm=5, stroke=2, dot=2),
    24: dict(inset=2, arm=6, stroke=2, dot=2),
    32: dict(inset=3, arm=8, stroke=3, dot=3),
}


def corners(d, size, inset, arm, stroke):
    """四枚 L 形角标，圆头圆角。坐标与描边都以这一档的像素为单位。"""
    lo, hi = inset, size - inset
    for cx, cy, dx, dy in ((lo, lo, 1, 1), (hi, lo, -1, 1), (lo, hi, 1, -1), (hi, hi, -1, -1)):
        d.line([(cx + dx * arm, cy), (cx, cy), (cx, cy + dy * arm)],
               fill=ACCENT, width=max(1, int(round(stroke))), joint="curve")


def draw_icon():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    corners(d, S, INSET, ARM, STROKE)
    c = S // 2
    d.ellipse([c - DOT_R, c - DOT_R, c + DOT_R, c + DOT_R], fill=ACCENT)
    return img


def draw_at(size):
    """按目标尺寸画一枚：先在 8 倍画布上画好再收下来，边缘比直接降采样干净。"""
    tiny = TINY.get(size)
    # 小尺寸直接按像素画：8 倍超采样再收下来会把 2px 描边抹成一层灰边，
    # 原生像素画出来的硬边在 16px 反而更认得出
    ss = 1 if tiny else 8
    n = size * ss
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if tiny:
        corners(d, n, tiny["inset"] * ss, tiny["arm"] * ss, tiny["stroke"] * ss)
        if tiny["dot"]:
            c = n // 2
            r = tiny["dot"] * ss
            d.ellipse([c - r, c - r, c + r, c + r], fill=ACCENT)
    else:
        scale = n / S
        corners(d, n, INSET * scale, ARM * scale, STROKE * scale)
        c = n // 2
        r = DOT_R * scale
        d.ellipse([c - r, c - r, c + r, c + r], fill=ACCENT)
    return img if ss == 1 else img.resize((size, size), Image.LANCZOS)


SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)


def ico_entry(image, size):
    """ICO 里的一帧：256 用 PNG，其余用 32 位 BMP（壳层各处都认这一套）。"""
    if size >= 256:
        buf = io.BytesIO()
        image.save(buf, format="PNG")
        return buf.getvalue()
    rgba = image.convert("RGBA")
    bottom_up = [rgba.crop((0, y, size, y + 1)).tobytes("raw", "BGRA") for y in range(size - 1, -1, -1)]
    xor = b"".join(bottom_up)
    mask_row = ((size + 31) // 32) * 4
    mask = (b"\x00" * mask_row) * size
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(xor) + len(mask), 0, 0, 0, 0)
    return header + xor + mask


def write_ico(path, frames):
    """写多尺寸 .ico：每一档都是自己单独画的那一张，不是同一张图缩下去。"""
    payloads = [(size, ico_entry(image, size)) for size, image in frames]
    header = struct.pack("<HHH", 0, 1, len(payloads))
    offset = 6 + 16 * len(payloads)
    directory = b""
    for size, data in payloads:
        directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(path, "wb") as f:
        f.write(header)
        f.write(directory)
        for _, data in payloads:
            f.write(data)


def preview(frames, path):
    """把每一档按真实像素排开（浅底/深底），右边再放一份 8 倍放大图，用来判断小尺寸还认不认得。"""
    pad, cell, zoom = 24, 20, 8
    big = max(SIZES)
    width = pad * 3 + sum(max(s, 32) + cell for s in SIZES) + big * zoom
    height = pad * 2 + big
    sheet = Image.new("RGBA", (width, height), (128, 128, 128, 255))
    sd = ImageDraw.Draw(sheet)
    row_h = big
    y = pad
    sd.rectangle([0, y, width, y + row_h], fill=(243, 243, 243, 255))
    x = pad
    for size in SIZES:
        icon = frames[size]
        box = (x, y + (row_h - size) // 2, x + size, y + (row_h - size) // 2 + size)
        sheet.paste(icon, box, icon)
        x += max(size, 32) + cell
    small = frames[32]
    sheet.paste(small.resize((big * zoom // 2, big * zoom // 2), Image.NEAREST),
                (width - pad - big * zoom // 2, y + row_h - big * zoom // 2), None)
    sheet.convert("RGB").save(path)


if __name__ == "__main__":
    import os
    os.makedirs("assets", exist_ok=True)
    os.makedirs("site/assets", exist_ok=True)
    os.makedirs("test-out", exist_ok=True)
    icon = draw_icon()
    frames = {size: draw_at(size) for size in SIZES}
    write_ico("assets/NekoCapture.ico", [(size, frames[size]) for size in SIZES])
    icon.save("assets/NekoCapture-256.png")
    icon.save("site/assets/NekoCapture-256.png")
    preview(frames, "test-out/icon-preview.png")
    print("written assets/NekoCapture.ico + 两份 PNG  sizes=" + ",".join(map(str, SIZES)))
    print("ratios " + " ".join(f"{k}={v:.4f}" for k, v in RATIOS.items()))
