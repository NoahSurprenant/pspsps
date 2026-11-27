import sys; sys.path.insert(0, "/work")
from lib import *
reset()
BLUE, BLUEL, WHITE, GREY, GLASS, RED, DARK, SIGN, SILVER = "#3a6fb8", "#5b8fd8", "#f1f4f8", "#6b6f78", "#cfeaff", "#d8423a", "#2a2f3a", "#1f3f8a", "#c8ccd4"
F = -0.7
box(6.4, 1.7, 0.35, 0, 0, 0, GREY)
box(6.0, 1.4, 4.4, 0, 0, 0.35, WHITE)
box(6.3, 1.6, 0.3, 0, 0, 4.75, BLUE)
box(5.6, 1.4, 1.6, 0, 0, 5.05, WHITE)
box(6.0, 1.6, 0.3, 0, 0, 6.65, BLUEL)
# the handset on the roof: ear cup, mouth cup and the grip between
cyl(0.8, 0.9, -2.2, -0.1, 7.9, DARK); cyl(0.8, 0.9, 2.2, -0.1, 7.9, DARK)
cyl(0.55, 0.95, -2.2, -0.1, 7.9, RED); cyl(0.55, 0.95, 2.2, -0.1, 7.9, RED)
box(3.6, 0.7, 0.6, 0, -0.1, 7.6, DARK)
box(3.2, 0.5, 0.4, 0, -0.1, 7.75, RED)
box(0.9, 0.9, 0.5, -2.2, -0.1, 6.95, GREY); box(0.9, 0.9, 0.5, 2.2, -0.1, 6.95, GREY)
# big round speaker dishes on the front corners
for s in (-1, 1):
    cyl(0.95, 0.22, s * 2.35, F - 0.12, 3.0, SILVER)
    cyl(0.78, 0.26, s * 2.35, F - 0.14, 3.0, GREY)
    cyl(0.4, 0.3, s * 2.35, F - 0.16, 3.0, SILVER)
    cyl(0.12, 0.34, s * 2.35, F - 0.18, 3.0, DARK)
# glass doors with a push bar and a blue frame
box(2.2, 0.16, 2.9, 0, F - 0.03, 0.35, BLUE)
box(1.0, 0.08, 2.6, -0.5, F - 0.1, 0.5, GLASS); box(1.0, 0.08, 2.6, 0.5, F - 0.1, 0.5, GLASS)
box(0.06, 0.1, 2.6, 0, F - 0.13, 0.5, BLUE)
box(0.1, 0.1, 0.9, -0.2, F - 0.16, 1.4, SILVER); box(0.1, 0.1, 0.9, 0.2, F - 0.16, 1.4, SILVER)
# the sign over the door
box(4.6, 0.2, 1.3, 0, F - 0.12, 3.55, SIGN)
box(4.4, 0.06, 1.1, 0, F - 0.24, 3.65, "#2f5fc0")
text("PHONES", 0.75, 0, F - 0.3, 3.9, "#ffffff", 0.06)
# upper windows shaped like a keypad of coloured buttons
for x in (-1.6, 0, 1.6):
    box(1.2, 0.12, 1.1, x, F - 0.03, 5.3, SILVER)
    box(0.95, 0.06, 0.85, x, F - 0.1, 5.42, GLASS)
for r in range(3):
    for c in range(3):
        box(0.22, 0.1, 0.22, -2.4 + c * 0.3 + 0.0, F - 0.06, 0.6 + r * 0.3, RED if (r + c) % 2 == 0 else "#ffd23f")
# awning stripes over the door
for k in range(6):
    wedge(0.37, 0.7, 0.35, -0.93 + k * 0.37, F - 0.02, 3.0, RED if k % 2 == 0 else WHITE)
export("/work/out/skiPhones.json")
