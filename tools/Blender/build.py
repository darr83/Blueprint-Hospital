"""Builds the game's Blender-made models into game/Art/Models/*.glb.

    python tools/Blender/build.py            # every model
    python tools/Blender/build.py bed        # just these
    python tools/Blender/build.py --preview DIR   # also render a picture of each into DIR

Each model is a module in tools/Blender/models/ with a BUDGET (most triangles allowed in the game)
and a build() function that makes it in the current, empty scene.
"""
import importlib
import math
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import bhkit  # noqa: E402

HERE = pathlib.Path(__file__).parent
OUT = HERE.parent.parent / "game" / "Art" / "Models"


def preview(glb, png):
    """Renders the exported model from the game's camera angle (30 degrees down, 45 round)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(glb))
    corners = [o.matrix_world @ Vector(c) for o in bpy.context.scene.objects if o.type == "MESH" for c in o.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), 0))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    cam = bpy.data.cameras.new("Camera")
    cam.type = "ORTHO"
    cam.ortho_scale = max(hi - lo) * 1.3
    rig = bpy.data.objects.new("Camera", cam)
    bpy.context.collection.objects.link(rig)
    back = Vector((-math.cos(math.radians(30)) * math.sin(math.radians(45)), -math.cos(math.radians(30)) * math.cos(math.radians(45)), math.sin(math.radians(30))))
    rig.location = (lo + hi) / 2 + back * 10
    rig.rotation_euler = (-back).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = rig
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    bpy.context.collection.objects.link(sun)
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.6, 0.65, 0.7, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    bpy.context.scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=20)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.render.resolution_x = scene.render.resolution_y = 700
    scene.render.filepath = str(png)
    bpy.ops.render.render(write_still=True)


def main(argv):
    preview_dir = None
    if "--preview" in argv:
        i = argv.index("--preview")
        preview_dir = pathlib.Path(argv[i + 1])
        argv = argv[:i] + argv[i + 2:]
    names = argv or sorted(p.stem for p in (HERE / "models").glob("*.py") if not p.stem.startswith("_"))
    OUT.mkdir(parents=True, exist_ok=True)
    failed = False
    for name in names:
        module = importlib.import_module(f"models.{name}")
        bhkit.reset()
        module.build()
        path = OUT / f"{name}.glb"
        count = bhkit.export(str(path), module.BUDGET)
        over = count > module.BUDGET
        failed |= over
        print(f"[models] {name}: {count} triangles (budget {module.BUDGET}){' OVER BUDGET' if over else ''}, {path.stat().st_size // 1024} KB")
        if preview_dir:
            preview_dir.mkdir(parents=True, exist_ok=True)
            preview(path, preview_dir / f"{name}.png")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
