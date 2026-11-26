import sys; sys.path.insert(0, "/work")
from lib import *
reset()
BROWN, WALL, DARK, CREAM, GLASS, GREEN, TEAL, PINK = "#8a5a3a", "#9a6542", "#4a2e1c", "#f4ead2", "#ffe9a8", "#3a5a4a", "#1fa89c", "#e0509a"
F = -0.7
box(6.3, 1.7, 0.3, 0, 0, 0, "#3a2414")                            # plinth
box(6.0, 1.4, 4.0, 0, 0, 0, WALL)                                 # ground storey
box(6.3, 1.6, 0.3, 0, 0, 4.0, DARK)                               # band
box(6.3, 1.4, 4.0, 0, 0, 4.3, BROWN)                              # upper storey
box(6.7, 1.8, 0.35, 0, 0, 8.3, CREAM)                             # cornice
prism(6.6, 2.3, 1.9, 0, 0, 8.65, GREEN)                           # roof
box(6.7, 0.12, 0.14, 0, -1.2, 8.62, CREAM)
# door with a teal awning
box(1.7, 0.14, 2.4, 0, F - 0.02, 0.0, DARK)
box(1.2, 0.08, 2.1, 0, F - 0.09, 0.05, "#6b4a36")
box(0.6, 0.04, 0.9, 0, F - 0.14, 1.15, GLASS)
for k in range(6):
    wedge(0.5, 0.75, 0.45, -1.25 + k * 0.5, F - 0.02, 2.6, TEAL if k % 2 == 0 else CREAM)
# display windows with clothes inside
for s in (-1, 1):
    x = s * 2.15
    box(1.7, 0.12, 1.75, x, F - 0.03, 0.55, DARK)
    box(1.4, 0.06, 1.45, x, F - 0.1, 0.7, GLASS)
    box(0.07, 0.07, 1.45, x, F - 0.15, 0.7, DARK); box(1.4, 0.07, 0.07, x, F - 0.15, 1.4, DARK)
    box(1.9, 0.4, 0.14, x, F - 0.17, 0.42, DARK)
    box(0.5, 0.05, 0.7, x - 0.3, F - 0.14, 0.85, PINK); box(0.5, 0.05, 0.7, x + 0.3, F - 0.14, 0.85, TEAL)
    box(0.12, 0.05, 0.4, x, F - 0.14, 1.45, "#ffd23f")
# the big sign
box(5.6, 0.16, 1.3, 0, F - 0.1, 2.85, DARK)
box(5.4, 0.05, 1.1, 0, F - 0.2, 2.95, "#2a1a10")
text("CLOTHES", 0.78, 0, F - 0.26, 3.5, "#ff9ad0", 0.06)
# upper windows with shutters and flower boxes
for x in (-2.1, 0, 2.1):
    box(1.6, 0.12, 2.2, x, F - 0.03, 4.85, DARK)
    box(1.2, 0.06, 1.8, x, F - 0.1, 5.05, GLASS)
    box(0.07, 0.07, 1.8, x, F - 0.15, 5.05, DARK); box(1.2, 0.07, 0.07, x, F - 0.15, 5.9, DARK)
    box(0.4, 0.08, 1.9, x - 0.95, F - 0.08, 5.0, TEAL); box(0.4, 0.08, 1.9, x + 0.95, F - 0.08, 5.0, TEAL)
    box(1.9, 0.5, 0.16, x, F - 0.22, 4.65, CREAM)
    box(1.6, 0.34, 0.3, x, F - 0.3, 4.8, "#4f9a4a")
    for j, c in enumerate(("#ff7ab8", "#ffd23f", "#ffffff", "#ff7ab8", "#ffd23f")):
        box(0.2, 0.2, 0.2, x - 0.6 + j * 0.3, F - 0.3, 5.1, c)
# timber posts
for s in (-1, 1):
    box(0.3, 0.2, 4.0, s * 3.05, F - 0.08, 4.3, DARK)
# dormer with a gift-box finial
box(1.7, 1.0, 1.5, 0, -0.35, 9.05, BROWN)
box(1.1, 0.06, 0.95, 0, -0.88, 9.3, GLASS)
box(2.2, 1.5, 0.14, 0, -0.3, 10.55, GREEN)
box(0.7, 0.7, 0.6, 0, -0.3, 10.65, PINK); box(0.14, 0.72, 0.62, 0, -0.3, 10.65, CREAM); box(0.72, 0.14, 0.62, 0, -0.3, 10.65, CREAM)
# chimney
box(0.95, 0.95, 1.9, -2.1, 0.2, 9.2, "#7a3a2a"); box(1.15, 1.15, 0.22, -2.1, 0.2, 11.1, "#4a2018")
export("/work/out/clothes.json")
