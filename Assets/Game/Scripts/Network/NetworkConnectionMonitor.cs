using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Networking
{
    public enum ConnectionQuality : byte { Unknown, Poor, Fair, Good }

    // O host mede as conexões reais do transporte. O RTT não identifica sozinho a origem de uma falha.
    public class NetworkConnectionMonitor : NetworkBehaviour
    {
        [SerializeField, Min(0.5f)] private float _sampleInterval = 1f;
        [SerializeField, Min(1)] private int _fairPingMs = 100;
        [SerializeField, Min(1)] private int _poorPingMs = 200;
        [SerializeField, Min(1)] private int _unstableVariationMs = 60;
        [SerializeField, Min(1f)] private float _alertDelaySeconds = 3f;

        private struct Sample : INetworkSerializable, IEquatable<Sample>
        {
            public ulong ClientId;
            public int PingMs;
            public ConnectionQuality Quality;
            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref ClientId);
                serializer.SerializeValue(ref PingMs);
                serializer.SerializeValue(ref Quality);
            }
            public bool Equals(Sample other) => ClientId == other.ClientId
                && PingMs == other.PingMs && Quality == other.Quality;
        }

        private class PeerHistory
        {
            public float Average;
            public float Variation;
            public int Previous;
            public float PoorSince = -1f;
        }

        private NetworkList<Sample> _samples;
        private readonly NetworkVariable<bool> _hostUnstable = new();
        private readonly Dictionary<ulong, PeerHistory> _history = new();
        private float _nextSample;

        public static NetworkConnectionMonitor Instance { get; private set; }
        public bool HostUnstable => _hostUnstable.Value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake() => _samples = new NetworkList<Sample>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _nextSample = 0f;
            _history.Clear();
            if (IsServer)
            {
                _samples.Clear();
                _hostUnstable.Value = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            _history.Clear();
        }

        public bool TryGetConnection(ulong clientId, out int pingMs, out ConnectionQuality quality)
        {
            foreach (Sample sample in _samples)
                if (sample.ClientId == clientId)
                {
                    pingMs = sample.PingMs;
                    quality = sample.Quality;
                    return true;
                }
            pingMs = -1;
            quality = ConnectionQuality.Unknown;
            return false;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + _sampleInterval;
            var clients = NetworkManager.ConnectedClientsIds;
            for (int i = _samples.Count - 1; i >= 0; i--)
                if (!ContainsClient(_samples[i].ClientId))
                {
                    _history.Remove(_samples[i].ClientId);
                    _samples.RemoveAt(i);
                }

            int measured = 0, poor = 0;
            foreach (ulong clientId in clients)
            {
                if (clientId == Unity.Netcode.NetworkManager.ServerClientId) continue;
                int raw = (int)NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId);
                var sample = new Sample { ClientId = clientId, PingMs = -1, Quality = ConnectionQuality.Unknown };
                if (raw > 0)
                {
                    if (!_history.TryGetValue(clientId, out PeerHistory history))
                    {
                        history = new PeerHistory { Average = raw, Previous = raw };
                        _history.Add(clientId, history);
                    }
                    history.Average = Mathf.Lerp(history.Average, raw, 0.35f);
                    history.Variation = Mathf.Lerp(history.Variation, Mathf.Abs(raw - history.Previous), 0.35f);
                    history.Previous = raw;
                    bool unstable = history.Average >= _poorPingMs || history.Variation >= _unstableVariationMs;
                    if (!unstable) history.PoorSince = -1f;
                    else if (history.PoorSince < 0f) history.PoorSince = Time.unscaledTime;
                    sample.PingMs = Mathf.RoundToInt(history.Average);
                    sample.Quality = unstable && Time.unscaledTime - history.PoorSince >= _alertDelaySeconds
                        ? ConnectionQuality.Poor
                        : history.Average >= _fairPingMs || unstable ? ConnectionQuality.Fair : ConnectionQuality.Good;
                    measured++;
                    if (sample.Quality == ConnectionQuality.Poor) poor++;
                }
                else _history.Remove(clientId);

                int index = -1;
                for (int i = 0; i < _samples.Count; i++)
                    if (_samples[i].ClientId == clientId) { index = i; break; }
                if (index < 0) _samples.Add(sample);
                else if (!_samples[index].Equals(sample)) _samples[index] = sample;
            }
            // Indício de instabilidade nas conexões da sala, sem atribuir a causa à internet do host.
            _hostUnstable.Value = measured > 0 && measured == clients.Count - 1 && poor == measured;
        }

        private bool ContainsClient(ulong clientId)
        {
            foreach (ulong connected in NetworkManager.ConnectedClientsIds)
                if (connected == clientId) return true;
            return false;
        }
    }
}
