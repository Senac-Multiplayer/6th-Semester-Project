using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PiGame.Sound
{
    public enum SFXList { WallJump, PlayerJump, Hit, DeathHit, DrawRoulette, VolumeScroll, LevelSelect, UiButton, DownPlatform, UpPlatform, WhirlPlatform,
    StuckNail, ShootNail, IrrigadorPull, IrrigadorSkill, DeserticoRoll, DeserticoShot, DeserticoEmpty, DeserticoReload, RebeldeBounce, RebeldeCatch, RebeldeEmpty, RebeldeSkill,
    RebeldeShot, FumaceiroEmpty, FumaceiroCatch, FumaceiroThrow, FumaceiroSkill, PlayerRespawn, CharacterBrowse, ReadyBanner, MatchStart
        }
    public enum OSTList { Battle_Desertico, Battle_Fumaceiro, Battle_Irrigador, Battle_Rebelde, Battle_Vitoria, Menu_Titulo, Menu_Screens }

    public class NetworkSoundManager : NetworkBehaviour
    {
        public static NetworkSoundManager Instance { get; private set; }

        [System.Serializable]
        public struct SoundData
        {
            public SFXList sfxType;
            public AudioClip clip;

            [Min(0f)] public float fadeInSeconds;
            [Min(0f)] public float fadeOutSeconds;
        }

        [System.Serializable]
        public struct MusicData
        {
            public OSTList ostType;
            public AudioClip clip;

            [Min(0f)] public float fadeInSeconds;
            [Min(0f)] public float fadeOutSeconds;
        }

        [Header("Componentes de �udio")]
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource ostSource;

        [Header("Listas de �udio (Configure no Inspector)")]
        [SerializeField] private List<SoundData> sfxList = new();
        [SerializeField] private List<MusicData> ostList = new();

        private Dictionary<SFXList, SoundData> _sfxDict = new();
        private Dictionary<OSTList, MusicData> _ostDict = new();

        private SFXList? _pendingUiEffect;
        private Coroutine _ostFadeRoutine;
        private OSTList? _requestedOst;
        private float _ostTargetVolume;
        private bool _ostTransitioning;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            RegisterMixer();
            if (sfxSource) sfxSource.volume = 1f;

            foreach (var item in sfxList)
            {
                if (!_sfxDict.ContainsKey(item.sfxType))
                    _sfxDict.Add(item.sfxType, item);
            }

            foreach (var item in ostList)
            {
                if (!_ostDict.ContainsKey(item.ostType))
                    _ostDict.Add(item.ostType, item);
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            RegisterMixer();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void RegisterMixer()
        {
            GameAudioSettings.UseMixer(ostSource != null
                ? ostSource.outputAudioMixerGroup?.audioMixer : null);
        }

        public void PlaySFX(SFXList sfx, float volume = 0.2f)
        {
            if (Instance == null) return;
            Instance.SendSFXServerRpc(sfx, Mathf.Clamp01(volume));
        }

        public void LocalSFX(SFXList sfx, float volume = 0.2f)
        {
            if(Instance == null) return;
            Instance.PlaySFXLocal(sfx, Mathf.Clamp01(volume));
        }

        public void PlayOST(OSTList ost, float volume = 0.3f)
        {
            if (Instance == null) return;
            Instance.SendOSTServerRpc(ost, Mathf.Clamp01(volume));
        }

        public void LocalOST(OSTList ost, float volume = 0.3f)
        {
            if(Instance == null) return;
            Instance.PlayOSTLocal(ost, Mathf.Clamp01(volume));
        }

        // O botão e seu atalho podem disparar no mesmo frame. Um único efeito local é suficiente.
        public void LocalUISFX(SFXList effect)
        {
            if (!_pendingUiEffect.HasValue || UiPriority(effect) > UiPriority(_pendingUiEffect.Value))
                _pendingUiEffect = effect;
        }

        private static int UiPriority(SFXList effect) => effect == SFXList.MatchStart ? 4
            : effect == SFXList.ReadyBanner ? 3 : (effect == SFXList.CharacterBrowse || effect == SFXList.LevelSelect) ? 2
            : effect == SFXList.UiButton ? 1 : 0;

        private void LateUpdate()
        {
            if (!_pendingUiEffect.HasValue) return;
            var effect = _pendingUiEffect.Value;
            _pendingUiEffect = null;
            float volume = effect == SFXList.ReadyBanner ? 0.75f
                : effect == SFXList.VolumeScroll ? 0.3f : 0.2f;
            PlaySFXLocal(effect, volume);
        }

        public void StopLocalOST(float fadeOutSeconds = 0.15f)
        {
            if (ostSource == null || (_requestedOst == null && _ostTransitioning)) return;
            if (_ostFadeRoutine != null) StopCoroutine(_ostFadeRoutine);
            _requestedOst = null;
            _ostTransitioning = true;
            _ostFadeRoutine = StartCoroutine(StopOSTWithFade(Mathf.Max(0f, fadeOutSeconds)));
        }

        private IEnumerator StopOSTWithFade(float seconds)
        {
            yield return FadeVolume(ostSource, ostSource.volume, 0f, seconds);
            ostSource.Stop();
            ostSource.clip = null;
            _ostTransitioning = false;
            _ostFadeRoutine = null;
        }

        #region RPC do SFX
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SendSFXServerRpc(SFXList sfx, float volume)
        {
            SendSFXClientRpc(sfx, volume);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SendSFXClientRpc(SFXList sfx, float volume)
        {
            PlaySFXLocal(sfx, volume);
        }
        #endregion

        #region Rpc do OST
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SendOSTServerRpc(OSTList ost, float volume)
        {
            SendOSTClientRpc(ost, volume);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SendOSTClientRpc(OSTList ost, float volume)
        {
            PlayOSTLocal(ost, volume);
        }
        #endregion

        #region Execu��o Local
        private void PlaySFXLocal(SFXList sfx, float volume)
        {
            if (_sfxDict.TryGetValue(sfx, out SoundData sound))
            {
                if (!sound.clip || !sfxSource) return;
                if (sound.fadeInSeconds <= 0f && sound.fadeOutSeconds <= 0f)
                    sfxSource.PlayOneShot(sound.clip, volume);
                else
                {
                    // Cada fade usa sua própria voz; não altera os efeitos já em reprodução.
                    var voice = gameObject.AddComponent<AudioSource>();
                    voice.playOnAwake = false;
                    voice.outputAudioMixerGroup = sfxSource.outputAudioMixerGroup;
                    voice.spatialBlend = sfxSource.spatialBlend;
                    voice.pitch = sfxSource.pitch;
                    StartCoroutine(PlaySFXWithFade(sound, volume, voice));
                }
            }
            else
            {
                Debug.LogWarning($"O SFX '{sfx}' n�o foi configurado nas listas do SoundManager!");
            }
        }

        private void PlayOSTLocal(OSTList ost, float volume)
        {
            if (ost == OSTList.Battle_Vitoria) volume = Mathf.Clamp01(volume * 1.35f);
            if (_ostDict.TryGetValue(ost, out MusicData music))
            {
                if (music.clip == null || ostSource == null) return;
                _ostTargetVolume = volume;
                // Repetir o contexto não reinicia a faixa, o fade ou um jingle já encerrado.
                if (_requestedOst == ost && (_ostTransitioning
                    || (ostSource.clip == music.clip && (ostSource.isPlaying || !ostSource.loop))))
                {
                    if (!_ostTransitioning && ostSource.isPlaying) ostSource.volume = volume;
                    return;
                }
                if (_ostFadeRoutine != null) StopCoroutine(_ostFadeRoutine);
                _requestedOst = ost;
                _ostTransitioning = true;
                _ostFadeRoutine = StartCoroutine(PlayOSTWithFade(music, volume, ost != OSTList.Battle_Vitoria));
            }
            else
            {
                Debug.LogWarning($"A OST '{ost}' n�o foi configurada nas listas do SoundManager!");
            }
        }

        private IEnumerator PlaySFXWithFade(SoundData sound, float targetVolume, AudioSource voice)
        {
            voice.clip = sound.clip;
            voice.volume = 0f;
            voice.Play();
            yield return FadeVolume(voice, 0f, targetVolume, sound.fadeInSeconds);
            float fadeStart = Mathf.Max(sound.fadeInSeconds, sound.clip.length - sound.fadeOutSeconds);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, fadeStart - sound.fadeInSeconds));
            yield return FadeVolume(voice, targetVolume, 0f, sound.fadeOutSeconds);
            Destroy(voice);
        }

        private IEnumerator PlayOSTWithFade(MusicData music, float targetVolume, bool loop)
        {
            if (music.clip == null)
            {
                _ostFadeRoutine = null;
                _ostTransitioning = false;
                yield break;
            }

            if(ostSource.isPlaying)
            {
                yield return FadeVolume(ostSource, ostSource.volume, 0f, music.fadeOutSeconds);

                ostSource.Stop();
            }

            ostSource.clip = music.clip;
            ostSource.loop = loop;
            ostSource.volume = 0f;
            ostSource.Play();

            yield return FadeVolume(ostSource, 0f, targetVolume, music.fadeInSeconds);
            ostSource.volume = _ostTargetVolume;
            _ostTransitioning = false;
            _ostFadeRoutine = null;
        }

        private IEnumerator FadeVolume(AudioSource source, float startVolume, float targetVolume, float duration)
        {
            if(duration <= 0f)
            {
                source.volume = targetVolume;
                yield break;
            }

            float elapsed = 0f;
            source.volume = startVolume;

            while(elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float time = Mathf.Clamp01(elapsed / duration);

                source.volume = Mathf.Lerp(startVolume, targetVolume, time);

                yield return null;
            }
            
            source.volume = targetVolume;
        }

        #endregion
        
    }
}
