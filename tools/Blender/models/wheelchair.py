"""A hospital wheelchair, built round someone sitting in it.

Unlike the other models this one is in the game's own units, not metres: it has to wrap the seated
figure exactly and its big wheels turn, so they must be round in the world (where heights are
squashed). x = px / 45.25 across, z = px / 39.19 up, as the people are drawn. It faces -Y.

Parts: "Frame" (tubes, back, armrests, castors, footplates), "Seat" (the sling seat, lowered for a
child) and "Wheel" (one big wheel centred on its own hub, push rim on the +X side, so the game can
spin it)."""
import math

from bhkit import cube, cyl, mat, torus, tube

BUDGET = 3500

W = 1 / 45.254834
V = 1 / 39.191836


def build():
    frame = mat("Frame", "#9AA5AD", 0.4, metal=0.3)
    navy = mat("Sling", "#2B4A6B", 0.8)
    navy_dark = mat("SlingDark", "#223C58", 0.8)
    rubber = mat("Rubber", "#22272B", 0.6)
    grip = mat("Grip", "#2B3238", 0.7)
    plate = mat("Plate", "#3A4148", 0.6)
    rim = mat("Rim", "#C9D2D9", 0.35, metal=0.3)
    spoke = mat("Spoke", "#6A757E", 0.4)

    seat = 13 * V
    for sx in (-1, 1):
        x = sx * 9.5 * W
        # The back upright curving into the push handle, and the seat rail running forward to the front leg.
        tube(f"Upright{sx}", [(x, 0.18, 8 * V), (x, 0.2, 42 * V), (x, 0.23, 44 * V), (x, 0.31, 44 * V)], 0.016, frame, part="Frame")
        tube(f"Handle{sx}", [(x, 0.25, 44 * V), (x, 0.33, 44 * V)], 0.024, grip, part="Frame")
        tube(f"Rail{sx}", [(x, 0.18, seat - 0.03), (x, -0.14, seat - 0.03), (sx * 0.17, -0.3, 6 * V), (sx * 0.16, -0.36, 4.2 * V)], 0.015, frame, part="Frame")
        tube(f"Lower{sx}", [(x, 0.18, 8 * V), (x, -0.1, 8 * V), (sx * 0.17, -0.3, 6 * V)], 0.012, frame, part="Frame", sides=0)
        # Armrest on a hoop.
        ax = sx * 10.5 * W
        tube(f"ArmHoop{sx}", [(ax, 0.16, seat - 0.02), (ax, 0.16, 24 * V), (ax, -0.1, 24 * V), (ax, -0.12, seat - 0.02)], 0.012, frame, part="Frame", sides=0)
        cube(f"ArmPad{sx}", (ax, 0.02, 25 * V), (0.06, 0.26, 0.045), grip, part="Frame", bevel=0.015)
        # Front castor and footplate.
        cyl(f"CastorFork{sx}", (sx * 0.16, -0.36, 3.6 * V), 0.012, 1.6 * V, frame, verts=8, part="Frame")
        cyl(f"Castor{sx}", (sx * 0.16, -0.375, 0.062), 0.062, 0.04, rubber, rot=(0, math.pi / 2, 0), verts=12, part="Frame")
        cyl(f"CastorHub{sx}", (sx * 0.16, -0.375, 0.062), 0.025, 0.045, rim, rot=(0, math.pi / 2, 0), verts=8, part="Frame")
        tube(f"FootHanger{sx}", [(sx * 0.17, -0.3, 6 * V), (sx * 0.12, -0.42, 2.8 * V)], 0.01, frame, part="Frame", sides=0)
        cube(f"Footplate{sx}", (sx * 0.095, -0.43, 2.5 * V), (0.15, 0.12, 0.018), plate, part="Frame", bevel=0.006)

    tube("BackBrace", [(-9.5 * W, 0.19, 18 * V), (9.5 * W, 0.19, 18 * V)], 0.01, frame, part="Frame", sides=0)
    back = cube("Back", (0, 0.195, 29 * V), (19 * W, 0.035, 24 * V), navy, part="Frame", bevel=0.012)
    for v in back.data.vertices:  # the sling sags back a little in the middle
        v.co.y += 0.03 * (1 - (v.co.x / (9.5 * W)) ** 2)
    cube("BackPocket", (0, 0.222, 24 * V), (12 * W, 0.012, 9 * V), navy_dark, part="Frame", smooth=False)

    cube("Seat", (0, -0.06, seat - 1.3 * V), (20 * W, 0.4, 2.6 * V), navy, part="Seat", bevel=0.025, bevel_segments=3)

    # One big wheel round the origin, turning about X.
    r = 9.2 * V
    turn = (0, math.pi / 2, 0)
    torus("Tyre", (0, 0, 0), r - 0.02, 0.022, rubber, rot=turn, seg=24, sides=6, part="Wheel")
    torus("WheelRim", (0, 0, 0), r - 0.045, 0.009, rim, rot=turn, seg=24, sides=4, part="Wheel")
    torus("PushRim", (0.04, 0, 0), r - 0.06, 0.008, rim, rot=turn, seg=24, sides=4, part="Wheel")
    cyl("Hub", (0, 0, 0), 0.03, 0.07, rim, rot=turn, verts=10, part="Wheel")
    for i in range(8):
        a = i * math.pi / 4
        end = (0.0, math.cos(a) * (r - 0.045), math.sin(a) * (r - 0.045))
        tube(f"Spoke{i}", [(0, 0, 0), end], 0.005, spoke, part="Wheel", sides=0)
    for i in range(3):
        a = i * math.pi * 2 / 3 + 0.5
        tube(f"RimStay{i}", [(0, math.cos(a) * (r - 0.05), math.sin(a) * (r - 0.05)), (0.04, math.cos(a) * (r - 0.06), math.sin(a) * (r - 0.06))], 0.005, rim, part="Wheel", sides=0)
