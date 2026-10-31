"""生成 FrameCapture 应用图标：取景框四角 + 中心快门点，强调色直接落在透明底上。

强调色 #4CC2FF 与 Theme.Accent 同值；深浅两种桌面都靠描边本身立形。
多尺寸 .ico 一次生成，exe / 开始菜单 / 「应用和功能」共用同一份源图。
应用图标的各尺寸图层由同一份源图生成。
同一份图同时写 assets/ 与 site/assets/，网页 favicon 与 exe 图标同源不分叉。
托盘图标由 Services/TrayService.Icons.cs 按物理像素绘制，共用取景框和中心点造型。
"""
from PIL import Image, ImageDraw

S = 1024                      # 源图边长，所有尺寸由它等比降采样
ACCENT = (76, 194, 255, 255)  # Fluent 强调色 #4CC2FF，与 Theme.Accent 同值
INSET = 150                   # 取景框四角到画布边的距离
ARM = 250                     # 每条角标的臂长
STROKE = 96                   # 角标描边粗细
DOT_R = 150                   # 中心快门点半径

# 供托盘图标换算的比例（占源图边长的百分比）
RATIOS = dict(inset=INSET / S, arm=ARM / S, stroke=STROKE / S, dot=2 * DOT_R / S)


def corners(d):
    """四枚 L 形角标，圆头圆角。"""
    lo, hi = INSET, S - INSET
    for cx, cy, dx, dy in ((lo, lo, 1, 1), (hi, lo, -1, 1), (lo, hi, 1, -1), (hi, hi, -1, -1)):
        d.line([(cx + dx * ARM, cy), (cx, cy), (cx, cy + dy * ARM)],
               fill=ACCENT, width=STROKE, joint="curve")


def draw_icon():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
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
    os.makedirs("site/assets", exist_ok=True)
    icon = draw_icon()
    icon.save("assets/FrameCapture.ico",
              format="ICO", sizes=[(s, s) for s in SIZES], bitmap_sizes=[(s, s) for s in SIZES])
    icon.save("assets/FrameCapture-256.png")
    icon.save("site/assets/FrameCapture-256.png")
    preview(icon, "test-out/icon-preview.png")
    print("written assets/FrameCapture.ico + 两份 PNG  sizes=" + ",".join(map(str, SIZES)))
    print("ratios " + " ".join(f"{k}={v:.4f}" for k, v in RATIOS.items()))
