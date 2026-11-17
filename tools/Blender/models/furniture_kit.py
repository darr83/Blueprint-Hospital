"""Furniture kit: chairs, bedside locker, clinical cupboard, office desk and staff table,
made in the game's own units like equipment_kit (a tile across, px/39.19 up), standing on the floor
at the origin with the front towards the viewer (game +z, Blender -Y).

Material names say what each surface is made of ("Tint_fabric", "TintDark_wood", "Chrome"...), so the
game's surface detail can pick wood grain, fabric weave or brushed metal for it."""
import math

from bhkit import cube, cyl, lathe, mat, sphere, tube

BUDGET = 30000

PX = 1 / 39.191836
M = 28 * PX


def P(x, up, z):
    return (x, -z, up)


def S(w, tall, d):
    return (w, d, tall)


def _mats():
    return {
        "fabric": mat("Tint_fabric", "#FFFFFF", 0.95),
        "fabric_dark": mat("TintDark_fabric", "#CCCCCC", 0.95),
        "lam": mat("Tint_laminate", "#FFFFFF", 0.5),
        "lam_dark": mat("TintDark_laminate", "#CCCCCC", 0.5),
        "lam_light": mat("TintLight_laminate", "#FFFFFF", 0.5),
        "wood": mat("TintLight_wood", "#FFFFFF", 0.55),
        "wood_dark": mat("TintDark_wood", "#CCCCCC", 0.55),
        "chrome": mat("Chrome", "#C3CCD3", 0.3, metal=0.3),
        "frame": mat("Frame_metal", "#5E6770", 0.45, metal=0.2),
        "black": mat("Black_plastic", "#2A2F34", 0.5),
        "rubber": mat("Rubber", "#22272B", 0.8),
        "white": mat("White_plastic", "#EEF2F5", 0.45),
        "screen": mat("Screen", "#24425E", 0.25),
        "glow": mat("ScreenGlow", "#4FC3F7", 0.3),
        "paper": mat("Paper", "#F4F5F0", 0.9),
        "jug": mat("Jug_plastic", "#B3E5FC", 0.2),
        "mug": mat("Mug_ceramic", "#E8604C", 0.3),
        "mug2": mat("Mug2_ceramic", "#F2F2EE", 0.3),
        "plinth": mat("Plinth_rubber", "#4A545D", 0.7),
        "ceramic": mat("Basin_ceramic", "#F4F7F9", 0.2),
        "steel": mat("Sink_steel", "#C3CCD3", 0.3, metal=0.35),
        "worktop": mat("Worktop_laminate", "#6D7F8C", 0.45),
        "glass": mat("Tint_glass", "#FFFFFF", 0.1),
        "drape": mat("Drape_fabric", "#3F7FBF", 0.9),
        "green": mat("Valve_plastic", "#2E7D32", 0.5),
        "led": mat("Led", "#3EE07A", 0.3),
        "kettle": mat("Kettle_plastic", "#ECEFF1", 0.35),
        "box_red": mat("BoxRed", "#E57373", 0.8),
        "box_blue": mat("BoxBlue", "#64B5F6", 0.8),
        "box_yellow": mat("BoxYellow", "#FFF176", 0.8),
        "box_green": mat("BoxGreen", "#81C784", 0.8),
        "box_purple": mat("BoxPurple", "#BA68C8", 0.8),
        "box_orange": mat("BoxOrange", "#FFB74D", 0.8),
        "box_white": mat("BoxWhite", "#F5F5F5", 0.8),
    }


def _legs4(name, part, w, d, h, inset, r, material):
    for i, (x, z) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        tube(f"{name}Leg{i}", [P(x * (w / 2 - inset), 0.01, z * (d / 2 - inset)), P(x * (w / 2 - inset), h, z * (d / 2 - inset))], r, material, sides=1, part=part)
        cyl(f"{name}Foot{i}", P(x * (w / 2 - inset), 0.008, z * (d / 2 - inset)), r * 1.25, 0.016, _mats()["rubber"], verts=10, part=part)


# ------------------------------------------------------------------ chairs (front +z)

def visitor_chair(m, part="visitor_chair", arms=True):
    seat = 14 * PX
    w, d = 0.84, 0.72
    # Sled frame: two loops of tube down each side, joined at the back.
    for s in (-1, 1):
        x = s * (w / 2 - 0.05)
        tube(f"{part}Sled{s}", [P(x, 0.02, d / 2 - 0.06), P(x, 0.02, -d / 2 + 0.06), P(x, seat - 2 * PX, -d / 2 + 0.08), P(x, seat - 2 * PX, d / 2 - 0.08), P(x, 0.02, d / 2 - 0.06)], 0.022, m["chrome"], sides=1, part=part)
        if arms:
            tube(f"{part}ArmPost{s}", [P(x + s * 0.02, seat - 2 * PX, d / 2 - 0.1), P(x + s * 0.02, seat + 8 * PX, d / 2 - 0.1), P(x + s * 0.02, seat + 8 * PX, -d / 2 + 0.1)], 0.02, m["chrome"], sides=1, part=part)
            cube(f"{part}ArmPad{s}", P(x + s * 0.02, seat + 8.6 * PX, 0.0), S(0.07, 1.4 * PX, d * 0.7), m["black"], part=part, bevel=0.02)
    # Moulded seat and back, upholstered, rounded.
    cube(f"{part}Seat", P(0, seat - 1.2 * PX, 0.02), S(w - 0.08, 3 * PX, d - 0.06), m["fabric"], part=part, bevel=0.05, bevel_segments=3)
    cube(f"{part}SeatShell", P(0, seat - 3 * PX, 0.02), S(w - 0.14, 1 * PX, d - 0.14), m["black"], part=part, bevel=0.02)
    back = cube(f"{part}Back", P(0, seat + 10 * PX, -d / 2 + 0.06), S(w - 0.1, 15 * PX, 0.08), m["fabric"], part=part, bevel=0.04, bevel_segments=3)
    back.rotation_euler = (-0.12, 0, 0)
    tube(f"{part}BackPost", [P(0, seat - 2 * PX, -d / 2 + 0.08), P(0, seat + 4 * PX, -d / 2 + 0.03)], 0.02, m["chrome"], part=part)


def lounge_chair(m):
    """A staff-room armchair: upholstered box arms, loose seat and back cushions, short wooden legs."""
    part = "lounge_chair"
    seat = 14 * PX
    w, d = 1.0, 0.74
    oak = mat("Oak_wood", "#B98A55", 0.55)
    for i, (x, z) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        tube(f"{part}Leg{i}", [P(x * (w / 2 - 0.08), 0.0, z * (d / 2 - 0.07)), P(x * (w / 2 - 0.07), 0.09, z * (d / 2 - 0.06))], 0.028, oak, part=part)
    cube(f"{part}Base", P(0, 0.09 + (seat - 4.5 * PX - 0.09) / 2, 0), S(w, seat - 4.5 * PX - 0.09, d), m["fabric_dark"], part=part, bevel=0.03, bevel_segments=2)
    cube(f"{part}Cushion", P(0, seat - 2.2 * PX, 0.03), S(w - 0.26, 4.6 * PX, d - 0.12), m["fabric"], part=part, bevel=0.06, bevel_segments=4)
    for s in (-1, 1):
        cube(f"{part}Arm{s}", P(s * (w / 2 - 0.065), seat + 1.5 * PX, 0), S(0.13, seat + 3 * PX - 0.09, d), m["fabric_dark"], part=part, bevel=0.05, bevel_segments=3)
    back = cube(f"{part}Back", P(0, seat + 9 * PX, -d / 2 + 0.07), S(w, 24 * PX, 0.14), m["fabric_dark"], part=part, bevel=0.05, bevel_segments=3)
    back.rotation_euler = (-0.06, 0, 0)
    cush = cube(f"{part}BackCushion", P(0, seat + 9 * PX, -d / 2 + 0.17), S(w - 0.28, 17 * PX, 0.1), m["fabric"], part=part, bevel=0.05, bevel_segments=4)
    cush.rotation_euler = (-0.12, 0, 0)


def waiting_seat(m):
    """A beam seat: moulded plastic shell on a steel beam with a pedestal, upholstered pad."""
    part = "waiting_seat"
    seat = 14 * PX
    w, d = 0.84, 0.72
    cube(f"{part}Beam", P(0, seat - 4.5 * PX, -0.05), S(w + 0.02, 1.6 * PX, 0.1), m["frame"], part=part, bevel=0.015)
    tube(f"{part}Post", [P(0, 0.02, -0.05), P(0, seat - 4 * PX, -0.05)], 0.035, m["frame"], part=part)
    cube(f"{part}Foot", P(0, 0.015, -0.05), S(0.1, 0.03, d - 0.1), m["frame"], part=part, bevel=0.01)
    cube(f"{part}Shell", P(0, seat - 2.4 * PX, 0.02), S(w - 0.06, 1.2 * PX, d - 0.06), m["lam_dark"], part=part, bevel=0.03, bevel_segments=3)
    cube(f"{part}Pad", P(0, seat - 0.8 * PX, 0.04), S(w - 0.14, 2 * PX, d - 0.16), m["fabric"], part=part, bevel=0.04, bevel_segments=3)
    back = cube(f"{part}BackShell", P(0, seat + 10 * PX, -d / 2 + 0.05), S(w - 0.06, 18 * PX, 0.05), m["lam_dark"], part=part, bevel=0.025, bevel_segments=3)
    back.rotation_euler = (-0.12, 0, 0)
    pad = cube(f"{part}BackPad", P(0, seat + 10.5 * PX, -d / 2 + 0.1), S(w - 0.16, 13 * PX, 0.04), m["fabric"], part=part, bevel=0.03, bevel_segments=3)
    pad.rotation_euler = (-0.12, 0, 0)


def staff_chair(m):
    """An office chair: five-star base on castors, gas lift, upholstered seat and mesh back."""
    part = "staff_chair"
    seat = 14 * PX
    for i in range(5):
        a = i * math.tau / 5
        x, z = math.sin(a) * 0.3, math.cos(a) * 0.3
        tube(f"{part}Spoke{i}", [P(0, 4 * PX, 0), P(x, 2.6 * PX, z)], 0.025, m["black"], part=part)
        cyl(f"{part}Wheel{i}", P(x, 0.05, z), 0.05, 0.05, m["rubber"], rot=(0, math.pi / 2, a), verts=12, part=part)
    cyl(f"{part}Lift", P(0, (4 * PX + seat) / 2, 0), 0.03, seat - 4 * PX, m["chrome"], verts=12, part=part)
    cyl(f"{part}Shroud", P(0, 7 * PX, 0), 0.045, 4 * PX, m["black"], verts=12, part=part)
    cube(f"{part}Seat", P(0, seat - 0.5 * PX, 0.02), S(0.78, 3 * PX, 0.72), m["fabric"], part=part, bevel=0.06, bevel_segments=3)
    tube(f"{part}Spine", [P(0, seat - 2 * PX, -0.2), P(0, seat - 1 * PX, -0.36), P(0, seat + 6 * PX, -0.38)], 0.025, m["black"], part=part)
    back = cube(f"{part}Back", P(0, seat + 12 * PX, -0.36), S(0.72, 15 * PX, 0.07), m["fabric_dark"], part=part, bevel=0.05, bevel_segments=3)
    back.rotation_euler = (-0.08, 0, 0)
    for s in (-1, 1):
        tube(f"{part}Arm{s}", [P(s * 0.38, seat - 1 * PX, -0.05), P(s * 0.42, seat + 6 * PX, -0.05), P(s * 0.42, seat + 6 * PX, 0.2)], 0.02, m["black"], part=part)


# ------------------------------------------------------------------ storage

def bedside_locker(m):
    """Ward bedside locker: drawer over a cupboard, raised-lip top, recessed plinth, jug and cup."""
    part = "bedside_locker"
    w, d, h = 0.9, 0.9, 0.8 * M
    cube(f"{part}Plinth", P(0, 0.04, 0.02), S(w - 0.08, 0.08, d - 0.1), m["plinth"], part=part)
    cube(f"{part}Body", P(0, 0.08 + (h - 0.12) / 2, 0), S(w, h - 0.12, d), m["lam"], part=part, bevel=0.02)
    cube(f"{part}Top", P(0, h - 0.02, 0), S(w + 0.03, 0.04, d + 0.03), m["lam_light"], part=part, bevel=0.012)
    tube(f"{part}Lip", [P(-w / 2, h + 0.01, d / 2 - 0.02), P(-w / 2, h + 0.01, -d / 2), P(w / 2, h + 0.01, -d / 2), P(w / 2, h + 0.01, d / 2 - 0.02)], 0.012, m["lam_light"], part=part)
    # Drawer front and door, each a slightly proud panel with a dark recessed pull.
    dh = h * 0.24
    cube(f"{part}Drawer", P(0, h - 0.06 - dh / 2, d / 2 + 0.006), S(w - 0.06, dh - 0.02, 0.014), m["lam_light"], part=part, bevel=0.006)
    cube(f"{part}DrawerPull", P(0, h - 0.07 - 0.02, d / 2 + 0.014), S(w * 0.4, 0.03, 0.01), m["black"], part=part, bevel=0.004)
    door_h = h - 0.12 - dh - 0.04
    cube(f"{part}Door", P(0, 0.1 + door_h / 2, d / 2 + 0.006), S(w - 0.06, door_h, 0.014), m["lam_light"], part=part, bevel=0.006)
    cube(f"{part}DoorPull", P(w / 2 - 0.1, 0.1 + door_h * 0.75, d / 2 + 0.014), S(0.025, door_h * 0.35, 0.01), m["black"], part=part, bevel=0.004)
    # Water jug with handle, and a cup.
    cyl(f"{part}Jug", P(-w * 0.18, h + 0.12, -0.05), 0.07, 0.22, m["jug"], verts=16, part=part)
    cyl(f"{part}JugLid", P(-w * 0.18, h + 0.24, -0.05), 0.072, 0.03, m["white"], verts=16, part=part)
    tube(f"{part}JugHandle", [P(-w * 0.18 + 0.06, h + 0.18, -0.05), P(-w * 0.18 + 0.12, h + 0.16, -0.05), P(-w * 0.18 + 0.12, h + 0.06, -0.05), P(-w * 0.18 + 0.06, h + 0.05, -0.05)], 0.012, m["white"], part=part)
    cyl(f"{part}Cup", P(w * 0.2, h + 0.055, 0.08), 0.04, 0.09, m["mug2"], verts=14, part=part)


def clinical_cupboard(m):
    """Two-door clinical cupboard: plinth, body, flush doors with long bar handles, a top."""
    part = "clinical_cupboard"
    w, d, h = 2.0, 1.0, 1.1 * M
    cube(f"{part}Plinth", P(0, 0.05, 0.02), S(w - 0.06, 0.1, d - 0.1), m["plinth"], part=part)
    cube(f"{part}Body", P(0, 0.1 + (h - 0.14) / 2, 0), S(w, h - 0.14, d), m["lam"], part=part, bevel=0.015)
    cube(f"{part}Top", P(0, h - 0.02, 0.005), S(w + 0.03, 0.04, d + 0.03), m["lam_light"], part=part, bevel=0.012)
    door_h = h - 0.2
    for s in (-1, 1):
        cube(f"{part}Door{s}", P(s * (w / 4), 0.12 + door_h / 2, d / 2 + 0.006), S(w / 2 - 0.03, door_h, 0.014), m["lam_light"], part=part, bevel=0.006)
        x = s * 0.07
        tube(f"{part}Handle{s}", [P(x, 0.12 + door_h * 0.45, d / 2 + 0.014), P(x, 0.12 + door_h * 0.45, d / 2 + 0.05), P(x, 0.12 + door_h * 0.85, d / 2 + 0.05), P(x, 0.12 + door_h * 0.85, d / 2 + 0.014)], 0.012, m["chrome"], part=part)
    # A label holder and a lock.
    cube(f"{part}Label", P(-w / 4, 0.12 + door_h * 0.9, d / 2 + 0.016), S(0.3, 0.06, 0.006), m["white"], part=part)
    cyl(f"{part}Lock", P(0.03, 0.12 + door_h * 0.65, d / 2 + 0.016), 0.015, 0.01, m["chrome"], rot=(math.pi / 2, 0, 0), verts=10, part=part)


# ------------------------------------------------------------------ desks and tables

def office_desk(m):
    """Wood-topped desk on a steel frame, drawer pedestal, monitor (facing the staff side, -z), keyboard, mug."""
    part = "office_desk"
    w, d, h = 2.0, 1.6, 0.75 * M
    top = 0.05
    cube(f"{part}Top", P(0, h - top / 2, 0), S(w, top, d), m["wood"], part=part, bevel=0.018, bevel_segments=2)
    for s in (-1, 1):
        x = s * (w / 2 - 0.08)
        tube(f"{part}Frame{s}", [P(x, 0.01, d / 2 - 0.1), P(x, h - top, d / 2 - 0.1), P(x, h - top, -d / 2 + 0.1), P(x, 0.01, -d / 2 + 0.1)], 0.025, m["frame"], part=part)
        cube(f"{part}Glide{s}a", P(x, 0.01, d / 2 - 0.1), S(0.06, 0.02, 0.06), m["rubber"], part=part)
        cube(f"{part}Glide{s}b", P(x, 0.01, -d / 2 + 0.1), S(0.06, 0.02, 0.06), m["rubber"], part=part)
    cube(f"{part}Modesty", P(0, h * 0.6, d / 2 - 0.12), S(w - 0.3, h * 0.55, 0.025), m["lam_dark"], part=part, bevel=0.006)
    ped_w = 0.6
    px = w / 2 - ped_w / 2 - 0.15
    cube(f"{part}Pedestal", P(px, (h - top - 0.04) / 2 + 0.02, -0.05), S(ped_w, h - top - 0.04, d - 0.4), m["lam"], part=part, bevel=0.012)
    for i in range(3):
        y = 0.06 + (i + 0.5) * (h - top - 0.1) / 3
        cube(f"{part}Drawer{i}", P(px, y, -(d - 0.4) / 2 - 0.055), S(ped_w - 0.05, (h - top - 0.1) / 3 - 0.02, 0.012), m["lam_light"], part=part, bevel=0.005)
        cube(f"{part}Pull{i}", P(px, y + 0.03, -(d - 0.4) / 2 - 0.066), S(0.18, 0.018, 0.012), m["chrome"], part=part, bevel=0.004)
    # Monitor on a stand facing the staff side (-z), keyboard and mouse in front of it.
    sx, sz = -0.15, 0.1
    cube(f"{part}MonBase", P(sx, h + 0.01, sz), S(0.25, 0.02, 0.18), m["black"], part=part, bevel=0.008)
    tube(f"{part}MonNeck", [P(sx, h + 0.02, sz + 0.03), P(sx, h + 0.22, sz + 0.03)], 0.02, m["black"], part=part)
    cube(f"{part}Monitor", P(sx, h + 0.3, sz), S(0.62, 0.36, 0.035), m["black"], part=part, bevel=0.01)
    cube(f"{part}Picture", P(sx, h + 0.3, sz - 0.019), S(0.57, 0.31, 0.004), m["screen"], part=part, smooth=False)
    cube(f"{part}Bar1", P(sx, h + 0.36, sz - 0.022), S(0.45, 0.025, 0.003), m["glow"], part=part, smooth=False)
    cube(f"{part}Keyboard", P(sx, h + 0.012, -0.3), S(0.48, 0.022, 0.15), m["black"], part=part, bevel=0.006)
    cube(f"{part}Keys", P(sx, h + 0.024, -0.3), S(0.44, 0.004, 0.12), m["frame"], part=part, smooth=False)
    cube(f"{part}Mouse", P(sx + 0.36, h + 0.015, -0.3), S(0.06, 0.03, 0.1), m["black"], part=part, bevel=0.012)
    cyl(f"{part}Mug", P(0.55, h + 0.05, -0.15), 0.045, 0.1, m["mug"], verts=14, part=part)
    cube(f"{part}Papers", P(0.45, h + 0.005, 0.25), S(0.3, 0.008, 0.4), m["paper"], part=part)


def staff_table(m):
    """Staff room table: rounded wood top on four tapered legs, mugs and a newspaper."""
    part = "staff_table"
    w, d, h = 2.0, 1.6, 0.7 * M
    cube(f"{part}Top", P(0, h - 0.025, 0), S(w, 0.05, d), m["wood"], part=part, bevel=0.06, bevel_segments=3)
    cube(f"{part}Apron", P(0, h - 0.08, 0), S(w - 0.25, 0.07, d - 0.25), m["wood_dark"], part=part, bevel=0.008)
    for i, (x, z) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        lx, lz = x * (w / 2 - 0.15), z * (d / 2 - 0.15)
        tube(f"{part}Leg{i}", [P(lx, 0.01, lz), P(lx * 0.98, h - 0.05, lz * 0.98)], 0.035, m["wood_dark"], part=part)
    cyl(f"{part}Mug1", P(-w * 0.2, h + 0.05, d * 0.1), 0.045, 0.1, m["mug"], verts=14, part=part)
    cyl(f"{part}Mug2", P(w * 0.25, h + 0.05, -d * 0.15), 0.045, 0.1, m["mug2"], verts=14, part=part)
    paper = cube(f"{part}Paper", P(w * 0.05, h + 0.006, d * 0.1), S(0.36, 0.01, 0.26), m["paper"], part=part)
    paper.rotation_euler = (0, 0, 0.3)


# ------------------------------------------------------------------ sinks, kitchen and clinical storage

def _base_unit(m, part, w, d, h, doors):
    """A floor unit: recessed plinth, carcass, flush doors with D-handles."""
    cube(f"{part}Plinth", P(0, 0.05, 0.03), S(w - 0.06, 0.1, d - 0.12), m["plinth"], part=part)
    cube(f"{part}Body", P(0, 0.1 + (h - 0.1) / 2, 0), S(w, h - 0.1, d), m["lam"], part=part, bevel=0.012)
    door_h = h - 0.18
    for i in range(doors):
        x = -w / 2 + (i + 0.5) * w / doors
        cube(f"{part}Door{i}", P(x, 0.12 + door_h / 2, d / 2 + 0.006), S(w / doors - 0.03, door_h, 0.014), m["lam_light"], part=part, bevel=0.006)
        hx = x + (0.5 if i % 2 == 0 else -0.5) * (w / doors - 0.16)
        tube(f"{part}Pull{i}", [P(hx, 0.12 + door_h * 0.62, d / 2 + 0.014), P(hx, 0.12 + door_h * 0.62, d / 2 + 0.04), P(hx, 0.12 + door_h * 0.88, d / 2 + 0.04), P(hx, 0.12 + door_h * 0.88, d / 2 + 0.014)], 0.01, m["chrome"], part=part)


def _lever_tap(m, part, x, top, z, reach=0.2):
    """A hospital elbow-lever mixer tap: a swan neck from the back of the basin with a long lever."""
    tube(f"{part}Neck", [P(x, top, z), P(x, top + 0.2, z), P(x, top + 0.24, z + 0.05), P(x, top + 0.22, z + reach)], 0.022, m["chrome"], sides=2, part=part)
    cyl(f"{part}TapBase", P(x, top + 0.01, z), 0.035, 0.02, m["chrome"], verts=12, part=part)
    tube(f"{part}Lever", [P(x + 0.03, top + 0.08, z), P(x + 0.22, top + 0.08, z + 0.02)], 0.012, m["chrome"], part=part)


def hand_basin(m, part="hand_basin", deep=False):
    """Clinical hand basin on a cupboard: ceramic top with a bowl, elbow-lever tap, splashback upstand."""
    w, d, h = (1.8, 1.2, 0.9 * M) if deep else (1.2, 0.9, 0.9 * M)
    _base_unit(m, part, w, d, h - 0.05, 2)
    cube(f"{part}Top", P(0, h - 0.03, 0), S(w + 0.03, 0.06, d + 0.03), m["ceramic"], part=part, bevel=0.015)
    bw, bd = w * 0.62, d * 0.58
    depth = 0.14 if deep else 0.07
    lathe(f"{part}Bowl", [(0.0, -depth), (0.82, -depth), (0.95, -depth * 0.6), (1.0, 0.0), (1.06, 0.004)], m["steel" if deep else "ceramic"], loc=P(0, h + 0.002, 0.04), sx=bw / 2, sy=bd / 2, seg=24, part=part)
    cyl(f"{part}Drain", P(0, h - depth + 0.004, 0.04), 0.03, 0.006, m["chrome"], verts=10, part=part)
    cube(f"{part}Upstand", P(0, h + 0.09, -d / 2 + 0.02), S(w + 0.03, 0.18, 0.04), m["ceramic"], part=part, bevel=0.01)
    _lever_tap(m, part, 0, h, -d / 2 + 0.08, reach=0.16 + (0.06 if deep else 0))


def plaster_sink(m):
    hand_basin(m, "plaster_sink", deep=True)


def kitchenette(m):
    """Staff kitchenette: base units, grey worktop, a steel sink and tap, a kettle and a microwave."""
    part = "kitchenette"
    w, d, h = 3.0, 1.2, 0.9 * M
    _base_unit(m, part, w, d, h - 0.06, 3)
    cube(f"{part}Worktop", P(0, h - 0.03, 0.01), S(w + 0.04, 0.06, d + 0.04), m["worktop"], part=part, bevel=0.012)
    lathe(f"{part}Sink", [(0.0, -0.08), (0.85, -0.08), (0.95, -0.05), (1.0, 0.0), (1.05, 0.004)], m["steel"], loc=P(-w * 0.25, h + 0.002, 0.04), sx=0.3, sy=0.26, seg=20, part=part)
    _lever_tap(m, part, -w * 0.25, h, -d / 2 + 0.1, reach=0.18)
    # Kettle: a rounded jug with a handle on its base.
    cyl(f"{part}KettleBase", P(w * 0.08, h + 0.012, 0.0), 0.085, 0.024, m["black"], verts=16, part=part)
    lathe(f"{part}Kettle", [(0.0, 0.0), (0.075, 0.0), (0.08, 0.06), (0.07, 0.17), (0.04, 0.2), (0.0, 0.2)], m["kettle"], loc=P(w * 0.08, h + 0.024, 0.0), seg=16, part=part)
    tube(f"{part}KettleHandle", [P(w * 0.08 + 0.07, h + 0.17, 0.0), P(w * 0.08 + 0.13, h + 0.15, 0.0), P(w * 0.08 + 0.11, h + 0.06, 0.0)], 0.014, m["kettle"], part=part)
    # Microwave.
    mx = w * 0.3
    cube(f"{part}Microwave", P(mx, h + 0.16, -0.05), S(0.55, 0.3, 0.42 * 2), m["white"], part=part, bevel=0.02)
    cube(f"{part}MicroDoor", P(mx - 0.06, h + 0.16, 0.38), S(0.36, 0.22, 0.01), m["black"], part=part, bevel=0.005)
    cube(f"{part}MicroPanel", P(mx + 0.2, h + 0.16, 0.375), S(0.1, 0.24, 0.01), m["frame"], part=part)


def drug_fridge(m):
    """Medicines fridge: white cabinet, wire shelves of boxes behind a glass door, a green temperature display."""
    part = "drug_fridge"
    w, d, h = 1.2, 1.2, 1.8 * M
    cube(f"{part}Plinth", P(0, 0.04, 0.02), S(w - 0.04, 0.08, d - 0.06), m["plinth"], part=part)
    cube(f"{part}Body", P(0, 0.08 + (h - 0.08) / 2, -0.03), S(w, h - 0.08, d - 0.06), m["white"], part=part, bevel=0.02)
    cube(f"{part}Inside", P(0, h / 2 + 0.02, d / 2 - 0.07), S(w - 0.14, h - 0.32, 0.02), m["jug"], part=part, smooth=False)
    boxes = ["box_red", "box_blue", "box_yellow", "box_green", "box_white"]
    for i in range(1, 4):
        y = 0.16 + (h - 0.32) * i / 4
        cube(f"{part}Shelf{i}", P(0, y, d / 2 - 0.12), S(w - 0.16, 0.012, 0.12), m["chrome"], part=part, smooth=False)
        for k in range(3):
            cube(f"{part}Box{i}{k}", P(-0.3 + k * 0.3, y + 0.06, d / 2 - 0.13), S(0.22, 0.11, 0.08), m[boxes[(i + k) % len(boxes)]], part=part, smooth=False)
    cube(f"{part}Frame", P(0, h / 2 + 0.02, d / 2 - 0.035), S(w - 0.02, h - 0.2, 0.03), m["white"], part=part, bevel=0.01, smooth=False)
    cube(f"{part}Display", P(w * 0.25, h - 0.06, d / 2 - 0.02), S(0.16, 0.05, 0.01), m["black"], part=part)
    cube(f"{part}Reading", P(w * 0.25, h - 0.06, d / 2 - 0.014), S(0.12, 0.03, 0.004), m["led"], part=part, smooth=False)
    tube(f"{part}Handle", [P(w / 2 - 0.08, h * 0.3, d / 2 - 0.02), P(w / 2 - 0.08, h * 0.3, d / 2 + 0.03), P(w / 2 - 0.08, h * 0.65, d / 2 + 0.03), P(w / 2 - 0.08, h * 0.65, d / 2 - 0.02)], 0.014, m["chrome"], part=part)
    cube(f"{part}Pane", P(0, h / 2 + 0.02, d / 2 - 0.02), S(w - 0.12, h - 0.3, 0.012), m["glass"], part="drug_fridge_glass", smooth=False)


def medicine_shelves(m):
    """Pharmacy shelving: steel uprights, five shelves with lipped fronts, rows of medicine boxes."""
    part = "medicine_shelves"
    w, d, h = 3.0, 0.8, 2.0 * M
    cube(f"{part}Back", P(0, h / 2, -d / 2 + 0.02), S(w, h, 0.03), m["lam"], part=part)
    for s in (-1, 1):
        cube(f"{part}Side{s}", P(s * (w / 2 - 0.025), h / 2, 0), S(0.05, h, d), m["lam_dark"], part=part, bevel=0.008)
    boxes = ["box_red", "box_blue", "box_yellow", "box_green", "box_purple", "box_orange", "box_white"]
    for i in range(6):
        y = min(h - 0.03, 0.06 + i * (h - 0.09) / 5)
        cube(f"{part}Shelf{i}", P(0, y, 0), S(w - 0.1, 0.025, d - 0.04), m["lam_light"], part=part, smooth=False)
        cube(f"{part}Lip{i}", P(0, y + 0.02, d / 2 - 0.03), S(w - 0.1, 0.03, 0.012), m["white"], part=part, smooth=False)
        if i == 5:
            continue
        x = -w / 2 + 0.1
        k = 0
        gap = (h - 0.09) / 5
        while x < w / 2 - 0.2:
            bw = 0.14 + 0.05 * ((i * 3 + k) % 3)
            tall = gap * (0.45 + 0.1 * ((i * 5 + k * 3) % 4))
            cube(f"{part}Box{i}_{k}", P(x + bw / 2, y + 0.0125 + tall / 2, 0.02), S(bw - 0.02, tall, d * 0.55), m[boxes[(i * 2 + k) % len(boxes)]], part=part, smooth=False)
            x += bw + 0.015
            k += 1


def instrument_trolley(m):
    """Stainless instrument trolley: two shelves on four posts, castors, top laid with a blue drape and instruments."""
    part = "instrument_trolley"
    w, d, h = 2.0, 1.0, 0.9 * M
    for i, (x, z) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        px, pz = x * (w / 2 - 0.06), z * (d / 2 - 0.06)
        tube(f"{part}Post{i}", [P(px, 0.1, pz), P(px, h, pz)], 0.02, m["steel"], part=part)
        cyl(f"{part}Fork{i}", P(px, 0.075, pz), 0.016, 0.05, m["chrome"], verts=8, part=part)
        cyl(f"{part}Wheel{i}", P(px, 0.045, pz + 0.015), 0.045, 0.035, m["rubber"], rot=(0, math.pi / 2, 0), verts=12, part=part)
    for name, y in (("Lower", 0.22), ("Upper", h - 0.015)):
        cube(f"{part}{name}", P(0, y, 0), S(w, 0.03, d), m["steel"], part=part, bevel=0.01)
        tube(f"{part}{name}Rim", [P(-w / 2, y + 0.025, d / 2), P(-w / 2, y + 0.025, -d / 2), P(w / 2, y + 0.025, -d / 2), P(w / 2, y + 0.025, d / 2), P(-w / 2, y + 0.025, d / 2)], 0.008, m["steel"], part=part)
    cube(f"{part}Drape", P(0, h + 0.006, 0), S(w - 0.08, 0.008, d - 0.08), m["drape"], part=part, smooth=False)
    for i in range(6):
        x = -w / 2 + 0.2 + i * (w - 0.4) / 5
        cube(f"{part}Tool{i}", P(x, h + 0.016, 0.03), S(0.02, 0.01, d * 0.5), m["chrome"], part=part, smooth=False)
        torus_free = cyl(f"{part}Ring{i}", P(x, h + 0.016, -d * 0.24), 0.025, 0.008, m["chrome"], verts=10, part=part)
    cube(f"{part}Kidney", P(w * 0.3, h + 0.03, -d * 0.12), S(0.3, 0.04, 0.18), m["steel"], part=part, bevel=0.04, bevel_segments=3)
    cube(f"{part}Gauze", P(-w * 0.3, h + 0.02, -d * 0.15), S(0.2, 0.025, 0.2), m["box_white"], part=part, bevel=0.01)


def oxygen_cylinder(m):
    """Oxygen cylinder in a wheeled stand: white body with a rounded shoulder, valve, green flowmeter."""
    part = "oxygen_cylinder"
    h = 1.2 * M
    cyl(f"{part}Stand", P(0, 0.03, 0), 0.2, 0.06, m["plinth"], verts=20, part=part)
    for i in range(3):
        a = i * math.tau / 3
        cyl(f"{part}Wheel{i}", P(math.sin(a) * 0.17, 0.03, math.cos(a) * 0.17), 0.03, 0.025, m["rubber"], rot=(0, math.pi / 2, a), verts=10, part=part)
    body = h - 0.32
    lathe(f"{part}Body", [(0.0, 0.0), (0.12, 0.0), (0.125, 0.02), (0.125, body - 0.1), (0.1, body - 0.03), (0.05, body), (0.0, body)], m["white"], loc=P(0, 0.06, 0), seg=20, part=part)
    tube(f"{part}Collar", [P(-0.13, 0.35, 0), P(0.13, 0.35, 0)], 0.012, m["plinth"], part=part)
    cyl(f"{part}Valve", P(0, 0.06 + body + 0.05, 0), 0.04, 0.1, m["chrome"], verts=12, part=part)
    cyl(f"{part}Flow", P(0.07, 0.06 + body + 0.06, 0.03), 0.03, 0.12, m["green"], rot=(0, 0, math.pi / 2), verts=12, part=part)
    tube(f"{part}Handle", [P(-0.06, 0.06 + body + 0.08, 0), P(-0.06, 0.06 + body + 0.2, 0), P(0.06, 0.06 + body + 0.2, 0), P(0.06, 0.06 + body + 0.1, 0)], 0.012, m["black"], part=part)


def build():
    m = _mats()
    visitor_chair(m)
    lounge_chair(m)
    waiting_seat(m)
    staff_chair(m)
    bedside_locker(m)
    clinical_cupboard(m)
    office_desk(m)
    staff_table(m)
    hand_basin(m)
    plaster_sink(m)
    kitchenette(m)
    drug_fridge(m)
    medicine_shelves(m)
    instrument_trolley(m)
    oxygen_cylinder(m)
