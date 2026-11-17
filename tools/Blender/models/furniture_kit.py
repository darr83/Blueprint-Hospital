"""Furniture kit: chairs, bedside locker, clinical cupboard, office desk and staff table,
made in the game's own units like equipment_kit (a tile across, px/39.19 up), standing on the floor
at the origin with the front towards the viewer (game +z, Blender -Y).

Material names say what each surface is made of ("Tint_fabric", "TintDark_wood", "Chrome"...), so the
game's surface detail can pick wood grain, fabric weave or brushed metal for it."""
import math

from bhkit import cube, cyl, mat, tube

BUDGET = 12000

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
