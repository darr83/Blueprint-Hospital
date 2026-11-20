"""An A&E trolley, 0.7 m by 2.0 m: castors, a dark base tray with an oxygen cylinder, two lifting
columns, a steel deck, a blue mattress under a white sheet, tube side rails, a push bar at the head
and a drip pole. Head at +Y, so it is pushed feet first.

Heights follow the game's trolleys (28 px a metre): the sheet's top is 16 px up, the side rails
reach 20 px and the push bar 25 px, where the porter's hands go."""
from bhkit import castor, cube, cyl, mat, tube

BUDGET = 3000

PX = 1 / 28
TOP = 16 * PX


def build():
    steel = mat("Steel", "#C3CCD4", 0.4, metal=0.3)
    grey = mat("Grey", "#7F8A93", 0.5)
    dark = mat("Dark", "#4A545D", 0.55)
    rubber = mat("Rubber", "#22272B", 0.6)
    mattress = mat("Mattress", "#3E6A96", 0.55)
    sheet = mat("Sheet", "#F4F7F9", 0.85)
    white = mat("White", "#F2F4F5", 0.5)
    green = mat("Green", "#2E9C63", 0.5)
    grip = mat("Grip", "#2B3238", 0.7)

    for sx in (-1, 1):
        for sy in (-1, 1):
            castor(f"Castor{sx}{sy}", sx * 0.25, sy * 0.8, 0.06, steel, rubber, stem=0.13)

    cube("Tray", (0, 0, 5 * PX), (0.58, 1.72, 0.12), dark, bevel=0.03)
    cyl("Oxygen", (0, -0.5, 8.6 * PX), 0.062, 0.42, white, rot=(1.5708, 0, 0), verts=12)
    cyl("OxygenNeck", (0, -0.25, 8.6 * PX), 0.03, 0.08, green, rot=(1.5708, 0, 0), verts=8)
    for y in (-0.45, 0.45):
        cube(f"Column{y}", (0, y, 9 * PX), (0.15, 0.15, 0.2), grey, bevel=0.02)
        cube(f"Sleeve{y}", (0, y, 7 * PX), (0.19, 0.19, 0.07), dark, bevel=0.015)

    cube("Deck", (0, 0, 11 * PX), (0.7, 2.0, 0.065), steel, bevel=0.02)
    cube("Bumper", (0, 0, 11 * PX), (0.705, 1.4, 0.022), grey, smooth=False)
    cube("Mattress", (0, 0, TOP - 0.085), (0.62, 1.92, 0.13), mattress, bevel=0.04, bevel_segments=3)
    cube("Sheet", (0, 0, TOP - 0.014), (0.63, 1.93, 0.028), sheet, bevel=0.012)

    rail, mid, foot = 20 * PX, 16.5 * PX, 12 * PX
    for sx in (-1, 1):
        x = sx * 0.37
        tube(f"Rail{sx}", [(x, 0.75, foot), (x, 0.75, rail - 0.05), (x, 0.7, rail), (x, -0.7, rail), (x, -0.75, rail - 0.05), (x, -0.75, foot)], 0.015, steel)
        tube(f"RailMid{sx}", [(x, 0.75, mid), (x, -0.75, mid)], 0.01, steel, sides=0)
        tube(f"RailPost{sx}", [(x, 0, foot), (x, 0, rail)], 0.01, steel, sides=0)

    bar = 25 * PX
    tube("PushBar", [(-0.31, 1.02, 12 * PX), (-0.31, 1.06, bar - 0.04), (-0.27, 1.06, bar), (0.27, 1.06, bar), (0.31, 1.06, bar - 0.04), (0.31, 1.02, 12 * PX)], 0.016, steel)
    tube("PushGrip", [(-0.24, 1.06, bar), (0.24, 1.06, bar)], 0.023, grip)

    pole = 39 * PX
    tube("DripPole", [(0.35, 0.96, 12 * PX), (0.35, 0.96, pole)], 0.011, steel, sides=0)
    tube("DripHooks", [(0.27, 0.96, pole - 0.03), (0.29, 0.96, pole), (0.41, 0.96, pole), (0.43, 0.96, pole - 0.03)], 0.008, steel, sides=0)
