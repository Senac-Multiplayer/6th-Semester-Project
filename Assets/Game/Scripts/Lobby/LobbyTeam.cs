using UnityEngine;

namespace PiGame.Lobby
{
    public enum LobbyTeam : byte { None, Blue, Orange }

    public static class LobbyTeams
    {
        public const int TeamSize = 2;
        public const int PlayerCount = 4;
        public static bool IsValid(LobbyTeam team) => team == LobbyTeam.Blue || team == LobbyTeam.Orange;
        public static string Name(LobbyTeam team) => team == LobbyTeam.Blue ? "AZUL"
            : team == LobbyTeam.Orange ? "LARANJA" : "SEM EQUIPE";
        public static Color Color(LobbyTeam team) => team == LobbyTeam.Blue
            ? new Color32(89, 191, 255, 255) : team == LobbyTeam.Orange
            ? new Color32(255, 173, 84, 255) : UnityEngine.Color.white;
        public static string Html(LobbyTeam team) => ColorUtility.ToHtmlStringRGB(Color(team));
    }
}
