import sys; sys.path.insert(0, "/work")
from lib import *
reset()
GREEN, DEEP, CREAM, GOLD, GLASS, DARK, RED, STONE = "#3f7a52", "#2a5a3c", "#f4ecd0", "#e0b84a", "#8fd0e6", "#1c2a24", "#d4503a", "#6a6a60"
F = -0.7
box(7.5, 1.7, 0.4, 0, 0, 0, STONE)                                # plinth
box(7.2, 1.4, 5.6, 0, 0, 0, GREEN)                                # the wide lower floor
box(7.5, 1.6, 0.3, 0, 0, 5.6, CREAM)
box(7.2, 1.4, 1.9, 0, 0, 5.9, DEEP)                               # parapet band
box(7.5, 1.7, 0.25, 0, 0, 7.8, CREAM)
# the glass atrium
box(3.8, 0.7, 7.6, 0, F - 0.25, 0, DEEP)
box(3.4, 0.12, 5.6, 0, F - 0.62, 0.1, GLASS)
cyl(1.7, 0.12, 0, F - 0.62, 5.7, GLASS)
cyl(1.95, 0.1, 0, F - 0.56, 5.7, GOLD)
cyl(1.7, 0.12, 0, F - 0.64, 5.7, GLASS)
for x in (-1.1, 0, 1.1):
    box(0.1, 0.1, 5.6, x, F - 0.68, 0.1, GOLD)
for z in (1.6, 3.2, 4.6):
    box(3.4, 0.1, 0.1, 0, F - 0.68, z, GOLD)
# the glass doors
box(2.4, 0.1, 2.3, 0, F - 0.7, 0.05, DARK)
box(1.1, 0.06, 2.1, -0.58, F - 0.76, 0.1, GLASS); box(1.1, 0.06, 2.1, 0.58, F - 0.76, 0.1, GLASS)
# the sign above the atrium
box(5.2, 0.2, 1.7, 0, F - 0.5, 7.4, DARK)
box(4.95, 0.06, 1.45, 0, F - 0.62, 7.52, "#0f1a14")
text("MALL", 1.4, 0, F - 0.7, 8.1, GOLD, 0.1)
# the wings: a big window with a green awning each side
for s in (-1, 1):
    x = s * 2.85
    box(1.3, 0.12, 2.6, x, F - 0.03, 1.0, CREAM)
    box(1.05, 0.06, 2.35, x, F - 0.1, 1.12, GLASS)
    box(0.07, 0.07, 2.35, x, F - 0.15, 1.12, CREAM)
    for k in range(3):
        wedge(0.45, 0.7, 0.4, x - 0.45 + k * 0.45, F - 0.02, 3.8, RED if k % 2 == 0 else CREAM)
    box(1.1, 0.12, 1.5, x, F - 0.04, 4.4, CREAM); box(0.88, 0.06, 1.28, x, F - 0.1, 4.5, GLASS)
# round windows in the parapet band
for s in (-1, 1):
    cyl(0.42, 0.1, s * 2.85, F - 0.06, 6.9, GOLD); cyl(0.32, 0.1, s * 2.85, F - 0.1, 6.9, GLASS)
# a ring logo on the roof
cyl(0.8, 0.3, 0, -0.1, 10.0, RED); cyl(0.55, 0.32, 0, -0.1, 10.0, GOLD); cyl(0.3, 0.34, 0, -0.1, 10.0, GREEN)
box(7.0, 1.2, 0.5, 0, 0, 8.05, DEEP)
export("/work/out/mall.json")
