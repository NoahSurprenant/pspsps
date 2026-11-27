import sys; sys.path.insert(0, "/work")
from lib import *
reset()
LOG, LOGD, STONE, ROOF, CREAM, GLASS, GREEN, GOLD, DARK, TRIM = "#a8743f", "#7a4f2a", "#8d8f94", "#7a3b2e", "#f4ead2", "#bfe6ff", "#3f7a4a", "#ffd23f", "#3a2418", "#5a3a20"
F = -0.7
# stone plinth, then two log storeys with darker log bands
box(8.2, 1.7, 0.5, 0, 0, 0, STONE)
box(7.8, 1.4, 3.6, 0, 0, 0.5, LOG)
for k in range(6):
    box(7.86, 1.46, 0.07, 0, 0, 0.9 + k * 0.55, LOGD)
box(8.1, 1.6, 0.3, 0, 0, 4.1, TRIM)
box(7.4, 1.4, 2.7, 0, 0, 4.4, LOG)
for k in range(4):
    box(7.46, 1.46, 0.07, 0, 0, 4.8 + k * 0.55, LOGD)
# roof over the main block, with a deep eave and a chimney
prism(8.6, 2.6, 2.1, 0, 0, 7.1, ROOF)
box(0.8, 0.8, 1.8, 2.4, 0.2, 7.6, STONE)
# the clock tower on the left
TF = -1.5
box(2.4, 2.7, 8.2, -4.1, -0.15, 0, LOG)
for k in range(8):
    box(2.46, 2.76, 0.07, -4.1, -0.15, 0.8 + k * 0.9, LOGD)
box(2.8, 3.0, 0.3, -4.1, -0.15, 8.2, TRIM)
prism(3.0, 3.4, 2.2, -4.1, -0.15, 8.5, ROOF)
cyl(0.1, 0.1, -4.1, -0.15, 10.9, GOLD)
box(0.09, 0.09, 0.9, -4.1, -0.2, 10.7, GOLD)
# the clock face and hands
cyl(0.85, 0.16, -4.1, TF - 0.05, 6.5, TRIM)
cyl(0.72, 0.12, -4.1, TF - 0.14, 6.5, CREAM)
box(0.07, 0.07, 0.5, -4.1, TF - 0.22, 6.5, DARK)
box(0.4, 0.07, 0.07, -3.95, TF - 0.22, 6.5, DARK)
for k in range(12):
    a = k * math.pi / 6
    box(0.07, 0.05, 0.1, -4.1 + 0.6 * math.sin(a), TF - 0.2, 6.45 + 0.6 * math.cos(a), DARK)
# a tall arched window and a flower box under the clock
box(1.0, 0.12, 1.5, -4.1, TF - 0.03, 2.0, CREAM)
box(0.8, 0.06, 1.3, -4.1, TF - 0.1, 2.1, GLASS)
box(0.06, 0.07, 1.3, -4.1, TF - 0.14, 2.1, CREAM)
box(1.1, 0.3, 0.22, -4.1, TF - 0.2, 1.8, GREEN)
# the porch: posts, a low roof, and wide steps
box(3.6, 1.3, 0.2, 0.6, F - 0.9, 0, STONE)
box(2.8, 0.7, 0.25, 0.6, F - 1.55, 0, STONE)
for s in (-1, 1):
    box(0.28, 0.28, 2.7, 0.6 + s * 1.6, F - 1.35, 0.2, TRIM)
box(3.9, 1.6, 0.22, 0.6, F - 0.85, 2.9, TRIM)
wedge(3.9, 1.5, 0.7, 0.6, F - 0.05, 3.1, ROOF)
# double door with a gold handle and a green wreath
box(1.7, 0.14, 2.5, 0.6, F - 0.02, 0.2, TRIM)
box(1.4, 0.08, 2.3, 0.6, F - 0.09, 0.3, LOGD)
box(0.06, 0.1, 2.3, 0.6, F - 0.14, 0.3, TRIM)
box(0.1, 0.1, 0.4, 0.45, F - 0.16, 1.3, GOLD); box(0.1, 0.1, 0.4, 0.75, F - 0.16, 1.3, GOLD)
cyl(0.3, 0.06, 0.6, F - 0.2, 2.1, GREEN)
# windows with shutters
for x in (-1.1, 2.7):
    box(1.3, 0.12, 1.4, x, F - 0.03, 1.2, CREAM)
    box(1.05, 0.06, 1.15, x, F - 0.1, 1.32, GLASS)
    box(0.06, 0.07, 1.15, x, F - 0.14, 1.32, CREAM); box(1.05, 0.07, 0.06, x, F - 0.14, 1.85, CREAM)
    for s in (-1, 1):
        box(0.35, 0.1, 1.3, x + s * 0.85, F - 0.04, 1.25, GREEN)
for x in (-1.1, 0.6, 2.3, 3.4):
    box(1.0, 0.12, 1.3, x, F - 0.03, 4.9, CREAM)
    box(0.8, 0.06, 1.1, x, F - 0.1, 5.0, GLASS)
    box(0.06, 0.07, 1.1, x, F - 0.14, 5.0, CREAM)
    box(1.1, 0.3, 0.22, x, F - 0.2, 4.7, GREEN)
    for j, c in enumerate(("#ff7ab8", "#ffd23f", "#ffffff")):
        box(0.16, 0.16, 0.16, x - 0.3 + j * 0.3, F - 0.22, 5.0 + 0.0, c)
# a balcony rail across the upper storey
box(7.4, 0.9, 0.1, 0, F - 0.5, 4.4, TRIM)
for k in range(15):
    box(0.07, 0.07, 0.6, -3.4 + k * 0.5, F - 0.9, 4.5, TRIM)
box(7.4, 0.1, 0.08, 0, F - 0.9, 5.1, TRIM)
# a wooden sign board over the porch
box(2.6, 0.14, 0.7, 0.6, F - 1.0, 3.35, TRIM)
box(2.35, 0.06, 0.5, 0.6, F - 1.1, 3.45, "#e8d3a0")
text("LODGE", 0.42, 0.6, F - 1.16, 3.5, DARK, 0.05)
# firewood stack beside the door
for r in range(3):
    for c in range(4):
        cyl(0.17, 0.5, 3.0 + c * 0.35 + (r % 2) * 0.17, F - 0.45, 0.2 + r * 0.3, "#c08a4a", 12)
export("/work/out/skiLodge.json")
