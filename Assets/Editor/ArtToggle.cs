using UnityEditor;
using BadAppleHotel.Game;

namespace BadAppleHotel.EditorTools
{
    /// <summary>Menu switch between the Autosprite art and the code-drawn placeholder sprites.</summary>
    public static class ArtToggle
    {
        const string Menu = "Bad Apple Hotel/Use Autosprite Art";

        [MenuItem(Menu, false, 100)]
        static void Toggle() => Sprites.UseArt = !Sprites.UseArt;

        [MenuItem(Menu, true)]
        static bool Validate()
        {
            UnityEditor.Menu.SetChecked(Menu, Sprites.UseArt);
            return true;
        }
    }
}
