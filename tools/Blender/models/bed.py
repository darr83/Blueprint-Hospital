"""A ward bed, 0.8 m by 2.1 m, made up and empty: castors, chassis and lifting column, moulded head
and foot boards, split tube side rails, a handset, the chart on the foot board, a puffy pillow and
a blanket that hangs over the sides with the sheet turned down. Head at +Y."""
import math

import bmesh
from mathutils import Vector

from bhkit import cube, cyl, drape, finish, mat, tube

BUDGET = 4000

LENGTH, WIDTH = 2.1, 0.8
TOP = 0.48  # the platform under the mattress; the sheet's top is 0.16 above it


def build():
    frame = mat("Frame", "#DDE3E8", 0.45)
    dark = mat("FrameDark", "#7C8A96", 0.4)
    accent = mat("Accent", "#2C6FAE", 0.4)
    chrome = mat("Chrome", "#C9D2DA", 0.3, metal=0.35)
    rubber = mat("Rubber", "#2A2D31", 0.6)
    mattress = mat("Mattress", "#3E6E92", 0.55)
    sheet = mat("Sheet", "#F4F6F8", 0.85)
    blanket = mat("Blanket", "#8DB7DA", 0.9)
    pillow = mat("Pillow", "#FFFFFF", 0.9)
    paper = mat("Paper", "#FAFAF5", 0.8)
    board = mat("Clipboard", "#9A6B3D", 0.5)
    L, W, top = LENGTH, WIDTH, TOP

    cube("Chassis", (0, 0, 0.13), (W * 0.8, L * 0.84, 0.07), dark, bevel=0.02)
    cube("Column", (0, 0, 0.28), (0.3, 0.6, 0.24), frame, bevel=0.03)
    cube("Platform", (0, 0, top - 0.04), (W, L, 0.07), frame, bevel=0.02)
    cube("PlatformStripe", (0, 0, top - 0.04), (W + 0.004, L * 0.7, 0.02), accent, smooth=False)
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (W * 0.4 - 0.04), sy * (L * 0.42 - 0.04)
            cyl(f"Fork{sx}{sy}", (x, y, 0.1), 0.022, 0.06, chrome, verts=8)
            cyl(f"Wheel{sx}{sy}", (x, y + 0.02, 0.055), 0.055, 0.045, rubber, rot=(0, math.pi / 2, 0), verts=12)
            cyl(f"Hub{sx}{sy}", (x, y + 0.02, 0.055), 0.025, 0.05, chrome, rot=(0, math.pi / 2, 0), verts=8)

    soft = cube("Mattress", (0, 0, top + 0.08), (W - 0.05, L - 0.08, 0.15), mattress, bevel=0.045, bevel_segments=3)
    top_sheet = cube("SheetTop", (0, 0, top + 0.15), (W - 0.04, L - 0.07, 0.025), sheet, bevel=0.01)

    boards = []
    for sy, h, name in ((1, 0.5, "Head"), (-1, 0.36, "Foot")):
        y = sy * (L / 2 - 0.01)
        boards.append(cube(f"{name}Board", (0, y, top + h / 2 - 0.02), (W + 0.04, 0.06, h), frame, bevel=0.028, bevel_segments=3))
        cube(f"{name}Accent", (0, y + sy * 0.032, top + h - 0.08), (W * 0.7, 0.01, 0.03), accent, smooth=False)
        cube(f"{name}Slot", (0, y + sy * 0.03, top + h - 0.035), (W * 0.36, 0.012, 0.035), dark, bevel=0.01)
    cube("ChartBoard", (0.16, -L / 2 - 0.035, top + 0.17), (0.22, 0.012, 0.28), board, smooth=False)
    cube("ChartPaper", (0.16, -L / 2 - 0.042, top + 0.16), (0.19, 0.004, 0.24), paper, smooth=False)
    cube("ChartClip", (0.16, -L / 2 - 0.045, top + 0.29), (0.07, 0.012, 0.025), chrome, smooth=False)

    for sx in (-1, 1):
        x = sx * (W / 2 + 0.025)
        for y0, y1 in ((0.15, 0.95), (-0.95, -0.15)):
            z0, z1 = top + 0.06, top + 0.3
            tube(f"Rail{sx}{y0}", [(x, y0, z0), (x, y0, z1 - 0.05), (x, y0 + 0.05, z1), (x, y1 - 0.05, z1), (x, y1, z1 - 0.05), (x, y1, z0)], 0.017, chrome)
            tube(f"RailMid{sx}{y0}", [(x, y0, top + 0.18), (x, y1, top + 0.18)], 0.012, chrome, sides=0)
    cube("Handset", (W / 2 + 0.055, 0.55, top + 0.18), (0.03, 0.06, 0.14), dark, bevel=0.01)
    cube("HandsetKeys", (W / 2 + 0.072, 0.55, top + 0.2), (0.004, 0.04, 0.07), accent, smooth=False)

    # Pillow: puffed in the middle, pinched at the corners, tipped up against the head board.
    cy, cz = L / 2 - 0.24, top + 0.22
    p = cube("Pillow", (0, cy, cz), (W * 0.66, 0.36, 0.11), pillow)
    bm = bmesh.new()
    bm.from_mesh(p.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=4, use_grid_fill=True)
    for v in bm.verts:
        nx, ny = v.co.x / (W * 0.33), (v.co.y - cy) / 0.18
        puff = max(0.0, 1 - nx * nx) * max(0.0, 1 - ny * ny)
        v.co.z = cz + (v.co.z - cz) * (0.3 + 1.1 * puff) + (v.co.y - cy) * 0.18
    bm.to_mesh(p.data)
    bm.free()
    finish(p, pillow, subdiv=1)

    # Blanket: dropped onto the bed so it hangs and folds at the sides, with the sheet turned down over it.
    cover = drape("Blanket", (0, -0.2, top + 0.32), (W + 0.5, L * 0.7), (22, 34), blanket, onto=[soft, top_sheet, boards[1], p])
    turn = cover.copy()
    turn.data = cover.data.copy()
    turn.modifiers.clear()
    turn.name = "Turndown"
    cover.users_collection[0].objects.link(turn)
    bm = bmesh.new()
    bm.from_mesh(turn.data)
    edge = max(v.co.y for v in bm.verts)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y < edge - 0.16], context="VERTS")
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * 0.014 if v.normal.z > 0 else Vector((0, 0, 0.014))
    bm.to_mesh(turn.data)
    bm.free()
    turn.modifiers.new("Solid", "SOLIDIFY").thickness = 0.01
    finish(turn, sheet)
