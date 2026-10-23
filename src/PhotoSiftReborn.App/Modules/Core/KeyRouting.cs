using Avalonia.Input;
namespace PhotoSiftReborn;

internal static class KeyRouting
{
    public static char? RouteFor(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return (char)('A' + ((int)key - (int)Key.A));
        if (key >= Key.D0 && key <= Key.D9) return (char)('0' + ((int)key - (int)Key.D0));
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return (char)('0' + ((int)key - (int)Key.NumPad0));
        return null;
    }
}
