# Blender models

The game's hand-modelled 3D assets are made here, by script, in Blender, and written to
`game/Art/Models/*.glb`. The `.glb` files are committed, so nobody needs Blender to play or build
the game; it is only needed to change a model.

```
python3.13 -m venv .venv && .venv/bin/pip install -r tools/Blender/requirements.txt
.venv/bin/python tools/Blender/build.py                    # rebuild every model
.venv/bin/python tools/Blender/build.py bed                # just one
.venv/bin/python tools/Blender/build.py --preview shots    # and render a picture of each
```

`requirements.txt` installs Blender itself as a Python module (`bpy`), so there is nothing else to
install and it runs without a screen.

## Adding a model

Add `models/<name>.py` with:

- `BUDGET`: the most triangles the model may have in the game. `build.py` slims the model down to
  it and fails if it cannot. The game refuses anything over 6,000 (`Models3D.TriangleLimit`), and
  things that appear many times at once (beds, people) should stay near 4,000 or less.
- `build()`: makes the model in the empty scene using the helpers in `bhkit.py`.

Conventions (see `bhkit.py`):

- Real metres, Z up, standing on the floor at the origin. A front faces -Y; a bed's head is at +Y.
  The game scales metres to its own units when it loads the model.
- Plain coloured materials from `mat()`. Keep `metal` low: the game has no sky to reflect.
- Give objects a `part="Name"` to keep them as a separate node in the game (a person's arm, a lid
  that opens). Everything else is joined into one mesh called `Main`.

## In the game

`Models3D.Make("bed")` returns a ready-sized copy. To look at every model in the game, next to
today's code-built bed for scale:

```
godot --path game -- --model-shots=DIR      # needs a display
```

The smoke test loads every model and checks it is within the triangle limit.

| Model | What | Triangles |
| --- | --- | --- |
| `bed` | Ward bed, made up, with pillow and draped blanket | 3,874 |
