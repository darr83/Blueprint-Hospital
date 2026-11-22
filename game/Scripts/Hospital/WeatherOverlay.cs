using System;
using Godot;
using Sky = BlueprintHospital.Simulation.Data.Models.Sky;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Rain, snow, fog and lightning over the screen, the same in 2D and 3D: drops slant in the wind, flakes
/// drift, fog is a pale wash that lifts towards midday and a thunderstorm now and then lights the sky.
/// Drawn between the hospital and the HUD, and only while it is raining, snowing or foggy (see
/// <see cref="WeatherLook"/>). Falls in real time, so it stops when the game is paused.
/// </summary>
public partial class WeatherOverlay : CanvasLayer
{
    private const int MaxDrops = 320;
    private const int MaxFlakes = 260;

    private readonly Vector2[] _drops = new Vector2[MaxDrops];
    private readonly float[] _dropSpeed = new float[MaxDrops];
    private readonly Vector2[] _flakes = new Vector2[MaxFlakes];
    private readonly float[] _flakeSize = new float[MaxFlakes];
    private readonly Random _random = new(1234);
    private Painter _painter = null!;
    private float _flash;
    private double _nextFlash = 6;
    private double _time;

    /// <summary>Whether the game clock is paused (then nothing falls).</summary>
    public Func<bool>? Paused { get; set; }

    /// <summary>How dark it is outside, 0 by day to 1 at night (drops and fog are dimmer at night).</summary>
    public Func<float>? Night { get; set; }

    /// <summary>What is showing, for checks: "rain", "snow", "fog" or "" (several joined with "+").</summary>
    public string Showing { get; private set; } = string.Empty;

    public override void _Ready()
    {
        Layer = 1;
        _painter = new Painter(this) { Name = "Weather", MouseFilter = Control.MouseFilterEnum.Ignore };
        _painter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_painter);
        for (var i = 0; i < MaxDrops; i++)
        {
            _drops[i] = new Vector2(_random.NextSingle(), _random.NextSingle());
            _dropSpeed[i] = 0.8f + (_random.NextSingle() * 0.5f);
        }

        for (var i = 0; i < MaxFlakes; i++)
        {
            _flakes[i] = new Vector2(_random.NextSingle(), _random.NextSingle());
            _flakeSize[i] = 1.4f + (_random.NextSingle() * 2.2f);
        }
    }

    public override void _Process(double delta)
    {
        var rain = WeatherLook.Rain;
        var snow = WeatherLook.Snowfall;
        var fog = WeatherLook.Fog;
        var parts = new System.Collections.Generic.List<string>(3);
        if (rain > 0.02f)
        {
            parts.Add("rain");
        }

        if (snow > 0.02f)
        {
            parts.Add("snow");
        }

        if (fog > 0.02f)
        {
            parts.Add("fog");
        }

        Showing = string.Join("+", parts);
        _painter.Visible = Visible && (parts.Count > 0 || _flash > 0);
        if (!_painter.Visible)
        {
            return;
        }

        if (Paused?.Invoke() != true)
        {
            var dt = (float)delta;
            _time += delta;
            for (var i = 0; i < MaxDrops; i++)
            {
                // Drops cross the screen in about a third of a second, a little across with the wind.
                _drops[i] += new Vector2(0.35f, 2.6f) * _dropSpeed[i] * dt;
                if (_drops[i].Y > 1.05f)
                {
                    _drops[i] = new Vector2(_random.NextSingle() * 1.2f - 0.2f, -0.05f);
                }
            }

            for (var i = 0; i < MaxFlakes; i++)
            {
                var sway = Mathf.Sin((float)(_time * 1.3) + (i * 0.7f)) * 0.03f;
                _flakes[i] += new Vector2(0.02f + sway, 0.12f * (0.6f + (_flakeSize[i] * 0.15f))) * dt;
                if (_flakes[i].Y > 1.02f)
                {
                    _flakes[i] = new Vector2(_random.NextSingle() * 1.1f - 0.05f, -0.02f);
                }
            }

            // Now and then a thunderstorm lights up the sky.
            _flash = Math.Max(0, _flash - (dt * 3.5f));
            if (WeatherLook.Today?.Sky == Sky.Thunderstorm && rain > 0.6f)
            {
                _nextFlash -= delta;
                if (_nextFlash <= 0)
                {
                    _flash = 1;
                    _nextFlash = 7 + (_random.NextDouble() * 14);
                }
            }
        }

        _painter.QueueRedraw();
    }

    private void DrawWeather(Control canvas)
    {
        var size = canvas.Size;
        var night = Night?.Invoke() ?? 0;
        var fog = WeatherLook.Fog;
        if (fog > 0.02f)
        {
            var wash = new Color(0.86f, 0.89f, 0.91f).Lerp(new Color(0.35f, 0.4f, 0.48f), night * 0.8f);
            canvas.DrawRect(new Rect2(Vector2.Zero, size), new Color(wash, 0.42f * fog));
        }

        var rain = WeatherLook.Rain;
        if (rain > 0.02f)
        {
            var count = (int)(MaxDrops * Math.Min(1f, rain));
            var colour = new Color(0.72f, 0.8f, 0.9f, 0.5f + (0.15f * rain)).Lerp(new Color(0.55f, 0.62f, 0.75f, 0.4f), night * 0.6f);
            var length = 16f + (10f * rain);
            for (var i = 0; i < count; i++)
            {
                var p = _drops[i] * size;
                canvas.DrawLine(p, p + new Vector2(length * 0.13f, length), colour, 1.6f, true);
            }
        }

        var snow = WeatherLook.Snowfall;
        if (snow > 0.02f)
        {
            var count = (int)(MaxFlakes * Math.Min(1f, snow));
            var colour = new Color(1f, 1f, 1f, 0.85f).Lerp(new Color(0.78f, 0.83f, 0.92f, 0.75f), night * 0.6f);
            for (var i = 0; i < count; i++)
            {
                canvas.DrawCircle(_flakes[i] * size, _flakeSize[i], colour);
            }
        }

        if (_flash > 0)
        {
            canvas.DrawRect(new Rect2(Vector2.Zero, size), new Color(0.95f, 0.97f, 1f, 0.32f * _flash * _flash));
        }
    }

    private sealed partial class Painter : Control
    {
        private readonly WeatherOverlay _owner;

        public Painter(WeatherOverlay owner) => _owner = owner;

        public Painter() => _owner = null!;

        public override void _Draw() => _owner?.DrawWeather(this);
    }
}
