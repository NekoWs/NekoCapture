"""生成 FrameCapture 应用图标：取景框四角 + 快门点，Fluent 强调色落在深色圆角砖上。

多尺寸 .ico 一次生成，exe / 开始菜单 / 「应用和功能」共用同一份源图。
小尺寸靠加大描边与中心点保证可读，不做逐尺寸重绘。
"""
from PIL import Image, ImageDraw

S = 1024                      # 源图边长，所有尺寸由它等比降采样
ACCENT = (76, 194, 255, 255)  # Fluent 强调色 #4CC2FF，与 Theme.Accent 同值
BG_TOP = (27, 30, 35, 255)    # 砖面顶部，贴近 Theme 深色 ChromeBg
BG_BOT = (38, 43, 51, 255)
TILE_R = 208                  # 圆角半径（源图坐标系）
TILE_EDGE = (255, 255, 255, 46)  # 砖面边线：深色桌面上砖面与背景几乎同色，靠这条才分得开
TILE_EDGE_W = 6               # 边线线宽（源图坐标系）
INSET = 150                   # 取景框四角到砖边的距离
ARM = 250                     # 每条角标的臂长
STROKE = 96                   # 角标描边粗细
DOT_R = 150                   # 中心快门点半径


def tile():
    """深色圆角砖，纵向微渐变。"""
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        c = tuple(int(BG_TOP[i] + (BG_BOT[i] - BG_TOP[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (S, y)], fill=c)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], TILE_R, fill=255)
    img.paste(grad, (0, 0), mask)
    # 边线向内收半个线宽，否则外缘被画布裁掉半像素、四角发暗
    w = TILE_EDGE_W
    o = w // 2
    ImageDraw.Draw(img).rounded_rectangle(
        [o, o, S - 1 - o, S - 1 - o], TILE_R - o, outline=TILE_EDGE, width=w)
    return img


def corners(d):
    """四枚 L 形角标，圆头圆角。"""
    lo, hi = INSET, S - INSET
    for cx, cy, dx, dy in ((lo, lo, 1, 1), (hi, lo, -1, 1), (lo, hi, 1, -1), (hi, hi, -1, -1)):
        d.line([(cx + dx * ARM, cy), (cx, cy), (cx, cy + dy * ARM)],
               fill=ACCENT, width=STROKE, joint="curve")


def draw_icon():
    img = tile()
    d = ImageDraw.Draw(img)
    corners(d)
    c = S // 2
    d.ellipse([c - DOT_R, c - DOT_R, c + DOT_R, c + DOT_R], fill=ACCENT)
    return img


SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)


def preview(src, path):
    """把同一枚图标在浅底/深底上按真实尺寸排开，用来判断 16px 是否还认得出。"""
    rows = [("浅色背景", (243, 243, 243, 255)), ("深色背景", (22, 22, 22, 255))]
    pad, cell = 28, 28
    h = pad * 3 + len(rows) * (max(SIZES) + cell)
    w = pad * 2 + sum(max(s, 40) + cell for s in SIZES)
    sheet = Image.new("RGBA", (w, h), (128, 128, 128, 255))
    sd = ImageDraw.Draw(sheet)
    y = pad
    for label, bg in rows:
        x = pad
        rowh = max(SIZES)
        for s in SIZES:
            box = (x, y + (rowh - s) // 2, x + s, y + (rowh - s) // 2 + s)
            sd.rectangle([x - cell // 2, y, x + max(s, 40) + cell // 2, y + rowh], fill=bg)
            sd.rectangle(box, fill=bg)
            sheet.paste(src.resize((s, s), Image.LANCZOS), box, src.resize((s, s), Image.LANCZOS))
            x += max(s, 40) + cell
        y += rowh + cell
    sheet.convert("RGB").save(path)


if __name__ == "__main__":
    import os
    os.makedirs("assets", exist_ok=True)
    icon = draw_icon()
    icon.save("assets/FrameCapture.ico",
              format="ICO", sizes=[(s, s) for s in SIZES], bitmap_sizes=[(s, s) for s in SIZES])
    icon.save("assets/FrameCapture-256.png")
    preview(icon, "test-out/icon-preview.png")
    print("written assets/FrameCapture.ico  sizes=" + ",".join(map(str, SIZES)))
