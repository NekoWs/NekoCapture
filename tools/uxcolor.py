"""采样界面配色台阶 + 裁一块区域给人眼看。"""
import sys
from PIL import Image

path, tag = sys.argv[1], sys.argv[2]
im = Image.open(path).convert("RGB")
W, H = im.size
px = im.load()

def sample(x, y, r=3):
    """取一小块的中位数，避开抗锯齿边。"""
    rs, gs, bs = [], [], []
    for dx in range(-r, r + 1):
        for dy in range(-r, r + 1):
            xx, yy = min(max(x + dx, 0), W - 1), min(max(y + dy, 0), H - 1)
            c = px[xx, yy]
            rs.append(c[0]); gs.append(c[1]); bs.append(c[2])
    rs.sort(); gs.sort(); bs.sort()
    m = len(rs) // 2
    return (rs[m], gs[m], bs[m])

pts = {
    "标题栏": (600, 27),
    "侧栏底": (150, 500),
    "内容区底": (700, 465),
    "卡片底": (600, 428),
    "绑定行底": (600, 245),
    "键位行底": (600, 565),
    "键位行左边框": (352, 565),
    "最右留白": (1128, 500),
}
print(f"== {tag}  {path}  {W}x{H}")
for name, (x, y) in pts.items():
    c = sample(x, y)
    print(f"  {name:<12} ({x:>4},{y:>4})  #{c[0]:02X}{c[1]:02X}{c[2]:02X}  亮度 {sum(c)/3:5.1f}")

im.crop((330, 180, 1090, 720)).save(f"test-out/uiaudit/{tag}-crop.png")
print(f"  裁图 test-out/uiaudit/{tag}-crop.png")
