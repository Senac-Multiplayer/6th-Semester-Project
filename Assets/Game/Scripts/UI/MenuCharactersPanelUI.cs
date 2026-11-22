using System;
using PiGame.Lobby;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PiGame.UI
{
    public sealed class MenuCharactersPanelUI : MonoBehaviour, IMoveHandler
    {
        [Serializable]
        private sealed class CharacterPreview
        {
            public LobbyCharacterDefinition Character;
            public string Name;
            public Color AccentColor = Color.white;
            [TextArea(2, 5)] public string Story;
            [TextArea(1, 3)] public string Attack;
            [TextArea(1, 3)] public string Ability;
            [TextArea(1, 3)] public string Ammo;
            public Sprite AbilityIcon;
            [Tooltip("Ajuste do retrato neste guia, sem alterar o seletor do lobby.")]
            public Vector2 PortraitOffset;
            [Tooltip("Opcional. Sem vídeo, a área de demonstração permanece vazia.")]
            public VideoClip Video;
        }

        [SerializeField] private GameObject _root;
        [SerializeField] private Text _name, _story, _attack, _ability, _ammo, _counter;
        [SerializeField] private Image _portrait, _abilityIcon;
        [SerializeField] private Button _previous, _next, _back;
        [SerializeField] private RawImage _videoScreen;
        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private CharacterPreview[] _characters;
        private RenderTexture _videoTexture;
        private int _index;
        private int _backFrame = -1;
        private Vector2 _portraitPosition;

        public bool IsVisible => _root && _root.activeInHierarchy;
        public event Action Closed;

        private void Awake()
        {
            if (!_root || !_name || !_story || !_attack || !_ability || !_ammo || !_counter
                || !_portrait || !_abilityIcon || !_previous || !_next || !_back
                || !_videoScreen || !_videoPlayer || _characters == null || _characters.Length == 0
                || Array.Exists(_characters, entry => entry == null || !entry.Character))
            {
                Debug.LogError("Configure as referências e personagens do guia no prefab.", this);
                enabled = false;
                return;
            }
            _previous.onClick.AddListener(Previous);
            _portraitPosition = _portrait.rectTransform.anchoredPosition;
            _next.onClick.AddListener(Next);
            _back.onClick.AddListener(HandleBack);
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = true;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.prepareCompleted += VideoPrepared;
            _videoPlayer.errorReceived += VideoFailed;
        }

        public void Show(LobbyCharacterId initial = LobbyCharacterId.None)
        {
            if (!enabled) return;
            _index = Array.FindIndex(_characters, entry => entry.Character.Id == initial);
            if (_index < 0) _index = 0;
            _root.SetActive(true);
            Refresh();
            EventSystem.current?.SetSelectedGameObject(_back.gameObject);
        }

        public void Hide()
        {
            StopVideo();
            if (_root) _root.SetActive(false);
        }

        public void HandleBack()
        {
            if (!IsVisible || _backFrame == Time.frameCount) return;
            _backFrame = Time.frameCount;
            PiGame.Sound.UiAudioManager.PlayButton();
            Closed?.Invoke();
        }

        public void OnMove(AxisEventData eventData)
        {
            if (!IsVisible) return;
            if (eventData.moveDir == MoveDirection.Up) Previous();
            else if (eventData.moveDir == MoveDirection.Down) Next();
            else EventSystem.current?.SetSelectedGameObject(_back.gameObject);
            eventData.Use();
        }

        // Botões encaminham Move para manter a navegação após um clique com o mouse.
        public void HandleNavigation(BaseEventData eventData)
        {
            if (eventData is AxisEventData axis) OnMove(axis);
        }

        private void Previous() => Browse(-1);
        private void Next() => Browse(1);
        private void Browse(int step)
        {
            if (!IsVisible) return;
            PiGame.Sound.UiAudioManager.PlayCharacterBrowse();
            _index = (_index + step + _characters.Length) % _characters.Length;
            Refresh();
        }

        private void Refresh()
        {
            StopVideo();
            CharacterPreview entry = _characters[_index];
            _name.text = entry.Name;
            _name.color = entry.AccentColor;
            _story.text = entry.Story;
            _attack.text = entry.Attack;
            _ability.text = entry.Ability;
            _ammo.text = entry.Ammo;
            _counter.text = $"{_index + 1:00} / {_characters.Length:00}";
            _portrait.sprite = entry.Character.Portrait;
            _portrait.rectTransform.anchoredPosition = _portraitPosition + entry.PortraitOffset;
            _abilityIcon.sprite = entry.AbilityIcon;
            SetArrow(_previous, entry.Character.LobbyUp, entry.Character.LobbyUpPressed);
            SetArrow(_next, entry.Character.LobbyDown, entry.Character.LobbyDownPressed);
            if (!entry.Video) return;
            _videoTexture = new RenderTexture(960, 540, 0) { name = "CharacterGuideVideo" };
            _videoTexture.Create();
            _videoScreen.texture = _videoTexture;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
            _videoPlayer.targetTexture = _videoTexture;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = entry.Video;
            _videoPlayer.Prepare();
        }

        private static void SetArrow(Button button, Sprite normal, Sprite highlighted)
        {
            button.image.sprite = normal;
            SpriteState state = button.spriteState;
            state.highlightedSprite = state.selectedSprite = state.pressedSprite = highlighted;
            button.spriteState = state;
        }

        private void VideoPrepared(VideoPlayer player)
        {
            if (!IsVisible || !_videoTexture || player.clip != _characters[_index].Video) return;
            _videoScreen.enabled = true;
            player.Play();
        }
        private void VideoFailed(VideoPlayer player, string error)
        {
            Debug.LogWarning($"Vídeo do guia indisponível: {error}", this);
            StopVideo();
        }
        private void StopVideo()
        {
            if (_videoPlayer)
            {
                _videoPlayer.Stop();
                _videoPlayer.clip = null;
                _videoPlayer.targetTexture = null;
            }
            if (_videoScreen) { _videoScreen.enabled = false; _videoScreen.texture = null; }
            if (!_videoTexture) return;
            _videoTexture.Release();
            Destroy(_videoTexture);
            _videoTexture = null;
        }
        private void OnDisable() => Hide();
        private void OnDestroy()
        {
            StopVideo();
            if (!_videoPlayer) return;
            _videoPlayer.prepareCompleted -= VideoPrepared;
            _videoPlayer.errorReceived -= VideoFailed;
        }
    }
}
