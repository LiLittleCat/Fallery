# Fallery（瀑廊）logo：三个方向对比
# A 瀑布流卡片：三列错落圆角卡片，最大的一张扣出播放三角
# B 字母 F：一根长卡片做竖，两张卡片做横，像瀑布流里的两张图
# C 瀑布播放键：播放三角切成三列，逐列下落，像水流过台阶
S = 1000
C = S / 2
M = S / PHI**4                 # 安全边距 ≈ 0.146S
BOX = S - 2 * M                # 内容区边长
BLUE = "#0F6CBD"
PLATE_R = S * 0.2              # Windows 风格圆角方形底板

def plate():
    return rect(0, 0, S, S, PLATE_R)

def play(cx, cy, h):
    # 指向右的等边三角，h 为竖直边长；按面积重心对齐（三角包围盒居中会显得偏左）
    t = polygon([(0, 0), (0, h), (h * SQRT3 / 2, h / 2)])
    return t.place_centroid(cx, cy)

# ── A 瀑布流卡片 ──
W_A = BOX / (3 + 2 / PHI**3)   # 列宽：3 列 + 2 条缝，缝 = 列宽/φ³
G_A = W_A / PHI**3
R_A = G_A                      # 卡片圆角 = 缝宽
L_A = (BOX - G_A) / PHI        # 长卡：一列两张卡按 φ 分
S_A = BOX - G_A - L_A          # 短卡

def col(i):
    return M + i * (W_A + G_A)

cards = [
    rect(col(0), M, W_A, L_A, R_A), rect(col(0), M + L_A + G_A, W_A, S_A, R_A),
    rect(col(1), M, W_A, S_A, R_A), rect(col(1), M + S_A + G_A, W_A, L_A, R_A),
    rect(col(2), M, W_A, (BOX - G_A) / 2, R_A), rect(col(2), M + (BOX + G_A) / 2, W_A, (BOX - G_A) / 2, R_A),
]
big = cards[3]
tri_a = play(big.center[0], big.center[1], W_A / PHI)
a = Logo(S, name="a-masonry")
a.plate(plate(), BLUE, cut=[union(*cards) - tri_a])
a.margin_box(M)

# ── B 字母 F ──
W_B = BOX / PHI**2             # 笔画 = 卡片宽
G_B = W_B / PHI**2
R_B = W_B / PHI**2
H_B = BOX
x0 = C - (W_B + G_B + BOX / PHI) / 2
stem = rect(x0, M, W_B, H_B, R_B)
arm1 = rect(x0 + W_B + G_B, M, BOX / PHI, W_B, R_B)
arm2 = rect(x0 + W_B + G_B, M + W_B + G_B, BOX / PHI**2, W_B, R_B)
f = stem + arm1 + arm2
b = Logo(S, name="b-letter-f")
b.plate(plate(), BLUE, cut=[f.place(C, C)])

# ── C 瀑布播放键 ──
T = BOX * 0.86                 # 三角竖边
G_C = T / PHI**5               # 列缝
D_C = T / PHI**4               # 每列下落量
tri = polygon([(0, 0), (0, T), (T * SQRT3 / 2, T / 2)])
tw = T * SQRT3 / 2
strips = []
for k in range(3):
    x_l = k * tw / 3 + (G_C / 2 if k else 0)
    x_r = (k + 1) * tw / 3 - (G_C / 2 if k < 2 else 0)
    strip = tri & rect(x_l, -T, x_r - x_l, 3 * T)
    strips.append(strip.move(0, k * D_C))
fall = union(*strips).round_corners(G_C / 2)
fall = fall.place_centroid(C, C)
c = Logo(S, name="c-falling-play")
c.plate(plate(), BLUE, cut=[fall])

# ── 定稿：基于 A 深化 ──
# 第一轮定稿把第三列做成第一列的镜像，结果中间读成字母 H。改成横缝逐列下降：
# 第一列短/长、第二列对半、第三列长/短，三条横缝连成一条从左上落到右下的斜线，
# 就是“瀑布”的走向。整体是 180° 旋转对称（有意打破左右镜像）。
# 播放三角放在左下的长卡里，与右上的长卡互相平衡。
TRI_H = W_A / PHI**1.5
HALF = (BOX - G_A) / 2

def masonry():
    return [
        rect(col(0), M, W_A, S_A, R_A), rect(col(0), M + S_A + G_A, W_A, L_A, R_A),
        rect(col(1), M, W_A, HALF, R_A), rect(col(1), M + HALF + G_A, W_A, HALF, R_A),
        rect(col(2), M, W_A, L_A, R_A), rect(col(2), M + L_A + G_A, W_A, S_A, R_A),
    ]

def glyph():
    cs = masonry()
    hero = cs[1]
    return union(*cs) - play(hero.center[0], hero.center[1], TRI_H)

icon = Logo(S, name="fallery-icon")
icon.plate(plate(), BLUE, cut=[glyph()])
icon.margin_box(M)
# 三条横缝的中线连成一条下落斜线
gaps = [(col(0) + W_A / 2, M + S_A + G_A / 2), (col(1) + W_A / 2, M + HALF + G_A / 2), (col(2) + W_A / 2, M + L_A + G_A / 2)]
icon.guide_line(*gaps[0], *gaps[2])
for gx, gy in gaps:
    icon.guide_point(gx, gy)
icon.dim(col(0), S - M, col(0) + W_A, S - M, "W", offset=-50)
icon.dim(col(0), M, col(0), M + S_A, "S", offset=-40)
icon.dim(col(0) + W_A, M + S_A / 2, col(1), M + S_A / 2, "g")
icon.note("边距 m = S/φ⁴", "3W + 2g = S − 2m，g = W/φ³", "长卡 L = (H − g)/φ，L : S = φ : 1", "圆角 = g", "播放三角高 = W/φ^1.5", "三条横缝连成下落斜线")

mark = Logo(S, name="fallery-mark")
mark.add(glyph(), BLUE)

# ── 小尺寸版（16–24px）：两列各两张，缝加宽到 W/φ²，播放三角放大；圆角 = 缝/φ，避免卡片变成胶囊 ──
W_S = BOX / (2 + 1 / PHI**2)
G_S = W_S / PHI**2
R_S = G_S / PHI
L_S = (BOX - G_S) / PHI
S_S = BOX - G_S - L_S
small_cards = [
    rect(M, M, W_S, S_S, R_S), rect(M, M + S_S + G_S, W_S, L_S, R_S),
    rect(M + W_S + G_S, M, W_S, L_S, R_S), rect(M + W_S + G_S, M + L_S + G_S, W_S, S_S, R_S),
]
hero_s = small_cards[1]
small = Logo(S, name="fallery-small")
small.plate(plate(), BLUE, cut=[union(*small_cards) - play(hero_s.center[0], hero_s.center[1], W_S / PHI)])

# ── 光影版用的分层路径：底板、卡片（含三角孔）、三角，分别导出给 fluent.py 上材质 ──
import json, os
os.makedirs("out", exist_ok=True)

def layers(cards, hero_index, tri_h):
    hero = cards[hero_index]
    tri = play(hero.center[0], hero.center[1], tri_h)
    return {"plate": plate().svg_d(), "cards": (union(*cards) - tri).svg_d(), "play": tri.svg_d()}

with open("out/layers.json", "w") as fp:
    json.dump({"size": S, "icon": layers(masonry(), 1, TRI_H), "small": layers(small_cards, 1, W_S / PHI)}, fp)
