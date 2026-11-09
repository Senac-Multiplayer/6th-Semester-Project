using UnityEngine;

namespace PiGame.UI
{
    // Preferências locais, independentes das configurações sincronizadas da partida.
    public static class GameDisplaySettings
    {
        public static bool ShowPing
        {
            get => PlayerPrefs.GetInt("Display.ShowPing", 1) != 0;
            set { PlayerPrefs.SetInt("Display.ShowPing", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt("Display.ShowFps", 0) != 0;
            set { PlayerPrefs.SetInt("Display.ShowFps", value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}
