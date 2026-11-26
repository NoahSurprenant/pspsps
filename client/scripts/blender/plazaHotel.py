import sys; sys.path.insert(0, "/work")
from lib import *
reset()
SAGE, CREAM, GOLD, DARK, GLASS, TEAL, RED, BROWN = "#9aab6a", "#f4ecd0", "#e0b84a", "#3a3028", "#ffe9a8", "#4fa6a6", "#a8433a", "#6a4a32"
F = -0.7
box(4.9, 1.7, 0.4, 0, 0, 0, "#5a5648")                            # plinth
box(4.6, 1.4, 4.0, 0, 0, 0, SAGE)                                 # ground storey
box(4.9, 1.6, 0.25, 0, 0, 4.0, CREAM)                             # band
box(4.6, 1.4, 3.2, 0, 0, 4.25, SAGE)                              # middle storey
box(4.9, 1.6, 0.25, 0, 0, 7.45, CREAM)
box(4.4, 1.3, 2.4, 0, 0, 7.7, SAGE)                               # top storey
box(5.0, 1.8, 0.3, 0, 0, 10.1, CREAM)                             # cornice
prism(4.8, 2.0, 1.8, 0, 0, 10.4, TEAL)                            # roof
for s in (-1, 1):
    box(0.35, 0.3, 10.1, s * 2.4, F - 0.06, 0, CREAM)             # corner pilasters
# the doorway: gold double door under a canopy
box(2.0, 0.14, 2.6, 0, F - 0.04, 0.0, GOLD)
box(0.95, 0.08, 2.35, -0.5, F - 0.11, 0.05, DARK); box(0.95, 0.08, 2.35, 0.5, F - 0.11, 0.05, DARK)
box(0.07, 0.1, 2.35, 0, F - 0.15, 0.05, GOLD)
cyl(0.45, 0.12, 0, F - 0.12, 2.95, GOLD); cyl(0.3, 0.1, 0, F - 0.16, 2.95, DARK)
box(2.8, 0.9, 0.2, 0, F - 0.4, 3.1, RED)                          # canopy
for k in range(5):
    box(0.5, 0.08, 0.12, -1.2 + k * 0.6, F - 0.86, 3.1, "#ffffff" if k % 2 == 0 else RED)
for s in (-1, 1):
    box(0.14, 0.14, 3.1, s * 1.3, F - 0.84, 0, GOLD)
# the sign
box(4.3, 0.18, 1.1, 0, F - 0.1, 4.65, DARK)
box(4.1, 0.05, 0.92, 0, F - 0.2, 4.74, "#2a3a5a")
text("HOTEL", 0.9, 0, F - 0.26, 5.05, "#ffd23f", 0.08)
# windows, two for each of the upper storeys, and round ones at the top
for z in (5.95, 8.0):
    for x in (-1.3, 1.3):
        box(1.0, 0.12, 1.4, x, F - 0.03, z, GOLD)
        box(0.78, 0.06, 1.18, x, F - 0.1, z + 0.11, GLASS)
        box(0.07, 0.07, 1.18, x, F - 0.15, z + 0.11, GOLD); box(0.78, 0.07, 0.07, x, F - 0.15, z + 0.7, GOLD)
        box(1.25, 0.28, 0.14, x, F - 0.2, z - 0.1, CREAM)
cyl(0.5, 0.12, 0, F - 0.04, 8.9, GOLD); cyl(0.4, 0.1, 0, F - 0.1, 8.9, GLASS)
# dormers
for s in (-1, 1):
    box(0.9, 0.8, 0.9, s * 1.3, -0.3, 10.7, SAGE); prism(1.1, 1.0, 0.5, s * 1.3, -0.3, 11.6, TEAL)
    box(0.55, 0.06, 0.6, s * 1.3, F - 0.04, 10.85, GLASS)
# flagpole and flag
box(0.08, 0.08, 1.9, 0, -0.1, 12.2, DARK); box(0.8, 0.04, 0.5, 0.45, -0.1, 13.4, RED)
export("/work/out/hotel.json")
