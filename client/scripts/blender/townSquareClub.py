import sys; sys.path.insert(0, "/work")
from lib import *
reset()
PURPLE, LILAC, DARK, TEAL, GOLD, PINK, GLASS, NIGHT = "#6b4a9e", "#7d5bb3", "#2f2147", "#1fa89c", "#ffd23f", "#ff7ad9", "#ffe9a8", "#1b1030"
F = -0.7
box(6.8, 1.7, 0.3, 0, 0, 0, DARK)                                 # plinth
box(6.4, 1.4, 4.8, 0, 0, 0, PURPLE)                               # ground storey
box(6.8, 1.6, 0.3, 0, 0, 4.8, TEAL)                               # teal band
box(6.2, 1.4, 3.7, 0, 0, 5.1, LILAC)                              # upper storey
box(6.9, 1.7, 0.25, 0, 0, 8.8, DARK)                              # crown steps
box(5.2, 1.5, 0.35, 0, 0, 9.05, PURPLE)
box(3.4, 1.3, 0.35, 0, 0, 9.4, TEAL)
cyl(0.8, 0.2, 0, F - 0.05, 9.95, GOLD); cyl(0.55, 0.22, 0, F - 0.07, 9.95, DARK)
for k in range(5):
    box(0.18, 0.12, 0.8, -1.4 + k * 0.7, F, 9.75, GOLD)
# pillars with caps
for s in (-1, 1):
    box(0.55, 0.5, 8.8, s * 3.3, F - 0.05, 0, TEAL)
    box(0.8, 0.7, 0.22, s * 3.3, F - 0.05, 8.8, GOLD)
    box(0.8, 0.7, 0.22, s * 3.3, F - 0.05, 0.0, GOLD)
    for z in (2.4, 4.8, 7.2):
        box(0.7, 0.62, 0.1, s * 3.3, F - 0.08, z, GOLD)
# doorway with a gold arch
box(2.3, 0.16, 2.5, 0, F - 0.1, 0.05, NIGHT)
cyl(1.15, 0.16, 0, F - 0.1, 2.55, NIGHT)
cyl(1.3, 0.12, 0, F - 0.04, 2.55, GOLD)
cyl(1.15, 0.16, 0, F - 0.12, 2.55, NIGHT)
box(0.18, 0.2, 2.6, -1.25, F - 0.1, 0.0, GOLD); box(0.18, 0.2, 2.6, 1.25, F - 0.1, 0.0, GOLD)
# speaker stacks beside the door
for s in (-1, 1):
    x = s * 2.35
    box(0.95, 0.5, 1.9, x, F - 0.28, 0.1, NIGHT)
    cyl(0.32, 0.1, x, F - 0.56, 1.45, "#555a66"); cyl(0.15, 0.12, x, F - 0.58, 1.45, "#222630")
    cyl(0.22, 0.1, x, F - 0.56, 0.7, "#555a66"); cyl(0.1, 0.12, x, F - 0.58, 0.7, "#222630")
# the sign over the door, with a bulb frame
box(5.9, 0.2, 1.7, 0, F - 0.1, 2.95, TEAL)
box(5.6, 0.06, 1.4, 0, F - 0.22, 3.1, NIGHT)
text("DANCE", 0.62, -1.25, F - 0.3, 3.95, GOLD, 0.08)
text("CLUB", 0.62, 1.5, F - 0.3, 3.95, GOLD, 0.08)
star(0.3, 0.08, 0, F - 0.3, 3.95, PINK)
for k in range(15):
    c = "#ffffff" if k % 2 == 0 else "#ffff65"
    box(0.17, 0.17, 0.17, -2.8 + k * 0.4, F - 0.26, 4.5, c); box(0.17, 0.17, 0.17, -2.8 + k * 0.4, F - 0.26, 3.2, c)
# stars above the arched windows
for s_ in (-1, 1):
    star(0.5, 0.1, s_ * 2.3, F - 0.18, 8.0, GOLD)
star(0.35, 0.1, 0, F - 0.18, 9.6, PINK)
# arched windows either side of the ball
for s in (-1, 1):
    x = s * 2.3
    box(1.3, 0.12, 1.5, x, F - 0.04, 5.55, TEAL); cyl(0.65, 0.12, x, F - 0.04, 7.05, TEAL)
    box(1.05, 0.06, 1.5, x, F - 0.11, 5.55, GLASS); cyl(0.525, 0.06, x, F - 0.11, 7.05, GLASS)
    box(0.07, 0.07, 2.5, x, F - 0.16, 5.55, TEAL); box(1.05, 0.07, 0.07, x, F - 0.16, 6.3, TEAL)
# neon strips
for z in (4.95, 8.75):
    box(6.4, 0.06, 0.08, 0, F - 0.12, z, "#2fe0d0")
export("/work/out/club.json")
