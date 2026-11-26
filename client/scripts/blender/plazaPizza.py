import sys; sys.path.insert(0, "/work")
from lib import *
reset()
BRICK, CREAM, RED, DARK, GLASS, GREEN, GOLD, BROWN = "#c8603a", "#fff0c8", "#c8352a", "#3a2418", "#ffe9a8", "#3f9a4a", "#ffd23f", "#6a3a22"
F = -0.7
box(6.3, 1.7, 0.35, 0, 0, 0, "#5a4a3a")                           # plinth
box(6.0, 1.4, 4.4, 0, 0, 0, BRICK)                                # ground storey
box(6.3, 1.6, 0.3, 0, 0, 4.4, CREAM)
box(6.0, 1.4, 3.0, 0, 0, 4.7, BRICK)                              # upper storey
box(6.5, 1.7, 0.3, 0, 0, 7.7, CREAM)
prism(6.4, 2.2, 1.4, 0, 0, 8.0, RED)
# the door with a round window
box(1.7, 0.14, 2.45, 0, F - 0.02, 0.0, BROWN)
box(1.2, 0.08, 2.15, 0, F - 0.09, 0.05, DARK)
cyl(0.32, 0.06, 0, F - 0.14, 1.5, GLASS)
for s in (-1, 1):
    x = s * 2.1
    box(1.6, 0.12, 1.65, x, F - 0.03, 0.55, CREAM)
    box(1.3, 0.06, 1.35, x, F - 0.1, 0.7, GLASS)
    box(0.07, 0.07, 1.35, x, F - 0.15, 0.7, CREAM); box(1.3, 0.07, 0.07, x, F - 0.15, 1.35, CREAM)
    for k in range(4):
        wedge(0.45, 0.7, 0.4, x - 0.675 + k * 0.45, F - 0.02, 2.2, RED if k % 2 == 0 else CREAM)
for k in range(4):
    wedge(0.5, 0.9, 0.45, -0.75 + k * 0.5, F - 0.02, 2.55, RED if k % 2 == 0 else CREAM)
# the sign
box(5.6, 0.18, 1.5, 0, F - 0.12, 3.5, "#2a2a4a")
box(5.35, 0.05, 1.25, 0, F - 0.22, 3.62, RED)
text("PIZZA", 1.1, 0, F - 0.28, 4.2, GOLD, 0.09)
# upper windows with flower boxes
for x in (-1.8, 0, 1.8):
    box(1.1, 0.12, 1.6, x, F - 0.03, 5.2, CREAM)
    box(0.85, 0.06, 1.35, x, F - 0.1, 5.35, GLASS)
    box(0.07, 0.07, 1.35, x, F - 0.15, 5.35, CREAM)
    box(1.2, 0.3, 0.25, x, F - 0.2, 4.95, GREEN)
    for j, c in enumerate(("#ff7ab8", "#ffd23f", "#ffffff")):
        box(0.2, 0.2, 0.2, x - 0.35 + j * 0.35, F - 0.22, 5.25, c)
# a big pizza on the roof
cyl(1.15, 0.3, 0, -0.1, 9.8, "#e0a848"); cyl(1.0, 0.32, 0, -0.1, 9.8, "#d4503a")
for dx, dz in ((-0.4, 0.3), (0.4, 0.35), (0.0, -0.4), (-0.5, -0.3), (0.5, -0.25)):
    cyl(0.2, 0.34, dx, -0.1, 9.8 + dz, "#8a1f1a")
box(0.8, 0.8, 1.5, -2.0, 0.3, 8.8, "#7a3a2a")
export("/work/out/pizza.json")
