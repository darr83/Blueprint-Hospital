using BlueprintHospital.Simulation.Core;
using Godot;

namespace BlueprintHospital.Hospital;

/// <summary>
/// Fixed-angle orthographic camera. Middle mouse drags, the wheel zooms towards the
/// cursor, WASD/arrow keys pan, and every movement is smoothed.
/// </summary>
public partial class IsoCamera : Camera2D
{
    public const float MinZoom = 0.15f;
    public const float MaxZoom = 3.0f;
    private const float ZoomStep = 1.15f;
    private const float KeyboardPanPixelsPerSecond = 900f;
    private const float EdgeScrollMargin = 12f;
    private const float Smoothing = 14f;

    private Vector2 _targetPosition;
    private float _targetZoom = 1f;
    private bool _dragging;

    /// <summary>World-space area the camera centre is kept inside.</summary>
    public Rect2 Bounds { get; set; } = new(-4000, -1000, 8000, 6000);

    public float PanSpeedMultiplier { get; set; } = 1f;

    public bool EdgeScrolling { get; set; }

    public override void _Ready()
    {
        _targetPosition = Position;
        _targetZoom = Zoom.X;
        MakeCurrent();
    }

    /// <summary>Moves the camera immediately, without smoothing.</summary>
    public void SnapTo(Vector2 position, float zoom)
    {
        _targetZoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        _targetPosition = ClampToBounds(position);
        Position = _targetPosition;
        Zoom = new Vector2(_targetZoom, _targetZoom);
    }

    public CameraState CaptureState() => new(_targetPosition.X, _targetPosition.Y, _targetZoom);

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _dragging = middle.Pressed;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ZoomTowardsCursor(ZoomStep);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ZoomTowardsCursor(1f / ZoomStep);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion motion when _dragging:
                // Dragging moves the world with the mouse, so move the camera the opposite way.
                _targetPosition = ClampToBounds(_targetPosition - (motion.Relative / Zoom.X));
                Position = ClampToBounds(Position - (motion.Relative / Zoom.X));
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Process(double delta)
    {
        var direction = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
        {
            direction.X -= 1;
        }

        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
        {
            direction.X += 1;
        }

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
        {
            direction.Y -= 1;
        }

        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
        {
            direction.Y += 1;
        }

        if (EdgeScrolling && !_dragging)
        {
            direction += EdgeDirection();
        }

        if (direction != Vector2.Zero)
        {
            var speed = KeyboardPanPixelsPerSecond * PanSpeedMultiplier / _targetZoom;
            _targetPosition = ClampToBounds(_targetPosition + (direction.Normalized() * speed * (float)delta));
        }

        var weight = 1f - Mathf.Exp(-Smoothing * (float)delta);
        Position = Position.Lerp(_targetPosition, weight);
        var zoom = Mathf.Lerp(Zoom.X, _targetZoom, weight);
        Zoom = new Vector2(zoom, zoom);
    }

    private void ZoomTowardsCursor(float factor)
    {
        var newZoom = Mathf.Clamp(_targetZoom * factor, MinZoom, MaxZoom);
        if (Mathf.IsEqualApprox(newZoom, _targetZoom))
        {
            return;
        }

        // Keep the world point under the cursor fixed while zooming.
        var mouseOffset = GetViewport().GetMousePosition() - (GetViewportRect().Size / 2f);
        var worldUnderMouse = _targetPosition + (mouseOffset / _targetZoom);
        _targetZoom = newZoom;
        _targetPosition = ClampToBounds(worldUnderMouse - (mouseOffset / _targetZoom));
    }

    private Vector2 EdgeDirection()
    {
        var size = GetViewportRect().Size;
        var mouse = GetViewport().GetMousePosition();
        var dir = Vector2.Zero;
        if (mouse.X <= EdgeScrollMargin)
        {
            dir.X -= 1;
        }
        else if (mouse.X >= size.X - EdgeScrollMargin)
        {
            dir.X += 1;
        }

        if (mouse.Y <= EdgeScrollMargin)
        {
            dir.Y -= 1;
        }
        else if (mouse.Y >= size.Y - EdgeScrollMargin)
        {
            dir.Y += 1;
        }

        return dir;
    }

    private Vector2 ClampToBounds(Vector2 p) =>
        new(Mathf.Clamp(p.X, Bounds.Position.X, Bounds.End.X), Mathf.Clamp(p.Y, Bounds.Position.Y, Bounds.End.Y));
}
