using UnityEngine;
using UnityEngine.InputSystem;

namespace PiGame.Input
{
    public static class GamepadSettings
    {
        private const string DeadzoneKey = "PiGame.GamepadDeadzone";

        public static float Deadzone => Mathf.Clamp(PlayerPrefs.GetFloat(DeadzoneKey, 0.125f), 0f, 0.5f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            InputSystem.settings.defaultDeadzoneMin = Deadzone;
        }

        public static void SetDeadzone(float value)
        {
            value = Mathf.Clamp(value, 0f, 0.5f);
            InputSystem.settings.defaultDeadzoneMin = value;
            PlayerPrefs.SetFloat(DeadzoneKey, value);
            PlayerPrefs.Save();
        }
    }
}
