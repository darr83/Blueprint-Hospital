# Art baker

The game draws in 2D, so 3D models are rendered offline into isometric sprite sheets here.

| Model | Source | Licence | Output |
| --- | --- | --- | --- |
| `source/ambulance.fbx` | Kenney Car Kit ([kenney.nl](https://kenney.nl)) | CC0 (public domain, no credit required) | `game/Art/Vehicles/ambulance.png` |

## Ambulance

```
python3 tools/ArtBaker/bake_ambulance.py   # needs numpy and Pillow
```

It renders 16 headings (22.5 degree steps) in the game's own isometric projection, in three rows
for the blue lights (off, left on, right on), and repaints the model in UK colours: yellow body
with a green and yellow Battenburg band. `Vehicles.Ambulance` in `game/Scripts/Hospital/Looks.cs`
picks the right cell from the vehicle's facing and the current view rotation. If you change the
cell size or anchor in the script, change the matching constants there too.
