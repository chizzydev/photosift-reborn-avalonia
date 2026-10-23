namespace PhotoSiftReborn;

// View-only preview geometry. Zoom values refer to the *bounded preview*,
// not to the original photograph's pixels. Never interpret as full-res 1:1.
internal readonly record struct InspectionLayout(
    double CanvasWidth, double CanvasHeight,
    double ImageWidth, double ImageHeight,
    double OffsetX, double OffsetY, int RotationDegrees);

internal static class InspectionGeometry
{
    internal static InspectionLayout Calculate(int width, int height, int quarterTurns, double zoom)
    {
        if (width <= 0 || height <= 0 || !double.IsFinite(zoom) || zoom < 0.25 || zoom > 4)
            throw new ArgumentOutOfRangeException(nameof(zoom), "Invalid preview geometry or zoom.");
        int turn = ((quarterTurns % 4) + 4) % 4;
        double w = width * zoom;
        double h = height * zoom;
        return turn switch
        {
            0 => new(w, h, w, h, 0, 0, 0),
            1 => new(h, w, w, h, h, 0, 90),
            2 => new(w, h, w, h, w, h, 180),
            _ => new(h, w, w, h, 0, w, 270)
        };
    }
}
