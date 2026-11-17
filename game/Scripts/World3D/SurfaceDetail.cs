using System;
using BlueprintHospital.Core;
using BlueprintHospital.Simulation.Hospital;
using Godot;

namespace BlueprintHospital.World3D;

/// <summary>What a surface is made of: picks the detail texture laid over its colour (see <see cref="SurfaceDetail"/>).</summary>
public enum SurfaceKind
{
    Plain = 0,
    Paint = 1,
    Wood = 2,
    Fabric = 3,
    Metal = 4,
    Vinyl = 5,
    Ceiling = 6,
    Laminate = 7,
    Rubber = 8,
    Glaze = 9,
    Carpet = 10,
    Terrazzo = 11,
    Safety = 12,
    Ceramic = 13,
}

/// <summary>
/// Surface detail: a fine texture for each kind of surface (painted plaster, sealed vinyl, wood grain,
/// fabric weave, brushed metal...) that multiplies the colour the game already paints, so tints,
/// uniforms and the Decorate finishes all keep working. The textures are made by
/// tools/Textures/make_surfaces.py; each holds shade (R), roughness (G) and height (B), with a normal map
/// beside it. Batched meshes carry their kind in UV.x, so a room of furniture is still one draw call.
/// Turned off in Settings (Surface textures), everything is drawn as before.
/// </summary>
public static class SurfaceDetail
{
    public const string Folder = "res://Art/Surfaces";

    private static readonly string[] Names =
    {
        "0_plain", "1_paint", "2_wood", "3_fabric", "4_metal", "5_vinyl", "6_ceiling", "7_laminate", "8_rubber", "9_glaze",
        "10_carpet", "11_terrazzo", "12_safety", "13_ceramic",
    };

    /// <summary>How many kinds there are (layers in the texture arrays).</summary>
    public static int Count => Names.Length;

    /// <summary>Whether surfaces are drawn with their detail (the Surface textures setting).</summary>
    public static bool Enabled => GameManager.Instance?.Settings.SurfaceTextures ?? true;

    private static Texture2DArray? _detail;
    private static Texture2DArray? _normals;
    private static ShaderMaterial? _batch;

    /// <summary>How many of the surface texture files could not be found (for the smoke test).</summary>
    public static int Missing { get; private set; }

    private static Texture2DArray Array(string suffix)
    {
        var images = new Godot.Collections.Array<Image>();
        foreach (var name in Names)
        {
            var path = $"{Folder}/{name}_{suffix}.png";
            var image = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path).GetImage() : null;
            if (image is null || image.IsEmpty())
            {
                GD.PushWarning($"Surface texture missing: {path}");
                Missing++;
                image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
                image.Fill(suffix == "normal" ? new Color(0.5f, 0.5f, 1f) : new Color(0.5f, 0.85f, 0.5f));
            }

            if (image.IsCompressed())
            {
                image.Decompress();
            }

            image.ClearMipmaps();
            image.Convert(Image.Format.Rgba8);
            if (images.Count > 0 && image.GetSize() != images[0].GetSize())
            {
                image.Resize(images[0].GetWidth(), images[0].GetHeight());
            }

            image.GenerateMipmaps();
            images.Add(image);
        }

        var array = new Texture2DArray();
        array.CreateFromImages(images);
        return array;
    }

    public static Texture2DArray Detail => _detail ??= Array("detail");

    public static Texture2DArray Normals => _normals ??= Array("normal");

    /// <summary>The material for vertex-coloured batches (walls, furniture, equipment, people).</summary>
    public static Material Batch
    {
        get
        {
            if (!Enabled)
            {
                return Kit3D.VertexColours;
            }

            if (_batch is null)
            {
                _batch = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/surface.gdshader") };
                _batch.SetShaderParameter("detail", Detail);
                _batch.SetShaderParameter("normals", Normals);
            }

            return _batch;
        }
    }

    /// <summary>A material for the painted floor texture, with each tile's finish read from <paramref name="finishes"/>.</summary>
    public static ShaderMaterial Floor()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/floor.gdshader") };
        m.SetShaderParameter("detail", Detail);
        m.SetShaderParameter("normals", Normals);
        return m;
    }

    /// <summary>A material for the wall-finish and picture atlas: each quad's kind is in UV2.x.</summary>
    public static ShaderMaterial Atlas()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/atlas.gdshader") };
        m.SetShaderParameter("detail", Detail);
        m.SetShaderParameter("normals", Normals);
        return m;
    }

    /// <summary>The surface a floor finish is laid with (Standard rooms and corridors are sealed vinyl).</summary>
    public static SurfaceKind Of(FloorFinish finish) => finish switch
    {
        FloorFinish.SafetyVinyl => SurfaceKind.Safety,
        FloorFinish.CeramicTiles or FloorFinish.Chequer => SurfaceKind.Ceramic,
        FloorFinish.WoodPlanks => SurfaceKind.Wood,
        FloorFinish.Terrazzo => SurfaceKind.Terrazzo,
        FloorFinish.CarpetTiles => SurfaceKind.Carpet,
        _ => SurfaceKind.Vinyl,
    };

    /// <summary>The surface a wall finish is made of (wood panelling is mostly paint above a dado, so it stays paint).</summary>
    public static SurfaceKind Of(WallFinish finish) => finish switch
    {
        WallFinish.Tiles => SurfaceKind.Glaze,
        _ => SurfaceKind.Paint,
    };

    /// <summary>What a Blender material is made of, from words in its name ("Tint_fabric", "Chrome"...); null if it doesn't say.</summary>
    public static SurfaceKind? FromMaterialName(string name)
    {
        name = name.ToLowerInvariant();
        foreach (var (word, kind) in Words)
        {
            if (name.Contains(word, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return null;
    }

    private static readonly (string Word, SurfaceKind Kind)[] Words =
    {
        ("fabric", SurfaceKind.Fabric), ("cushion", SurfaceKind.Fabric), ("mattress", SurfaceKind.Fabric), ("blanket", SurfaceKind.Fabric),
        ("wood", SurfaceKind.Wood), ("chrome", SurfaceKind.Metal), ("steel", SurfaceKind.Metal), ("metal", SurfaceKind.Metal),
        ("rubber", SurfaceKind.Rubber), ("tyre", SurfaceKind.Rubber), ("laminate", SurfaceKind.Laminate), ("plastic", SurfaceKind.Laminate),
        ("ceramic", SurfaceKind.Glaze), ("glaze", SurfaceKind.Glaze),
    };
}
