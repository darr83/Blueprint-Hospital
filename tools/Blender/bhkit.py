"""Shared building blocks for Blueprint Hospital's Blender-made models.

Models are written in real metres with Blender's Z up. Anything with a front faces -Y (which is +Z
in the game), and a bed's head is at +Y. The game scales metres to its own stylised units when it
loads a model (see game/Scripts/World3D/Models3D.cs), so a model never has to know about them.

Every object belongs to a "part" (default "Main"). On export each part becomes one mesh node, so
the game can move parts on their own (a person's arms and legs) while the rest stays one draw.
"""
import math

import bmesh
import bpy

_materials = {}


def reset():
    """An empty scene to build one model in."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _materials.clear()


def _linear(hex_colour):
    h = hex_colour.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((c + 0.055) / 1.055) ** 2.4 if c > 0.04045 else c / 12.92 for c in srgb) + (1.0,)


def mat(name, colour, rough=0.7, metal=0.0):
    """A plain painted material, shared by name. The game has no sky to reflect, so keep metal low."""
    if name in _materials:
        return _materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = _linear(colour)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    _materials[name] = m
    return m


def finish(obj, material, part="Main", subdiv=0, bevel=0.0, bevel_segments=2, smooth=True):
    """Gives an object its material, part, and optional rounded edges or smoothing."""
    obj.data.materials.clear()
    obj.data.materials.append(material)
    obj["part"] = part
    if bevel > 0:
        b = obj.modifiers.new("Bevel", "BEVEL")
        b.width = bevel
        b.segments = bevel_segments
        b.limit_method = "ANGLE"
    if subdiv:
        s = obj.modifiers.new("Subdiv", "SUBSURF")
        s.levels = subdiv
        s.render_levels = subdiv
    if smooth:
        for poly in obj.data.polygons:
            poly.use_smooth = True
    return obj


def cube(name, loc, size, material, **kw):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return finish(o, material, **kw)


def sphere(name, loc, radius, material, scale=(1, 1, 1), seg=16, rings=10, **kw):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=loc, segments=seg, ring_count=rings)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return finish(o, material, **kw)


def cyl(name, loc, radius, depth, material, rot=(0, 0, 0), verts=16, scale=(1, 1, 1), **kw):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, location=loc, rotation=rot, vertices=verts)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return finish(o, material, **kw)


def tube(name, points, radius, material, sides=1, **kw):
    """A round tube along a line of points: rails, lanyards, a smile."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = sides
    cu.use_fill_caps = True
    sp = cu.splines.new("POLY")
    sp.points.add(len(points) - 1)
    for p, co in zip(sp.points, points):
        p.co = (*co, 1)
    o = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(o)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.convert(target="MESH")
    return finish(bpy.context.active_object, material, **kw)


def lathe(name, profile, material, loc=(0, 0, 0), sx=1.0, sy=1.0, seg=20, **kw):
    """Spins a (radius, height) profile round Z: a torso, a trouser leg, a bottle."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        rings.append([bm.verts.new((loc[0] + r * math.cos(2 * math.pi * i / seg) * sx, loc[1] + r * math.sin(2 * math.pi * i / seg) * sy, loc[2] + z)) for i in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    return finish(o, material, **kw)


def drape(name, loc, size, grid, material, onto, frames=40, thickness=0.012, **kw):
    """A cloth dropped onto the named objects and left to settle: blankets and sheets."""
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=grid[0], y_subdivisions=grid[1], size=1, location=loc)
    cloth = bpy.context.active_object
    cloth.name = name
    cloth.scale = (size[0], size[1], 1)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in onto:
        o.modifiers.new("Collision", "COLLISION")
        o.collision.thickness_outer = 0.01
    sim = cloth.modifiers.new("Cloth", "CLOTH")
    sim.settings.quality = 8
    sim.settings.mass = 0.4
    sim.settings.tension_stiffness = 20
    sim.settings.bending_stiffness = 2.0
    sim.collision_settings.distance_min = 0.008
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, frames
    sim.point_cache.frame_end = frames
    for f in range(1, frames + 1):
        scene.frame_set(f)
        bpy.context.view_layer.update()
    settled = bpy.data.meshes.new_from_object(cloth.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    cloth.modifiers.clear()
    cloth.data = settled
    for o in onto:
        o.modifiers.remove(o.modifiers["Collision"])
    scene.frame_set(1)
    if thickness > 0:
        cloth.modifiers.new("Solid", "SOLIDIFY").thickness = thickness
    return finish(cloth, material, **kw)


def triangles(objects=None):
    objects = objects if objects is not None else [o for o in bpy.context.scene.objects if o.type == "MESH"]
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects)


def _apply_modifiers(obj):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def export(path, budget):
    """Applies modifiers, slims the model to its triangle budget, joins each part into one mesh and
    writes a .glb. Returns the triangle count."""
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for o in meshes:
        _apply_modifiers(o)

    # Slim the heavy objects only: small details (a pen, a castor) fall apart if decimated.
    for _ in range(6):
        total = triangles(meshes)
        if total <= budget:
            break
        heavy = [o for o in meshes if triangles([o]) > 120]
        light = total - triangles(heavy)
        ratio = max(0.05, (budget * 0.97 - light) / max(1, triangles(heavy)))
        for o in heavy:
            d = o.modifiers.new("Slim", "DECIMATE")
            d.ratio = ratio
            _apply_modifiers(o)

    parts = {}
    for o in meshes:
        parts.setdefault(o.get("part", "Main"), []).append(o)
    for part, objs in parts.items():
        bpy.ops.object.select_all(action="DESELECT")
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        if len(objs) > 1:
            bpy.ops.object.join()
        joined = bpy.context.view_layer.objects.active
        joined.name = part
        joined.data.name = part

    total = triangles()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_apply=True, use_selection=False, export_extras=False)
    return total
