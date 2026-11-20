"""A baby's cot, 0.5 m by 0.8 m and 0.9 m tall: a clear tub (the part "Glass") on a wheeled chrome
stand with a shelf, a little mattress and a name card on the end. The game stretches it to the
cot's footprint; the mattress top is 62% of the way up plus 3 px, where the baby lies."""
from bhkit import castor, cube, mat, tube

BUDGET = 2000

PX = 1 / 28
W, D, H = 0.5, 0.8, 0.9
RIM = H * 0.62


def build():
    chrome = mat("Chrome", "#C9D2DA", 0.35, metal=0.3)
    rubber = mat("Rubber", "#22272B", 0.6)
    shelf = mat("Shelf", "#B0BEC5", 0.5)
    mattress = mat("Mattress", "#E3F2FD", 0.85)
    blanket = mat("Blanket", "#FFF3C4", 0.9)
    card = mat("Card", "#FFFFFF", 0.8)
    pink = mat("CardBand", "#F48FB1", 0.8)
    tint = mat("Tint", "#DDEFFA", 0.2)

    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (W / 2 - 0.05), sy * (D / 2 - 0.07)
            castor(f"Castor{sx}{sy}", x, y, 0.04, chrome, rubber, stem=0.1)
            tube(f"Leg{sx}{sy}", [(x, y, 0.1), (x, y, RIM - 0.02)], 0.014, chrome, sides=0)
    for sy in (-1, 1):
        y = sy * (D / 2 - 0.07)
        tube(f"Handle{sy}", [(-(W / 2 - 0.05), y, RIM - 0.06), (-(W / 2 - 0.05), y + sy * 0.05, RIM - 0.02), (W / 2 - 0.05, y + sy * 0.05, RIM - 0.02), (W / 2 - 0.05, y, RIM - 0.06)], 0.012, chrome, sides=0)

    cube("Shelf", (0, 0, 8.5 * PX), (W - 0.08, D - 0.12, 0.03), shelf, bevel=0.012)
    cube("Nappies", (0.05, 0.1, 8.5 * PX + 0.05), (0.2, 0.26, 0.07), card, bevel=0.02)
    cube("Tray", (0, 0, RIM + 0.02), (W - 0.04, D - 0.05, 0.045), chrome, bevel=0.018)
    cube("Mattress", (0, 0, RIM + 2.2 * PX), (W - 0.12, D - 0.13, 1.6 * PX), mattress, bevel=0.02, bevel_segments=3)
    cube("Fold", (0, -0.2, RIM + 2.2 * PX + 0.004), (W - 0.115, 0.2, 1.6 * PX), blanket, bevel=0.02, bevel_segments=3)
    cube("NameCard", (0, -(D / 2 - 0.012), RIM + 0.15), (0.16, 0.006, 0.1), card, smooth=False)
    cube("NameBand", (0, -(D / 2 - 0.008), RIM + 0.185), (0.16, 0.006, 0.025), pink, smooth=False)

    tub_h = H - RIM - 0.03
    z = RIM + 0.03 + tub_h / 2
    t = 0.012
    for sx in (-1, 1):
        cube(f"TubSide{sx}", (sx * (W / 2 - 0.035), 0, z), (t, D - 0.06, tub_h), tint, part="Glass", bevel=0.004)
    for sy in (-1, 1):
        cube(f"TubEnd{sy}", (0, sy * (D / 2 - 0.035), z), (W - 0.07, t, tub_h), tint, part="Glass", bevel=0.004)
