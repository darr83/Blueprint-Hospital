using System;
using System.Linq;
using BlueprintHospital.Hospital;
using BlueprintHospital.Simulation.Core;
using BlueprintHospital.World3D;
using Godot;

namespace BlueprintHospital.Core;

/// <summary>
/// Review shots of the Blender-made models, run with: godot --path game -- --model-shots=DIR (needs a display).
/// Stands every model in res://Art/Models in a row on a plain floor, with today's code-built bed at
/// the end for scale, and photographs the row from all four sides.
/// </summary>
public partial class ModelShots : Node
{
    private static readonly (double X, double Y) Centre = (16, 9);

    /// <summary>Where the row of key equipment starts, and how far apart its groups stand.</summary>
    private static readonly (double X, double Y) KitRow = (Centre.X + 9, Centre.Y + 9);

    private const double KitStep = 3.6;

    private string _directory = "";
    private int _frame;
    private int _shot;
    private int _count;
    private HospitalScene? _hospital;

    public static bool Begin()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--model-shots=", StringComparison.Ordinal))
            {
                GameManager.Instance.AddChild(new ModelShots { Name = "ModelShots", ProcessMode = ProcessModeEnum.Always, _directory = arg["--model-shots=".Length..] });
                Callable.From(() => GameManager.Instance.StartNewGame("Model Review General")).CallDeferred();
                return true;
            }
        }

        return false;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_hospital is null)
        {
            if (GetTree().CurrentScene is HospitalScene h && h.IsNodeReady())
            {
                _hospital = h;
                h.Hud.Visible = false;
                if (h.GetNodeOrNull<CanvasItem>("TileCursor") is { } cursor)
                {
                    cursor.Visible = false;
                }

                h.Session.Clock.SetSpeed(SimulationSpeed.Paused);
                Build(h);
                _frame = 0;
            }

            return;
        }

        if (_frame % 8 != 0)
        {
            return;
        }

        var view = _shot / 2;
        if (view >= 6)
        {
            GD.Print("[models] Done.");
            GetTree().Quit(0);
            return;
        }

        if (_shot % 2 == 0)
        {
            if (view < 4)
            {
                _hospital.RotateView(view - IsoDraw.Rotation);
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(Centre.X + (_count * 1.5), Centre.Y - (_count * 1.5)), 3f);
            }
            else
            {
                // The two halves of the equipment row.
                var along = view == 4 ? 1.0 : 4.1;
                _hospital.RotateView(-IsoDraw.Rotation);
                _hospital.Camera.SnapTo(IsoDraw.ToScreen(KitRow.X + (along * KitStep), KitRow.Y - (along * KitStep)), 2.3f);
            }
        }
        else
        {
            DirAccess.MakeDirRecursiveAbsolute(_directory);
            GetViewport().GetTexture().GetImage().SavePng(view < 4 ? $"{_directory}/models_{view}.png" : $"{_directory}/equipment_{view - 4}.png");
            GD.Print($"[models] Saved view {view}");
        }

        _shot++;
    }

    private void Build(HospitalScene h)
    {
        var root = h.View3D.Root;
        // Models run up the screen's right-hand diagonal, three tiles apart; part kits are left out.
        var names = Models3D.All().Where(n => !Models3D.IsKit(n)).ToList();
        _count = names.Count;
        for (var i = 0; i < names.Count; i++)
        {
            var holder = new Node3D { Position = Kit3D.At(Centre.X + (i * 3), Centre.Y - (i * 3)) };
            var model = Models3D.Make(names[i])!;
            holder.AddChild(model);
            GD.Print($"[models] {names[i]}: {Models3D.Triangles(model)} triangles");
            root.AddChild(holder);
        }

        // A second row: the key equipment as the game builds it (stand-alone items, then the imaging and theatre sets).
        var groups = new Action<MeshBatch, MeshBatch, MeshBatch>[]
        {
            (b, g, _) => Furnish3D.Build(b, g, "obs_monitor", 0.8f, 0.8f, Kit3D.M(1.6), new Color("#4A545D"), 1),
            (b, g, _) => Furnish3D.Build(b, g, "drip_stand", 0.6f, 0.6f, Kit3D.M(1.8), new Color("#B5BEC6"), 1),
            (b, g, _) => Furnish3D.Build(b, g, "resus_trolley", 1.2f, 1.4f, Kit3D.M(1.0), new Color("#D84B3E"), 1),
            (b, g, _) =>
            {
                Carriers3D.Table(b, "xray_table", new Color("#DDE3E8"));
                b.Frame = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), new Vector3(-1.4f, 0, -0.6f));
                Furnish3D.Build(b, g, "xray_tube", 1.2f, 1.2f, Kit3D.M(2.1), new Color("#C9D3DB"), 1);
            },
            (b, g, _) =>
            {
                Carriers3D.Table(b, "ct_table", new Color("#E4E9ED"));
                b.Frame = new Transform3D(Basis.Identity, new Vector3(0, 0, -2.6f));
                Furnish3D.Build(b, g, "ct_gantry", 4.0f, 1.4f, Kit3D.M(1.9), new Color("#F1F4F6"), 1);
            },
            (b, _, glow) =>
            {
                Carriers3D.Table(b, "operating_table", new Color("#5E7A8C"));
                Props3D.SurgicalLights(b, glow);
            },
        };
        for (var i = 0; i < groups.Length; i++)
        {
            var holder = new Node3D { Position = Kit3D.At(KitRow.X + (i * KitStep), KitRow.Y - (i * KitStep)) };
            var (solid, glass, glow) = (new MeshBatch(), new MeshBatch(), new MeshBatch());
            groups[i](solid, glass, glow);
            solid.AddTo(holder);
            glass.AddTo(holder, alpha: true, shadow: false);
            if (glow.AddTo(holder, shadow: false) is { } lit)
            {
                lit.MaterialOverride = Kit3D.VertexColoursUnshadedAlpha;
            }

            root.AddChild(holder);
        }

        var span = 60;
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(span, 0.02f, span) },
            Position = Kit3D.At(Centre.X + 12, Centre.Y + 2),
            MaterialOverride = Kit3D.Mat(new Color("#D9D4C7")),
        });
    }
}
