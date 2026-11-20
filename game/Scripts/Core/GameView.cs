namespace BlueprintHospital.Core;

/// <summary>
/// Which of the two ways of drawing the hospital the game is using: 2D from directly above, or 3D.
/// The player picks one on the main menu, and it cannot change while a game is open, so the two
/// views never have to keep in step with each other.
/// </summary>
public static class GameView
{
    /// <summary>True when the hospital is drawn in 3D; false when it is drawn in 2D from above.</summary>
    public static bool ThreeD => GameManager.Instance?.Settings.View3D ?? false;
}
