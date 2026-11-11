"""The body parts people are assembled from, in people style A ("Friendly").

This is a kit, not a finished figure: the game (Person3D) places, sizes and colours each part from
a person's look, exactly as it used to place plain balls and capsules, so every uniform, build and
animation carries over. Each part is modelled round the origin to fill a box one unit across
(x left-right, y front-back with the front at -Y, z up), and the game stretches that box to size.
Hair parts are modelled round a head one unit across instead, so they sit on any head.

Surfaces in the material "Tint" take the colour the game asks for (skin, cloth, hair); any other
material keeps its own colour (the glint in an eye, the sole of a shoe).
"""
import math
import random

import bmesh
import bpy

from bhkit import finish, lathe, mat, sphere, tube

BUDGET = 30000  # the whole kit; a person uses a few hundred triangles of each part


TORSO = [(0.28, -0.5), (0.43, -0.49), (0.47, -0.43), (0.45, -0.3), (0.43, -0.05), (0.46, 0.15), (0.5, 0.29), (0.47, 0.39), (0.34, 0.46), (0.16, 0.5)]


def _radius(profile, z):
    """A lathe profile's radius at a height."""
    for (r0, z0), (r1, z1) in zip(profile, profile[1:]):
        if z0 <= z <= z1:
            return r0 + (r1 - r0) * (z - z0) / max(1e-6, z1 - z0)
    return profile[0][0] if z < profile[0][1] else profile[-1][0]


def _edit(obj, fn):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    fn(bm)
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def _cut(obj, point, normal):
    """Slices away everything on the side of the plane the normal points to."""
    def go(bm):
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=point, plane_no=normal, clear_outer=True)
    return _edit(obj, go)


def _shell(obj, thickness):
    s = obj.modifiers.new("Solid", "SOLIDIFY")
    s.thickness = thickness
    s.offset = -1
    return obj


def _lumpy(name, radius, amount, tint, part, seed, subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius)
    o = bpy.context.active_object
    o.name = name
    rng = random.Random(seed)
    for v in o.data.vertices:
        v.co *= 1 + rng.uniform(-amount, amount)
    return finish(o, tint, part=part, subdiv=1)


def build():
    tint = mat("Tint", "#FFFFFF", 0.8)
    ink = mat("Ink", "#1D2B36", 0.3)
    glint = mat("Glint", "#FFFFFF", 0.3)
    sole = mat("Sole", "#E9E6DF", 0.7)

    # ---- head: round, a touch wider at the cheeks, with ears and a button nose
    head = sphere("Head", (0, 0, 0), 0.5, tint, seg=24, rings=14, part="head")
    for v in head.data.vertices:
        cheeks = math.exp(-((v.co.z + 0.14) / 0.24) ** 2)
        v.co.x *= 1 + 0.05 * cheeks
        if v.co.y < 0:
            v.co.y *= 1 - 0.04 * cheeks  # a slightly flatter face
    sphere("Nose", (0, -0.485, -0.07), 0.055, tint, seg=8, rings=5, scale=(1, 0.8, 0.9), part="head")
    for side in (-1, 1):
        sphere(f"Ear{side}", (side * 0.5, 0.02, -0.03), 0.09, tint, seg=8, rings=5, scale=(0.45, 0.75, 1), part="head")

    # ---- face
    sphere("Eye", (0, 0, 0), 0.5, ink, seg=12, rings=8, scale=(1, 0.5, 1), part="eye")
    sphere("EyeGlint", (0.14, -0.2, 0.18), 0.15, glint, seg=6, rings=4, part="eye")
    tube("Smile", [(x, (x * x) * 0.9, -0.3 + ((x / 0.44) ** 2) * 0.6) for x in [i * 0.088 - 0.44 for i in range(11)]], 0.1, tint, part="smile")
    tube("Brow", [(x, (x * x) * 0.5, 0.2 - ((x / 0.42) ** 2) * 0.35) for x in [i * 0.14 - 0.42 for i in range(7)]], 0.16, tint, part="brow")

    # ---- body
    lathe("Torso", TORSO, tint, seg=20, part="torso")
    # ---- clothes and kit that lie on the torso (modelled on the torso's own box, a hair outside its surface)
    def front(x, z, lift=0.012):
        """How far forward (-y) the torso's surface is at (x, z), plus a little lift."""
        r = _radius(TORSO, z)
        return -(math.sqrt(max(0.0, r * r - x * x)) + lift)

    def panel(name, part, half_width, z0, z1, material, lift=0.012, columns=6, rows=6, taper=0.0):
        """A piece of cloth lying on the front of the torso between two heights."""
        bm = bmesh.new()
        grid = []
        for j in range(rows + 1):
            z = z0 + (z1 - z0) * j / rows
            w = half_width * (1 - taper * (1 - j / rows))
            w = min(w, _radius(TORSO, z) * 0.97)
            grid.append([bm.verts.new((x, front(x, z, lift), z)) for x in [-w + 2 * w * i / columns for i in range(columns + 1)]])
        for j in range(rows):
            for i in range(columns):
                bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(o)
        o.modifiers.new("Solid", "SOLIDIFY").thickness = 0.02
        return finish(o, material, part=part)

    panel("Apron", "apron", 0.36, -0.5, 0.3, tint, lift=0.02)
    panel("ShirtFront", "shirt_front", 0.15, -0.36, 0.44, tint, taper=0.5)
    panel("Tie", "tie", 0.06, -0.2, 0.36, tint, lift=0.03, columns=2, taper=-0.6)
    panel("Pocket", "pocket", 0.11, 0.0, 0.17, tint, lift=0.02, columns=3, rows=2)
    for o in (bpy.data.objects["Pocket"],):
        for v in o.data.vertices:
            v.co.x += 0.2
            v.co.y = front(v.co.x, v.co.z, 0.02)
    tube("PocketPen", [(0.17, front(0.17, 0.14, 0.04), 0.14), (0.17, front(0.17, 0.22, 0.04), 0.22)], 0.018, mat("Pen", "#2F6FD0", 0.4), sides=0, part="pocket")
    panel("Badge", "badge", 0.07, 0.16, 0.24, tint, lift=0.025, columns=2, rows=1)
    for v in bpy.data.objects["Badge"].data.vertices:
        v.co.x -= 0.2
        v.co.y = front(v.co.x, v.co.z, 0.025)
    silver = mat("Silver", "#E8EDF2", 0.3, metal=0.3)
    sphere("FobWatch", (0.2, front(0.2, 0.22, 0.03), 0.22), 0.055, silver, seg=10, rings=6, scale=(1, 0.45, 1), part="fob")
    tube("FobStrap", [(0.2, front(0.2, 0.33, 0.03), 0.33), (0.2, front(0.2, 0.25, 0.03), 0.25)], 0.014, mat("FobStrap", "#2B3440", 0.5), sides=0, part="fob")
    # Lanyard: a ribbon from each shoulder to the card on the chest.
    ribbon = mat("Lanyard", "#005EB8", 0.6)
    for side in (-1, 1):
        tube(f"Lanyard{side}", [(side * (0.2 - 0.19 * t), front(side * (0.2 - 0.19 * t), 0.45 - 0.45 * t, 0.03), 0.45 - 0.45 * t) for t in [i / 5 for i in range(6)]], 0.022, ribbon, sides=0, part="lanyard")
    panel("LanyardCard", "lanyard", 0.07, -0.13, 0.02, mat("Card", "#FFFFFF", 0.5), lift=0.04, columns=2, rows=2)
    panel("LanyardPhoto", "lanyard", 0.03, -0.07, 0.0, ribbon, lift=0.062, columns=1, rows=1)
    # Collar piping: a V at the neck.
    tube("Collar", [(x, front(x, 0.22 + abs(x) * 1.25, 0.02), 0.22 + abs(x) * 1.25) for x in [i * 0.045 - 0.18 for i in range(9)]], 0.028, tint, sides=0, part="collar")
    # A band round the body one unit across (hems, stripes, reflective bands).
    lathe("Band", [(0.46, -0.5), (0.5, -0.3), (0.5, 0.3), (0.46, 0.5)], tint, seg=20, part="band")

    lathe("Sleeve", [(0.0, 0.5), (0.28, 0.47), (0.44, 0.38), (0.5, 0.22), (0.47, -0.3), (0.5, -0.44), (0.5, -0.5), (0.36, -0.5)], tint, seg=12, part="sleeve")
    lathe("Limb", [(0.0, 0.5), (0.3, 0.46), (0.46, 0.36), (0.5, 0.2), (0.43, -0.28), (0.37, -0.42), (0.22, -0.49), (0.0, -0.5)], tint, seg=12, part="limb")
    sphere("Hand", (0, 0.02, -0.02), 0.5, tint, seg=10, rings=7, scale=(0.78, 0.62, 0.96), part="hand")
    sphere("Thumb", (0, -0.27, 0.06), 0.17, tint, seg=6, rings=4, scale=(0.8, 1, 1.2), part="hand")

    # ---- shoe: rounded toe at the front, lower than the heel, on a pale sole
    upper = sphere("Shoe", (0, 0, -0.05), 0.5, tint, seg=12, rings=8, scale=(0.9, 1.0, 0.9), part="shoe")
    for v in upper.data.vertices:
        if v.co.y < 0 and v.co.z > -0.2:
            v.co.z = -0.2 + (v.co.z + 0.2) * (1 - 0.55 * min(1.0, -v.co.y / 0.4))
        v.co.z = max(v.co.z, -0.34)
    sole_part = sphere("ShoeSole", (0, 0, -0.42), 0.5, sole, seg=12, rings=6, scale=(0.94, 1.0, 0.16), part="shoe")
    _ = sole_part

    # ---- hair (round a head one unit across, centred on the origin)
    cap = sphere("HairCap", (0, 0, 0), 0.56, tint, seg=24, rings=14, part="hair_cap")
    _cut(cap, (0, -0.42, 0.25), (0, -0.6, -0.8))  # the hairline: forehead at the front, down to the nape behind
    _shell(cap, 0.06)
    fringe = sphere("Fringe", (0, 0, 0), 0.5, tint, seg=12, rings=8, scale=(1, 0.62, 0.5), part="hair_fringe")
    for v in fringe.data.vertices:
        v.co.z -= 0.25 * (v.co.x + 0.5) ** 2  # sweeps down to one side
    _lumpy("Bun", 0.5, 0.05, tint, "hair_bun", 3, subdivisions=1)
    tube("Tail", [(0, 0.1 * math.sin(t * 2.6), 0.5 - t) for t in [i / 8 for i in range(9)]], 0.3, tint, part="hair_tail")
    tail = bpy.context.active_object
    for v in tail.data.vertices:  # taper to a point at the bottom
        k = 0.45 + 0.55 * min(1.0, (v.co.z + 0.5) / 0.7)
        v.co.x *= k
        v.co.y = (v.co.y - 0.1 * math.sin((0.5 - v.co.z) * 2.6)) * k + 0.1 * math.sin((0.5 - v.co.z) * 2.6)
    bob = lathe("Bob", [(0.0, 0.56), (0.3, 0.5), (0.5, 0.3), (0.58, 0.0), (0.6, -0.3), (0.56, -0.5)], tint, seg=24, part="hair_bob")
    _cut(bob, (0, -0.2, 0.22), (0, -0.94, -0.34))
    _shell(bob, 0.06)
    long_hair = lathe("Long", [(0.0, 0.56), (0.3, 0.5), (0.5, 0.3), (0.58, 0.0), (0.6, -0.5), (0.56, -0.95), (0.42, -1.1)], tint, seg=24, part="hair_long")
    _cut(long_hair, (0, -0.16, 0.24), (0, -0.97, -0.24))
    _shell(long_hair, 0.06)
    _lumpy("Afro", 0.5, 0.045, tint, "hair_afro", 5)
    _lumpy("Curl", 0.5, 0.1, tint, "hair_curl", 7, subdivisions=1)
