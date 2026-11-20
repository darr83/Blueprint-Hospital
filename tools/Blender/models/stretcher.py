"""The ambulance crew's stretcher, 0.66 m by 2.0 m: big castors on a scissor undercarriage, a steel
deck, a charcoal mattress with a raised head pad, low side rails, a yellow lift bar at the head
and a dark one at the foot. Head at +Y.

Heights follow the game's stretchers (28 px a metre): the mattress top is 16 px up."""
from bhkit import castor, cube, mat, tube

BUDGET = 3000

PX = 1 / 28
TOP = 16 * PX


def build():
    steel = mat("Steel", "#C9D1D8", 0.4, metal=0.3)
    grey = mat("Grey", "#7F8A93", 0.5)
    rubber = mat("Rubber", "#22272B", 0.6)
    pad = mat("Pad", "#2E3338", 0.7)
    seam = mat("Seam", "#444B52", 0.7)
    yellow = mat("Yellow", "#F2C230", 0.5)
    grip = mat("Grip", "#2B3238", 0.7)

    for sx in (-1, 1):
        for sy in (-1, 1):
            castor(f"Castor{sx}{sy}", sx * 0.275, sy * 0.85, 0.07, steel, rubber, stem=0.14)

    base, under = 4.6 * PX, 12 * PX
    for sx in (-1, 1):
        x = sx * 0.275
        tube(f"Base{sx}", [(x, -0.85, base), (x, 0.85, base)], 0.018, grey)
        tube(f"LegA{sx}", [(x, -0.7, base), (x, 0.7, under)], 0.016, steel)
        tube(f"LegB{sx}", [(x, 0.7, base), (x, -0.7, under)], 0.016, steel)
    for y in (-0.85, 0.85):
        tube(f"Cross{y}", [(-0.275, y, base), (0.275, y, base)], 0.018, grey)
    tube("Pivot", [(-0.3, 0, (base + under) / 2), (0.3, 0, (base + under) / 2)], 0.02, grey)

    cube("Deck", (0, 0, 13 * PX), (0.66, 2.0, 0.06), steel, bevel=0.02)
    cube("Mattress", (0, -0.02, TOP - 0.045), (0.6, 1.9, 0.085), pad, bevel=0.03, bevel_segments=3)
    for y in (-0.5, 0.2):
        cube(f"Seam{y}", (0, y, TOP - 0.002), (0.6, 0.012, 0.006), seam, smooth=False)

    rail = 17.5 * PX
    for sx in (-1, 1):
        x = sx * 0.35
        tube(f"Rail{sx}", [(x, 0.7, 13 * PX), (x, 0.66, rail), (x, -0.66, rail), (x, -0.7, 13 * PX)], 0.013, steel)
        tube(f"RailPost{sx}", [(x, 0, 13 * PX), (x, 0, rail)], 0.01, steel, sides=0)

    lift = 17 * PX
    tube("HeadBar", [(-0.275, 0.95, 13.5 * PX), (-0.31, 1.075, lift), (0.31, 1.075, lift), (0.275, 0.95, 13.5 * PX)], 0.016, steel)
    tube("HeadGrip", [(-0.26, 1.075, lift), (0.26, 1.075, lift)], 0.024, yellow)
    tube("FootBar", [(-0.275, -0.95, 13.5 * PX), (-0.3, -1.06, 15 * PX), (0.3, -1.06, 15 * PX), (0.275, -0.95, 13.5 * PX)], 0.016, steel)
    tube("FootGrip", [(-0.22, -1.06, 15 * PX), (0.22, -1.06, 15 * PX)], 0.022, grip)
