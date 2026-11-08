"""Bake the ambulance model into isometric sprites for the game.

The game draws everything in 2D, so the 3D ambulance (tools/ArtBaker/source/ambulance.fbx,
Kenney Car Kit, CC0) is rendered here, offline, into one sprite sheet:

    16 headings (columns 0-15 = 0, 22.5, 45 ... degrees in grid space; 0 = towards +x)
    x 3 light states (row 0 = blue lights off, row 1 = left lights on, row 2 = right lights on)

It uses the game's own projection (IsoDraw.ToScreen): a grid point (x, y) in tiles goes to
screen (32 (x - y), 16 (x + y)), and height is drawn at a fixed number of pixels per model unit.
The paint is changed to UK colours: yellow body with a green and yellow Battenburg band.

Usage (needs numpy and Pillow):
    python3 tools/ArtBaker/bake_ambulance.py
which writes game/Art/Vehicles/ambulance.png. Vehicles.Ambulance in Looks.cs reads it using
the CELL_* and ANCHOR_* values printed here, so update those if you change the layout.
"""

import math
import os
import re
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "source", "ambulance.fbx")
OUTPUT = os.path.join(HERE, "..", "..", "game", "Art", "Vehicles", "ambulance.png")

HEADINGS = 16
CELL_W, CELL_H = 576, 400          # pixels per sprite
ANCHOR_X, ANCHOR_Y = 288, 232      # where the model's origin (centre of the vehicle, on the ground) lands
SUPERSAMPLE = 3

TILES_PER_UNIT = 11.6 / 32         # the model is 32 units long; the game ambulance is 11.6 tiles (5.8 m)
PIXELS_PER_UNIT = 4.8              # height: roof (16 units) about 77 px, just under the 84 px (3 m) walls

YELLOW = np.array([0.98, 0.84, 0.10])
GREEN = np.array([0.08, 0.45, 0.24])
LIGHT_OFF = np.array([0.13, 0.25, 0.55])
LIGHT_ON = np.array([0.55, 0.82, 1.00])
CHECK = 1.6                        # Battenburg square size in model units (about 0.3 m)

# Light from the upper left, like IsoDraw boxes: in (grid x, grid y, up).
LIGHT = np.array([-0.2, 0.6, 1.0])
LIGHT /= np.linalg.norm(LIGHT)
TOP_LIT = LIGHT[2]


def read_fbx(path):
    src = open(path, encoding="utf-8", errors="replace").read()

    def arr(block, key):
        m = re.search(key + r": \*\d+ \{\s*a: ([^}]*)\}", block)
        return [float(v) for v in re.split(r"[,\s]+", m.group(1).strip()) if v] if m else None

    geos = {}
    for m in re.finditer(r'Geometry: (\d+), "Geometry::", "Mesh" \{(.*?)\n\t\}', src, re.S):
        b = m.group(2)
        geos[m.group(1)] = (arr(b, "Vertices"), [int(v) for v in arr(b, "PolygonVertexIndex")], arr(b, "Materials"))

    models = {}
    for m in re.finditer(r'Model: (\d+), "Model::(\w+)", "Mesh" \{(.*?)\n\t\}', src, re.S):
        b = m.group(3)

        def prop(k):
            return [float(x) for x in re.search(k + r'", "[^"]*", "", "A\+?",([-\dE.,]+)', b).group(1).split(",")]

        models[m.group(1)] = (m.group(2), prop("Lcl Translation"), prop("Lcl Rotation"), prop("Lcl Scaling"))

    mats = {}
    for m in re.finditer(r'Material: (\d+), "Material::(\w+)".*?"DiffuseColor", "Color", "", "A",([\d.,E-]+)', src, re.S):
        mats[m.group(1)] = (m.group(2), np.array([float(x) for x in m.group(3).split(",")]))

    conns = re.findall(r'C: "OO",(\d+),(\d+)', src)
    polys = []  # (vertices Nx3 in model space, material name, colour)
    for mid, (name, t, r, s) in models.items():
        gid = next(a for a, b in conns if b == mid and a in geos)
        mlist = [a for a, b in conns if b == mid and a in mats]
        V, I, M = geos[gid]
        V = np.array(V).reshape(-1, 3) * np.array(s)
        ry = math.radians(r[1])
        R = np.array([[math.cos(ry), 0, math.sin(ry)], [0, 1, 0], [-math.sin(ry), 0, math.cos(ry)]])
        V = V @ R.T + np.array(t)
        poly, pi = [], 0
        for idx in I:
            if idx >= 0:
                poly.append(idx)
                continue
            poly.append(~idx)
            mi = int(M[pi]) if M and len(M) > 1 else 0
            mname, colour = mats[mlist[mi]]
            polys.append((V[poly], mname, colour))
            poly, pi = [], pi + 1
    return polys


def paint(mname, colour, pos, normal, lights):
    """UK colours, worked out per pixel from the model-space position."""
    if mname == "paintWhite":
        return YELLOW
    if mname == "lightBlue":
        side = "left" if pos[0] > 0 else "right"
        return LIGHT_ON if lights == side else LIGHT_OFF
    return colour


def paint_band(pos, normal):
    """The model's red paint, per pixel: the cab roof turns yellow, everything else becomes a
    green and yellow Battenburg band, with squares along the sides (z) or across the ends (x)."""
    x, y, z = pos[:, 0], pos[:, 1], pos[:, 2]
    along = z if abs(normal[0]) > 0.5 else x
    green = (np.floor(along / CHECK) + np.floor(y / CHECK)) % 2 == 0
    cab_roof = (z > 5) & (y >= 9)
    return np.where((green & ~cab_roof)[:, None], GREEN, YELLOW)


def render(polys, heading, lights):
    W, H = CELL_W * SUPERSAMPLE, CELL_H * SUPERSAMPLE
    rgb = np.zeros((H, W, 3))
    alpha = np.zeros((H, W))
    zbuf = np.full((H, W), -1e9)
    fx, fy = math.cos(heading), math.sin(heading)
    lx, ly = fy, -fx  # the vehicle's left in grid space (grid x east, y south)

    def to_world(P):
        gx = (P[:, 2] * fx + P[:, 0] * lx) * TILES_PER_UNIT
        gy = (P[:, 2] * fy + P[:, 0] * ly) * TILES_PER_UNIT
        h = P[:, 1] * PIXELS_PER_UNIT
        sx = (ANCHOR_X + 32 * (gx - gy)) * SUPERSAMPLE
        sy = (ANCHOR_Y + 16 * (gx + gy) - h) * SUPERSAMPLE
        near = gx + gy + h / 16  # any depth that changes along the view ray works for an orthographic view
        return np.stack([sx, sy], 1), near

    for P, mname, colour in polys:
        n = np.cross(P[1] - P[0], P[2] - P[0])
        nn = np.linalg.norm(n)
        if nn == 0:
            continue
        n /= nn
        nw = np.array([n[2] * fx + n[0] * lx, n[2] * fy + n[0] * ly, n[1]])
        lit = 0.68 + 0.32 * min(1.0, max(0.0, float(nw @ LIGHT)) / TOP_LIT)
        if mname == "lightBlue" and lights is not None and (P[:, 0].mean() > 0) == (lights == "left"):
            lit = 1.0
        S, d = to_world(P)
        for k in range(1, len(P) - 1):
            idx = [0, k, k + 1]
            tri, dz, mp = S[idx], d[idx], P[idx]
            x0, y0 = np.floor(tri.min(0)).astype(int)
            x1, y1 = np.ceil(tri.max(0)).astype(int)
            x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, W - 1), min(y1, H - 1)
            if x1 < x0 or y1 < y0:
                continue
            X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            (ax, ay), (bx, by), (cx, cy) = tri
            den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(den) < 1e-9:
                continue
            w1 = ((by - cy) * (X - cx) + (cx - bx) * (Y - cy)) / den
            w2 = ((cy - ay) * (X - cx) + (ax - cx) * (Y - cy)) / den
            w3 = 1 - w1 - w2
            inside = (w1 >= -1e-6) & (w2 >= -1e-6) & (w3 >= -1e-6)
            z = w1 * dz[0] + w2 * dz[1] + w3 * dz[2]
            zb = zbuf[y0:y1 + 1, x0:x1 + 1]
            m = inside & (z > zb)
            if not m.any():
                continue
            zb[m] = z[m]
            alpha[y0:y1 + 1, x0:x1 + 1][m] = 1
            if mname == "paintRed":
                pos = w1[m][:, None] * mp[0] + w2[m][:, None] * mp[1] + w3[m][:, None] * mp[2]
                rgb[y0:y1 + 1, x0:x1 + 1][m] = np.clip(paint_band(pos, n) * lit, 0, 1)
            elif mname in ("paintWhite", "lightBlue"):
                rgb[y0:y1 + 1, x0:x1 + 1][m] = np.clip(paint(mname, colour, P.mean(0), n, lights) * lit, 0, 1)
            else:
                rgb[y0:y1 + 1, x0:x1 + 1][m] = np.clip(colour * lit, 0, 1)

    # Downsample with premultiplied alpha for clean edges.
    pre = np.dstack([rgb * alpha[..., None], alpha])
    pre = pre.reshape(CELL_H, SUPERSAMPLE, CELL_W, SUPERSAMPLE, 4).mean((1, 3))
    a = pre[..., 3]
    out = np.zeros((CELL_H, CELL_W, 4))
    out[..., :3] = np.where(a[..., None] > 0, pre[..., :3] / np.maximum(a[..., None], 1e-9), 0)
    out[..., 3] = a
    if a[0].any() or a[-1].any() or a[:, 0].any() or a[:, -1].any():
        print(f"warning: heading {math.degrees(heading):.1f} touches the cell edge", file=sys.stderr)
    return (out * 255 + 0.5).astype(np.uint8)


def main():
    polys = read_fbx(SOURCE)
    sheet = np.zeros((CELL_H * 3, CELL_W * HEADINGS, 4), np.uint8)
    for row, lights in enumerate([None, "left", "right"]):
        for col in range(HEADINGS):
            cell = render(polys, col * 2 * math.pi / HEADINGS, lights)
            sheet[row * CELL_H:(row + 1) * CELL_H, col * CELL_W:(col + 1) * CELL_W] = cell
    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    Image.fromarray(sheet, "RGBA").save(OUTPUT, optimize=True)
    print(f"wrote {os.path.normpath(OUTPUT)}: {HEADINGS} headings x 3, cell {CELL_W}x{CELL_H}, anchor ({ANCHOR_X}, {ANCHOR_Y})")


if __name__ == "__main__":
    main()
