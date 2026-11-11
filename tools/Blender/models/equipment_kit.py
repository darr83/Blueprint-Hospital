"""The key clinical equipment, as a kit of parts the game places and tints: the obs monitor's stand,
a drip stand, the resus trolley, the X-ray tube and table, the CT gantry and its table, the
operating table and the theatre lights.

Unlike the single models this kit is made in the game's own units, not metres: across the floor one
unit is a tile (half a metre), and heights are in the game's squashed scale (px / 39.19, a metre is
0.714). That keeps round things round in the world: the CT bore, castors, lamp heads. Each part
stands on the floor at its own origin with its front towards -Y, at the size of the game's furniture
entry; the game stretches it if that entry's size changes.

Materials named "Tint", "TintDark" and "TintLight" take the colour the game gives the item (and a
darker and lighter shade of it); the others keep their own colour."""
import math

import bmesh
import bpy

from bhkit import cube, cyl, finish, mat, sphere, torus, tube

BUDGET = 40000  # the whole kit

PX = 1 / 39.191836  # one pixel of height in the old 2D art
M = 28 * PX         # a metre of height


def P(x, up, z):
    """A point given the game's way (x across, height, z towards the viewer) in Blender's axes."""
    return (x, -z, up)


def S(w, tall, d):
    return (w, d, tall)


def _castor(name, part, x, z, mats, r=0.055):
    tube(f"{name}Fork", [P(x, r, z), P(x, r * 2 + 0.03, z)], 0.014, mats["chrome"], sides=0, part=part)
    cyl(f"{name}Wheel", P(x, r, z + 0.012), r, 0.045, mats["rubber"], rot=(0, math.pi / 2, 0), verts=12, part=part)
    cyl(f"{name}Hub", P(x, r, z + 0.012), r * 0.45, 0.05, mats["chrome"], rot=(0, math.pi / 2, 0), verts=8, part=part)


def _spokes(name, part, radius, material, mats):
    """A five-spoke wheeled base with a hub, like a drip stand's."""
    hub = 4.2 * PX
    cyl(f"{name}Hub", P(0, hub, 0), 0.05, 0.05, material, verts=10, part=part)
    for i in range(5):
        a = i * math.tau / 5
        x, z = math.sin(a) * radius, math.cos(a) * radius
        tube(f"{name}Spoke{i}", [P(0, hub, 0), P(x * 0.8, hub - 0.012, z * 0.8), P(x, 3.6 * PX, z)], 0.02, material, part=part)
        _castor(f"{name}Castor{i}", part, x, z, mats, r=0.045)


def _screen(name, part, centre, w, tall, mats, picture="screen"):
    x, up, z = centre
    cube(f"{name}Bezel", P(x, up, z), S(w, tall, 0.05), mats["bezel"], part=part, bevel=0.012)
    cube(f"{name}Picture", P(x, up, z + 0.027), S(w * 0.86, tall * 0.8, 0.008), mats[picture], part=part, smooth=False)
    cube(f"{name}Trace1", P(x, up + tall * 0.12, z + 0.033), S(w * 0.7, tall * 0.07, 0.004), mats["cyan"], part=part, smooth=False)
    cube(f"{name}Trace2", P(x, up - tall * 0.15, z + 0.033), S(w * 0.7, tall * 0.06, 0.004), mats["green"], part=part, smooth=False)


def _ring_block(name, part, w, base, top, d, centre_up, bore, material, n=40):
    """A block with a round hole right through it from front to back: the CT scanner's shroud."""
    bm = bmesh.new()
    rings = {}
    half_h, mid = (top - base) / 2, (top + base) / 2
    for key, y in (("f", -d / 2), ("b", d / 2)):
        outer, inner = [], []
        for i in range(n):
            a = math.tau * i / n
            c, s = math.cos(a), math.sin(a)
            ox = (w / 2) * math.copysign(abs(c) ** 0.35, c)
            oz = mid + half_h * math.copysign(abs(s) ** 0.35, s)
            outer.append(bm.verts.new((ox, y, oz)))
            inner.append(bm.verts.new((bore * c, y, centre_up + bore * s)))
        rings[key] = (outer, inner)
    fo, fi = rings["f"]
    bo, bi = rings["b"]
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((fo[i], fo[j], fi[j], fi[i]))  # front
        bm.faces.new((bo[j], bo[i], bi[i], bi[j]))  # back
        bm.faces.new((fo[j], fo[i], bo[i], bo[j]))  # outside
        bm.faces.new((fi[i], fi[j], bi[j], bi[i]))  # the bore
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return finish(o, material, part=part, bevel=0.035, bevel_segments=2)


def build():
    m = {
        "tint": mat("Tint", "#FFFFFF", 0.5),
        "tint_dark": mat("TintDark", "#CCCCCC", 0.5),
        "tint_light": mat("TintLight", "#FFFFFF", 0.5),
        "chrome": mat("Chrome", "#BCC6CE", 0.35, metal=0.3),
        "steel": mat("Steel", "#9AA6B0", 0.45, metal=0.2),
        "grey": mat("Grey", "#7D8995", 0.5),
        "dark": mat("Dark", "#4A545D", 0.55),
        "plinth": mat("Plinth", "#55616B", 0.6),
        "rubber": mat("Rubber", "#22272B", 0.6),
        "white": mat("White", "#EEF2F5", 0.5),
        "bezel": mat("Bezel", "#2B2F33", 0.5),
        "screen": mat("Screen", "#24425E", 0.3),
        "defib": mat("DefibScreen", "#173326", 0.3),
        "cyan": mat("Cyan", "#4FC3F7", 0.3),
        "green": mat("Green", "#8BE38F", 0.3),
        "yellow": mat("Yellow", "#F9C22E", 0.5),
        "blue": mat("Blue", "#7FA7C4", 0.5),
        "label": mat("Label", "#1E88E5", 0.6),
        "line": mat("Line", "#E0F2F1", 0.5),
        "pad": mat("Pad", "#4E5B67", 0.7),
        "lamp": mat("Lamp", "#D7DEE3", 0.4),
        "glow": mat("Glow", "#FFF8D6", 0.3),
        "orange": mat("Orange", "#F28C28", 0.5),
        "red": mat("Red", "#C62828", 0.5),
    }
    tint, chrome, dark, grey = m["tint"], m["chrome"], m["dark"], m["grey"]

    # ---- obs monitor stand: wheeled base, pole, a wire basket and the bracket the screen sits on.
    part = "monitor_stand"
    _spokes("Mon", part, 0.2, m["dark"], m)
    tube("MonPole", [P(0, 4 * PX, 0), P(0, 52 * PX, 0)], 0.024, chrome, part=part)
    cyl("MonCollar", P(0, 20 * PX, 0), 0.036, 0.05, dark, verts=10, part=part)
    cube("MonBasket", P(0, 30 * PX, 0.08), S(0.26, 4.5 * PX, 0.16), grey, part=part, bevel=0.015)
    cube("MonBasketIn", P(0, 31.2 * PX, 0.08), S(0.22, 2.4 * PX, 0.12), dark, part=part, smooth=False)
    tube("MonCable", [P(0.05, 32 * PX, 0.08), P(0.12, 36 * PX, 0.1), P(0.1, 44 * PX, 0.04), P(0.04, 48 * PX, 0.02)], 0.008, m["bezel"], sides=0, part=part)
    cube("MonBracket", P(0, 50 * PX, 0), S(0.3, 4 * PX, 0.12), dark, part=part, bevel=0.015)
    tube("MonHandle", [P(-0.12, 46 * PX, -0.04), P(-0.12, 46 * PX, -0.1), P(0.12, 46 * PX, -0.1), P(0.12, 46 * PX, -0.04)], 0.011, chrome, sides=0, part=part)

    # ---- drip stand (1.8 m): base, pole, four hooks, a pump; the bag is its own part so it can be see-through.
    part, h = "drip_stand", 1.8 * M
    _spokes("Drip", part, 0.22, chrome, m)
    tube("DripPole", [P(0, 4 * PX, 0), P(0, h - 0.02, 0)], 0.017, chrome, part=part)
    cyl("DripCollar", P(0, h * 0.55, 0), 0.026, 0.05, dark, verts=8, part=part)
    for i in range(4):
        a = i * math.pi / 2
        x, z = math.cos(a), math.sin(a)
        tube(f"DripHook{i}", [P(0, h - 0.03, 0), P(x * 0.1, h - 0.01, z * 0.1), P(x * 0.14, h - 0.03, z * 0.14), P(x * 0.13, h - 0.06, z * 0.13)], 0.008, chrome, sides=0, part=part)
    cube("DripPump", P(0, h * 0.5, 0.06), S(0.16, 7 * PX, 0.1), m["white"], part=part, bevel=0.015)
    cube("DripPumpScreen", P(0, h * 0.5 + 1 * PX, 0.112), S(0.1, 2.4 * PX, 0.006), m["screen"], part=part, smooth=False)
    cube("DripLabel", P(0.13, h - 7 * PX, 0.028), S(0.08, 2.6 * PX, 0.006), m["label"], part=part, smooth=False)
    tube("DripLine", [P(0.13, h - 12.5 * PX, 0), P(0.13, h - 16 * PX, 0.01), P(0.05, h * 0.56, 0.08), P(0.04, h * 0.42, 0.1), P(0.1, h * 0.3, 0.12)], 0.006, m["line"], sides=0, part=part)
    cyl("DripChamber", P(0.13, h - 13.5 * PX, 0), 0.014, 2 * PX, m["line"], verts=8, part=part)
    bag = cube("DripBag", P(0.13, h - 7.5 * PX, 0), S(0.12, 9.5 * PX, 0.045), tint, part="drip_bag", bevel=0.018, bevel_segments=3)
    del bag

    # ---- resus trolley (0.6 by 0.7 m, 1 m tall): red drawers, the defibrillator on top, the push handle behind.
    part, w, d, h = "resus_trolley", 1.2, 1.4, 1.0 * M
    for sx in (-1, 1):
        for sz in (-1, 1):
            _castor(f"Resus{sx}{sz}", part, sx * (w / 2 - 0.09), sz * (d / 2 - 0.09), m)
    body0 = 4.4 * PX
    cube("ResusBody", P(0, (body0 + h) / 2, 0), S(w, h - body0, d), tint, part=part, bevel=0.03, bevel_segments=3)
    cube("ResusTop", P(0, h - 0.4 * PX, 0), S(w + 0.03, 1 * PX, d + 0.03), m["tint_light"], part=part, bevel=0.012)
    drawer = (h - body0 - 1.5 * PX) / 4
    for i in range(4):
        y = body0 + 0.6 * PX + drawer * (i + 0.5)
        cube(f"ResusDrawer{i}", P(0, y, d / 2 + 0.004), S(w - 0.1, drawer - 0.7 * PX, 0.02), m["tint_light"] if i == 3 else tint, part=part, bevel=0.008)
        cube(f"ResusGap{i}", P(0, y - drawer / 2, d / 2 + 0.002), S(w - 0.08, 0.5 * PX, 0.012), m["tint_dark"], part=part, smooth=False)
        tube(f"ResusPull{i}", [P(-0.12, y + 0.3 * PX, d / 2 + 0.014), P(-0.1, y + 0.3 * PX, d / 2 + 0.04), P(0.1, y + 0.3 * PX, d / 2 + 0.04), P(0.12, y + 0.3 * PX, d / 2 + 0.014)], 0.008, chrome, sides=0, part=part)
    cube("ResusSeal", P(w / 2 - 0.12, body0 + 0.6 * PX + drawer * 3.5, d / 2 + 0.018), S(0.07, 1.6 * PX, 0.006), m["yellow"], part=part, smooth=False)
    cube("Defib", P(0, h + 2.6 * PX, -0.06), S(w * 0.7, 5 * PX, d * 0.58), dark, part=part, bevel=0.03, bevel_segments=3)
    tube("DefibHandle", [P(-0.14, h + 5 * PX, -0.06), P(-0.12, h + 6.6 * PX, -0.06), P(0.12, h + 6.6 * PX, -0.06), P(0.14, h + 5 * PX, -0.06)], 0.016, m["yellow"], part=part)
    _screen("Defib", part, (-0.12, h + 2.9 * PX, d * 0.23 - 0.03), w * 0.36, 3.6 * PX, m, picture="defib")
    for i, colour in enumerate(("green", "orange", "red")):
        cyl(f"DefibKey{i}", P(0.14 + i * 0.09, h + 2.9 * PX, d * 0.23 - 0.004), 0.028, 0.012, m[colour], rot=(math.pi / 2, 0, 0), verts=10, part=part)
    for sx in (-1, 1):
        cube(f"DefibPaddle{sx}", P(sx * (w * 0.35 + 0.05), h + 2.2 * PX, -0.1), S(0.07, 3.4 * PX, 0.2), m["bezel"], part=part, bevel=0.015)
    x = w / 2 - 0.06
    tube("ResusHandle", [P(-x, h - 6 * PX, -d / 2 - 0.01), P(-x, h + 3 * PX, -d / 2 - 0.09), P(-x + 0.04, h + 4 * PX, -d / 2 - 0.1), P(x - 0.04, h + 4 * PX, -d / 2 - 0.1), P(x, h + 3 * PX, -d / 2 - 0.09), P(x, h - 6 * PX, -d / 2 - 0.01)], 0.02, chrome, part=part)
    cyl("ResusOxygen", P(-w / 2 - 0.07, h * 0.5, -0.2), 0.06, h * 0.62, m["white"], verts=10, part=part)
    sphere("ResusOxygenTop", P(-w / 2 - 0.07, h * 0.81, -0.2), 0.06, m["white"], seg=10, rings=6, part=part)
    cyl("ResusOxygenValve", P(-w / 2 - 0.07, h * 0.9, -0.2), 0.025, 2.4 * PX, m["grey"], verts=8, part=part)
    cube("ResusSharps", P(w / 2 + 0.07, h * 0.72, 0.25), S(0.12, 6 * PX, 0.2), m["yellow"], part=part, bevel=0.02)
    cube("ResusSharpsLid", P(w / 2 + 0.07, h * 0.72 + 3.2 * PX, 0.25), S(0.125, 1 * PX, 0.205), m["red"], part=part, bevel=0.008)

    # ---- X-ray tube stand (2.1 m): a column on a plinth, an arm that reaches over the table, the tube and collimator.
    part, h = "xray_tube", 2.1 * M
    arm = h * 0.7
    cube("XrPlinth", P(0, 1.5 * PX, -0.15), S(0.5, 3 * PX, 0.32), m["plinth"], part=part, bevel=0.02)
    cube("XrColumn", P(0, (3 * PX + h) / 2, -0.18), S(0.17, h - 3 * PX, 0.15), m["tint_dark"], part=part, bevel=0.025)
    cube("XrTrack", P(0, (3 * PX + h) / 2, -0.102), S(0.05, h - 8 * PX, 0.01), dark, part=part, smooth=False)
    cube("XrCarriage", P(0, arm + 1.5 * PX, -0.15), S(0.24, 7 * PX, 0.22), tint, part=part, bevel=0.03, bevel_segments=3)
    cube("XrArm", P(0, arm + 1.5 * PX, 0.14), S(0.12, 3 * PX, 0.52), tint, part=part, bevel=0.025)
    cube("XrYoke", P(0, arm + 1.2 * PX, 0.45), S(0.5, 1.6 * PX, 0.1), m["tint_dark"], part=part, bevel=0.015)
    cyl("XrTube", P(0, arm + 1 * PX, 0.45), 0.105, 0.4, m["white"], rot=(0, math.pi / 2, 0), verts=16, part=part, bevel=0.02)
    for sx in (-1, 1):
        cyl(f"XrCap{sx}", P(sx * 0.21, arm + 1 * PX, 0.45), 0.08, 0.03, grey, rot=(0, math.pi / 2, 0), verts=12, part=part)
    cube("XrCollimator", P(0, arm - 4.4 * PX, 0.45), S(0.24, 5 * PX, 0.24), dark, part=part, bevel=0.02)
    cyl("XrLens", P(0, arm - 7 * PX, 0.45), 0.07, 0.012, mat("Lens", "#FFF59D", 0.3), verts=12, part=part)
    for sx in (-1, 1):
        tube(f"XrGrip{sx}", [P(sx * 0.12, arm - 3 * PX, 0.45), P(sx * 0.2, arm - 3 * PX, 0.5), P(sx * 0.2, arm - 5.5 * PX, 0.5), P(sx * 0.12, arm - 5.5 * PX, 0.45)], 0.01, chrome, sides=0, part=part)
    tube("XrCable", [P(0, h - 2 * PX, -0.18), P(0, h + 1 * PX, -0.05), P(0, arm + 6 * PX, 0.2), P(0, arm + 3.6 * PX, 0.4)], 0.012, m["bezel"], sides=0, part=part)

    # ---- X-ray table: a white pedestal with a film tray, a floating top with a dark pad and side rails.
    part = "xray_table"
    cube("XtSkirt", P(0, 1.5 * PX, 0), S(1.34, 3 * PX, 3.22), dark, part=part, bevel=0.015)
    cube("XtBase", P(0, 9.75 * PX, 0), S(1.3, 13.5 * PX, 3.18), m["white"], part=part, bevel=0.04, bevel_segments=3)
    for sx in (-1, 1):
        cube(f"XtTray{sx}", P(sx * 0.652, 11 * PX, 0), S(0.02, 4.4 * PX, 1.6), dark, part=part, smooth=False)
        cube(f"XtTrayPull{sx}", P(sx * 0.665, 11 * PX, 0), S(0.02, 1 * PX, 0.4), chrome, part=part, smooth=False)
        cube(f"XtKeys{sx}", P(sx * 0.655, 11 * PX, -1.2), S(0.02, 3 * PX, 0.3), m["blue"], part=part, smooth=False)
        tube(f"XtRail{sx}", [P(sx * 0.8, 20.5 * PX, -1.7), P(sx * 0.84, 23.5 * PX, -1.6), P(sx * 0.84, 23.5 * PX, 1.6), P(sx * 0.8, 20.5 * PX, 1.7)], 0.02, chrome, part=part)
    cube("XtTop", P(0, 20.2 * PX, 0), S(1.6, 3.4 * PX, 4.1), m["white"], part=part, bevel=0.03, bevel_segments=3)
    cube("XtPad", P(0, 21.85 * PX, 0), S(1.24, 0.5 * PX, 3.76), m["pad"], part=part, bevel=0.006)

    # ---- CT gantry (2 m wide, 0.7 m deep, 1.9 m tall): a white ring with the bore open right through.
    part, w, d, h = "ct_gantry", 4.0, 1.4, 1.9 * M
    cy, bore = h * 0.55, min(w, h) * 0.24
    cube("CtPlinth", P(0, 1.5 * PX, 0), S(w * 0.9, 3 * PX, d), m["plinth"], part=part, bevel=0.015)
    _ring_block("CtShroud", part, w * 0.94, 2.5 * PX, h, d, cy, bore, tint)
    for side in (-1, 1):
        torus(f"CtLip{side}", P(0, cy, side * (d / 2 - 0.005)), bore * 1.04, 0.03, m["tint_dark"], rot=(math.pi / 2, 0, 0), seg=32, sides=6, part=part)
        torus(f"CtLed{side}", P(0, cy, side * (d / 2 + 0.008)), bore * 1.2, 0.018, m["cyan"], rot=(math.pi / 2, 0, 0), seg=32, sides=4, part=part)
    cube("CtPanel", P(w * 0.33, cy + h * 0.2, d / 2 + 0.008), S(0.24, 4.6 * PX, 0.03), mat("Panel", "#37474F", 0.5), part=part, bevel=0.008)
    cube("CtPanelKeys", P(w * 0.33, cy + h * 0.2, d / 2 + 0.026), S(0.16, 1.4 * PX, 0.004), m["cyan"], part=part, smooth=False)
    cube("CtBadge", P(-w * 0.33, cy + h * 0.22, d / 2 + 0.006), S(0.3, 1.4 * PX, 0.01), m["blue"], part=part, smooth=False)
    for side in (-1, 1):
        cube(f"CtVent{side}", P(side * w * 0.38, 9 * PX, d / 2 + 0.004), S(0.3, 6 * PX, 0.012), m["tint_dark"], part=part, smooth=False)

    # ---- CT table: the pedestal, and the top that slides into the scanner (its own part, so it can move).
    part = "ct_base"
    cube("CbSkirt", P(0, 1.5 * PX, 0.7), S(1.14, 3 * PX, 2.62), dark, part=part, bevel=0.015)
    cube("CbBody", P(0, 9.25 * PX, 0.7), S(1.12, 12.5 * PX, 2.6), tint, part=part, bevel=0.05, bevel_segments=3)
    cube("CbStripe", P(0, 7.6 * PX, 0.7), S(1.135, 1.3 * PX, 2.4), m["blue"], part=part, smooth=False)
    cube("CbRunner", P(0, 16 * PX, 0.7), S(1.0, 2 * PX, 2.5), m["tint_dark"], part=part, bevel=0.01)
    cube("CbPedal", P(0.62, 1.4 * PX, 1.4), S(0.14, 1.2 * PX, 0.24), m["yellow"], part=part, bevel=0.01)
    part = "ct_top"
    cube("CtopSlab", P(0, 19 * PX, 0), S(1.28, 3.8 * PX, 4.1), m["tint_light"], part=part, bevel=0.04, bevel_segments=3)
    cube("CtopPad", P(0, 20.9 * PX, 0), S(0.94, 0.5 * PX, 3.9), m["blue"], part=part, bevel=0.006)
    for sx in (-1, 1):
        cube(f"CtopHead{sx}", P(sx * 0.39, 22.4 * PX, -1.7), S(0.14, 3 * PX, 0.5), mat("HeadRest", "#56616B", 0.7), part=part, bevel=0.025)

    # ---- operating table: a heavy base, the column, and a padded top in four sections with an arm board.
    part = "operating_table"
    cube("OpBase", P(0, 1.75 * PX, 0.1), S(1.2, 3.5 * PX, 2.2), mat("OpBase", "#56616B", 0.5), part=part, bevel=0.035, bevel_segments=3)
    cube("OpColumn", P(0, 10.5 * PX, 0.05), S(0.7, 14 * PX, 1.05), mat("OpColumn", "#C3CCD3", 0.4), part=part, bevel=0.04, bevel_segments=3)
    cube("OpSleeve", P(0, 6 * PX, 0.05), S(0.78, 5 * PX, 1.14), m["steel"], part=part, bevel=0.03)
    cube("OpDeck", P(0, 18.1 * PX, 0), S(1.12, 1.8 * PX, 3.7), mat("OpDeck", "#AEB8C1", 0.4), part=part, bevel=0.012)
    for i, (z0, z1, half) in enumerate(((-2.05, -1.5, 0.45), (-1.42, -0.2, 0.62), (-0.12, 0.7, 0.62), (0.78, 2.05, 0.62))):
        cube(f"OpPad{i}", P(0, 21 * PX, (z0 + z1) / 2), S(2 * half, 4 * PX, z1 - z0), tint, part=part, bevel=0.035, bevel_segments=3)
    cube("OpArmBoard", P(1.095, 21.25 * PX, -1.0), S(0.95, 2.5 * PX, 0.38), tint, part=part, bevel=0.03, bevel_segments=3)
    for sx in (-1, 1):
        tube(f"OpRail{sx}", [P(sx * 0.66, 20 * PX, -1.4), P(sx * 0.66, 20 * PX, 2.05)], 0.022, chrome, part=part)
    cube("OpPedal", P(0.66, 1.4 * PX, 0.9), S(0.16, 1.4 * PX, 0.3), m["yellow"], part=part, bevel=0.01)

    # ---- theatre lights: a ceiling column, two jointed arms and two lamp heads; the lit faces are their own part.
    part = "surgical_lights"
    top, joint, lamp_up = 118 * PX, 96 * PX, 84 * PX
    cyl("SlRose", P(0, top - 1 * PX, 0), 0.14, 2 * PX, m["steel"], verts=14, part=part)
    tube("SlColumn", [P(0, top, 0), P(0, joint, 0)], 0.05, m["steel"], part=part)
    for i, (x, z) in enumerate(((-0.55, 0.2), (0.5, -0.15))):
        up = joint + (2 - i * 4) * PX
        cyl(f"SlJoint{i}", P(0, up, 0), 0.07, 3 * PX, m["grey"], verts=12, part=part)
        elbow = (x * 0.75, up + 1 * PX, z * 0.75 - (0.25 if i == 0 else -0.25))
        tube(f"SlArm{i}", [P(0, up, 0), P(*elbow), P(x, lamp_up + 9 * PX, z), P(x, lamp_up + 5 * PX, z)], 0.032, m["steel"], part=part)
        sphere(f"SlElbow{i}", P(*elbow), 0.05, m["grey"], seg=10, rings=6, part=part)
        sphere(f"SlHead{i}", P(x, lamp_up + 1.2 * PX, z), 0.33, m["lamp"], scale=(1, 1, 5.5 * PX / 0.33), seg=20, rings=8, part=part)
        torus(f"SlRim{i}", P(x, lamp_up + 0.6 * PX, z), 0.31, 0.02, m["grey"], seg=24, sides=4, part=part)
        tube(f"SlHandle{i}", [P(x, lamp_up, z), P(x, lamp_up - 3 * PX, z)], 0.02, m["white"], sides=0, part=part)
        cyl(f"SlGlow{i}", P(x, lamp_up - 0.1 * PX, z), 0.26, 0.012, m["glow"], verts=20, part="surgical_glow")
