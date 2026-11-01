using System;
using PiGame.Lobby;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace PiGame.Networking
{
    // O SDK cuida da eleição e do Relay; recuperamos só as regras da sala.
    // Personagens, votos e pronto recomeçam no lobby, sem reaproveitar ClientIds antigos.
    internal sealed class SessionLobbyMigrationData : IMigrationDataHandler
    {
        [Serializable]
        private class Data
        {
            public LobbyMatchSettingsData Settings;
        }

        private readonly Func<LobbyMatchSettingsData> _capture;
        private readonly Action<LobbyMatchSettingsData> _restore;

        public SessionLobbyMigrationData(Func<LobbyMatchSettingsData> capture,
            Action<LobbyMatchSettingsData> restore)
        {
            _capture = capture;
            _restore = restore;
        }

        public byte[] Generate()
        {
            return System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Data { Settings = _capture() }));
        }

        public void Apply(byte[] migrationData)
        {
            Data data = JsonUtility.FromJson<Data>(System.Text.Encoding.UTF8.GetString(migrationData));
            if (data == null || data.Settings.MinimumPlayers < 1 || data.Settings.DurationMinutes < 0)
                throw new InvalidOperationException("Configurações inválidas na recuperação da sala.");
            _restore(data.Settings);
        }
    }
}
