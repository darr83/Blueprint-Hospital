"""Export the ambulance model as a 3D mesh for the game's 3D world.

Reads the same Kenney model as bake_ambulance.py (tools/ArtBaker/source/ambulance.fbx, CC0),
repaints it in UK colours (yellow, with the green and yellow Battenburg band cut into real
squares) and writes game/Art/Vehicles/ambulance.obj plus its .mtl, already in game units:
x across (the vehicle's left), y up, z forwards, 1 unit = 1 tile; heights use the game's
3D height scale so the roof sits just under the walls, as in the 2D sprite.
The blue lights are their own materials (lightLeft, lightRight), so the game can flash them.

Usage (needs numpy):  python3 tools/ArtBaker/export_ambulance_mesh.py
"""

import os

import numpy as np

import bake_ambulance as bake

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "game", "Art", "Vehicles")
HEIGHT_PX_PER_UNIT = 32 * 2 ** 0.5 * 3 ** 0.5 / 2   # Kit3D.HeightPxPerUnit
VERTICAL = bake.PIXELS_PER_UNIT / HEIGHT_PX_PER_UNIT

COLOURS = {
    "yellow": bake.YELLOW,
    "green": bake.GREEN,
    "lightLeft": bake.LIGHT_OFF,
    "lightRight": bake.LIGHT_OFF,
}


def split(tri, limit):
    """Cuts a triangle into smaller ones until every edge is shorter than limit."""
    a, b, c = tri
    edges = [np.linalg.norm(b - a), np.linalg.norm(c - b), np.linalg.norm(a - c)]
    longest = int(np.argmax(edges))
    if edges[longest] <= limit:
        return [tri]
    p, q, r = [(a, b, c), (b, c, a), (c, a, b)][longest]
    m = (p + q) / 2
    return split(np.array([p, m, r]), limit) + split(np.array([m, q, r]), limit)


def main():
    faces = {}  # material -> list of (triangle, normal)
    colours = {}
    for P, mname, colour in bake.read_fbx(bake.SOURCE):
        n = np.cross(P[1] - P[0], P[2] - P[0])
        if np.linalg.norm(n) == 0:
            continue
        n = n / np.linalg.norm(n)
        for k in range(1, len(P) - 1):
            tri = P[[0, k, k + 1]]
            if mname == "paintRed":
                for small in split(tri, bake.CHECK / 3):
                    centre = small.mean(0)[None, :]
                    green = (bake.paint_band(centre, n)[0] == bake.GREEN).all()
                    faces.setdefault("green" if green else "yellow", []).append((small, n))
            elif mname == "paintWhite":
                faces.setdefault("yellow", []).append((tri, n))
            elif mname == "lightBlue":
                name = "lightLeft" if P[:, 0].mean() > 0 else "lightRight"
                faces.setdefault(name, []).append((tri, n))
            else:
                faces.setdefault(mname, []).append((tri, n))
                colours[mname] = colour

    colours.update(COLOURS)
    os.makedirs(OUT, exist_ok=True)
    scale = np.array([bake.TILES_PER_UNIT, VERTICAL, bake.TILES_PER_UNIT])
    with open(os.path.join(OUT, "ambulance.mtl"), "w") as mtl:
        for name in faces:
            r, g, b = colours[name]
            mtl.write(f"newmtl {name}\nKd {r:.4f} {g:.4f} {b:.4f}\n\n")

    count = 0
    with open(os.path.join(OUT, "ambulance.obj"), "w") as obj:
        obj.write("# UK ambulance for Blueprint Hospital, from the Kenney Car Kit (CC0). Made by export_ambulance_mesh.py.\nmtllib ambulance.mtl\n")
        vi = ni = 1
        for name, tris in faces.items():
            obj.write(f"usemtl {name}\n")
            for tri, n in tris:
                for v in tri * scale:
                    obj.write(f"v {v[0]:.4f} {v[1]:.4f} {v[2]:.4f}\n")
                m = n / scale
                m /= np.linalg.norm(m)
                obj.write(f"vn {m[0]:.4f} {m[1]:.4f} {m[2]:.4f}\n")
                obj.write(f"f {vi}//{ni} {vi + 1}//{ni} {vi + 2}//{ni}\n")
                vi += 3
                ni += 1
                count += 1
    print(f"wrote ambulance.obj: {count} triangles, materials {', '.join(faces)}")


if __name__ == "__main__":
    main()
