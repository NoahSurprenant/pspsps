import sys; sys.path.insert(0, "/work")
from lib import *
reset()
ORANGE, BROWN, DARK, CREAM, GLASS, RED, GREEN = "#e3a755", "#7a4a2a", "#4a3426", "#fff0c8", "#ffe9a8", "#a14630", "#4f9a4a"
F = -0.7  # the front wall
box(6.3, 1.7, 0.35, 0, 0, 0, "#5a3a22")                         # plinth
box(6.0, 1.4, 4.4, 0, 0, 0.35 - 0.35, ORANGE)                   # ground storey
box(6.6, 1.7, 0.3, 0, 0, 4.4, BROWN)                            # band
box(6.3, 1.4, 3.6, 0, 0, 4.7, ORANGE)                           # upper storey
box(7.2, 1.9, 0.35, 0, 0, 8.3, CREAM)                           # cornice
prism(7.0, 2.3, 1.9, 0, 0, 8.65, RED)                           # roof
box(7.2, 0.12, 0.14, 0, -1.2, 8.62, BROWN)                      # eave trim
# door
box(1.7, 0.14, 2.45, 0, F - 0.02, 0.0, BROWN)
box(1.2, 0.08, 2.15, 0, F - 0.09, 0.05, DARK)
box(0.7, 0.04, 0.8, 0, F - 0.14, 1.25, GLASS)
box(0.9, 0.5, 0.18, 0, F - 0.25, 2.5, BROWN)
for s in (-1, 1):
    box(0.34, 0.4, 2.7, s * 1.15, F - 0.18, 0.0, BROWN)
    # display window with a striped awning over it
    x = s * 2.1
    box(1.6, 0.12, 1.65, x, F - 0.03, 0.55, BROWN)
    box(1.3, 0.06, 1.35, x, F - 0.1, 0.7, GLASS)
    box(0.07, 0.07, 1.35, x, F - 0.15, 0.7, BROWN); box(1.3, 0.07, 0.07, x, F - 0.15, 1.35, BROWN)
    box(1.8, 0.4, 0.14, x, F - 0.17, 0.42, BROWN)
    for k in range(4):
        wedge(0.45, 0.65, 0.4, x - 0.675 + k * 0.45, F - 0.02, 2.2, ORANGE if k % 2 == 0 else CREAM)
# big sign
box(5.4, 0.16, 1.3, 0, F - 0.1, 3.0, BROWN)
box(5.2, 0.05, 1.1, 0, F - 0.2, 3.1, "#3a2012")
text("COFFEE", 0.85, 0, F - 0.26, 3.65, CREAM, 0.06)
# upper windows
for x in (-2.1, 0, 2.1):
    box(1.6, 0.12, 2.2, x, F - 0.03, 5.15, BROWN)
    box(1.2, 0.06, 1.8, x, F - 0.1, 5.35, GLASS)
    box(0.07, 0.07, 1.8, x, F - 0.15, 5.35, BROWN); box(1.2, 0.07, 0.07, x, F - 0.15, 6.2, BROWN)
    box(0.4, 0.08, 1.9, x - 0.95, F - 0.08, 5.3, "#3f7a8a"); box(0.4, 0.08, 1.9, x + 0.95, F - 0.08, 5.3, "#3f7a8a")
    box(1.9, 0.5, 0.16, x, F - 0.22, 4.95, CREAM)
    box(1.6, 0.34, 0.3, x, F - 0.3, 5.1, GREEN)
    for j, c in enumerate(("#ff7ab8", "#ffd23f", "#ffffff", "#ff7ab8", "#ffd23f")):
        box(0.2, 0.2, 0.2, x - 0.6 + j * 0.3, F - 0.3, 5.4, c)
# timber posts
for s in (-1, 1):
    box(0.3, 0.2, 3.6, s * 3.05, F - 0.08, 4.7, BROWN)
# dormer
box(1.7, 1.0, 1.5, 0, -0.35, 9.05, ORANGE)
box(1.1, 0.06, 0.95, 0, -0.88, 9.3, GLASS)
box(0.07, 0.07, 0.95, 0, -0.93, 9.3, BROWN)
box(2.2, 1.5, 0.14, 0, -0.3, 10.55, RED)
box(1.9, 0.3, 0.16, 0, -0.88, 8.98, CREAM)
# chimney
box(0.95, 0.95, 1.9, 2.1, 0.2, 9.2, "#7a3a2a"); box(1.15, 1.15, 0.22, 2.1, 0.2, 11.1, "#4a2018")
export("/work/out/coffee.json")
