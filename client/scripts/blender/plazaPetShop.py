import sys; sys.path.insert(0, "/work")
from lib import *
reset()
SUN, CREAM, BROWN, DARK, GLASS, ORANGE, GREEN, TEAL, PINK = "#f2d680", "#fff0c8", "#7a4a2a", "#4a3426", "#ffe9a8", "#f29a3a", "#4fae5a", "#3a9a9a", "#f29bc1"
F = -0.7
box(6.3, 1.7, 0.35, 0, 0, 0, "#6a5a44")                          # plinth
box(6.0, 1.4, 4.3, 0, 0, 0, SUN)                                  # ground storey
box(6.3, 1.6, 0.3, 0, 0, 4.3, ORANGE)                             # band
box(6.0, 1.4, 3.0, 0, 0, 4.6, CREAM)                              # upper storey
box(6.5, 1.7, 0.3, 0, 0, 7.6, TEAL)                               # cornice
prism(6.4, 2.2, 1.5, 0, 0, 7.9, GREEN)                            # roof
# the door and a window with a striped awning either side
box(1.7, 0.14, 2.45, 0, F - 0.02, 0.0, BROWN)
box(1.2, 0.08, 2.15, 0, F - 0.09, 0.05, DARK)
box(0.7, 0.04, 0.8, 0, F - 0.14, 1.25, GLASS)
for s in (-1, 1):
    x = s * 2.1
    box(1.6, 0.12, 1.65, x, F - 0.03, 0.55, BROWN)
    box(1.3, 0.06, 1.35, x, F - 0.1, 0.7, GLASS)
    box(0.07, 0.07, 1.35, x, F - 0.15, 0.7, BROWN); box(1.3, 0.07, 0.07, x, F - 0.15, 1.35, BROWN)
    for k in range(4):
        wedge(0.45, 0.7, 0.4, x - 0.675 + k * 0.45, F - 0.02, 2.2, ORANGE if k % 2 == 0 else GREEN)
# a big striped awning over the door
for k in range(4):
    wedge(0.5, 0.9, 0.45, -0.75 + k * 0.5, F - 0.02, 2.55, ORANGE if k % 2 == 0 else GREEN)
# the sign
box(5.6, 0.18, 1.45, 0, F - 0.12, 3.5, TEAL)
box(5.35, 0.05, 1.2, 0, F - 0.22, 3.62, "#1f5f66")
text("PET SHOP", 0.95, 0, F - 0.28, 4.22, "#ffd23f", 0.08)
# upper windows
for x in (-1.8, 0, 1.8):
    box(1.1, 0.12, 1.6, x, F - 0.03, 5.15, BROWN)
    box(0.85, 0.06, 1.35, x, F - 0.1, 5.3, GLASS)
    box(0.07, 0.07, 1.35, x, F - 0.15, 5.3, BROWN)
    box(1.3, 0.3, 0.2, x, F - 0.22, 4.9, PINK)
# a big cat head on the roof
cyl(1.05, 0.5, 0, -0.1, 9.45, "#f2a3c8"); cyl(0.9, 0.2, 0, F - 0.1, 9.45, "#f8c8de")
for s in (-1, 1):
    prism(0.7, 0.45, 0.75, s * 0.75, -0.1, 10.2, "#f2a3c8")
    cyl(0.16, 0.1, s * 0.4, F - 0.22, 9.65, "#2a2230"); cyl(0.07, 0.1, s * 0.4 + 0.04, F - 0.28, 9.7, "#ffffff")
    for j in (-1, 0, 1):
        box(0.5, 0.04, 0.03, s * 0.85, F - 0.24, 9.25 + j * 0.1, "#ffffff")
cyl(0.12, 0.1, 0, F - 0.22, 9.3, "#d4607a")
# chimney
box(0.8, 0.8, 1.5, 2.0, 0.3, 8.8, "#8a4a3a")
export("/work/out/petShop.json")
