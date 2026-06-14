using Godot;

namespace RefreshShop;

internal static class RefreshShopLog
{
    internal static void Info(string message)
    {
        GD.Print($"[RefreshShop] {message}");
    }

    internal static void Warn(string message)
    {
        GD.PushWarning($"[RefreshShop] {message}");
    }

    internal static void Error(string message)
    {
        GD.PushError($"[RefreshShop] {message}");
    }
}