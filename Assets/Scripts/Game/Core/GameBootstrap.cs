using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Spawns the game into whatever scene is open, so pressing Play in an empty scene just works.</summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<GameManager>() != null) return;
            var go = new GameObject("Bad Apple Hotel");
            go.AddComponent<GameManager>();
            go.AddComponent<GameHUD>();
        }
    }
}
