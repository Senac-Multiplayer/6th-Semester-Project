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
        private sealed class TutorialTopic
        {
            public string Heading;
            [TextArea(2, 8)] public string Description;
            public Sprite Icon;
            [Tooltip("Clipes mudos. Havendo mais de um, são exibidos em sequência e repetidos.")]
            public VideoClip[] Videos = Array.Empty<VideoClip>();
        }

        [Serializable]
        private sealed class CharacterPreview
        {
            public LobbyCharacterDefinition Character;
            public string Name;
            public Color AccentColor = Color.white;
            [TextArea(2, 5)] public string Story;
            [Tooltip("Ajuste exclusivo do retrato deste guia.")]
            public Vector2 PortraitOffset;
            public TutorialTopic[] Topics;
        }

        [SerializeField] private GameObject _root;
        [SerializeField] private Text _name, _story, _counter, _videoHeading;
        [SerializeField] private Image _portrait;
        [SerializeField] private Button _previous, _next, _back;
        [SerializeField] private RectTransform _topicsRoot;
        [SerializeField] private CharacterGuideTopicUI[] _topicViews;
        [SerializeField] private RawImage _videoScreen;
        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private CharacterPreview[] _characters;
        private RenderTexture _videoTexture;
        private int _index, _activeTopic = -1, _clipIndex, _videoVersion;
        private int _backFrame = -1;
        private Vector2 _portraitPosition;
        private VideoClip[] _playlist = Array.Empty<VideoClip>();
        private VideoPlayer.EventHandler _prepared, _finished;
        private VideoPlayer.FrameReadyEventHandler _frameReady;
        private VideoPlayer.ErrorEventHandler _failed;
        public bool IsVisible => _root && _root.activeInHierarchy;
        public event Action Closed;

        private void Awake()
        {
            if (!_root || !_name || !_story || !_counter || !_videoHeading || !_portrait
                || !_previous || !_next || !_back || !_topicsRoot || !_videoScreen || !_videoPlayer
                || _topicViews == null || _topicViews.Length == 0 || Array.Exists(_topicViews, view => !view)
                || _characters == null || _characters.Length == 0
                || Array.Exists(_characters, entry => entry == null || !entry.Character
                    || entry.Topics == null || entry.Topics.Length == 0 || entry.Topics.Length > _topicViews.Length
                    || Array.Exists(entry.Topics, topic => topic == null)))
            {
                Debug.LogError("Configure as referências, os blocos e os personagens do guia no prefab.", this);
                enabled = false;
                return;
            }
            _portraitPosition = _portrait.rectTransform.anchoredPosition;
            _previous.onClick.AddListener(Previous);
            _next.onClick.AddListener(Next);
            _back.onClick.AddListener(HandleBack);
            for (int i = 0; i < _topicViews.Length; i++) _topicViews[i].Bind(this, i);
            _videoPlayer.playOnAwake = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        public void Show(LobbyCharacterId initial = LobbyCharacterId.None)
        {
            if (!enabled) return;
            _index = Array.FindIndex(_characters, entry => entry.Character.Id == initial);
            if (_index < 0) _index = 0;
            _root.SetActive(true);
            Refresh();
            Focus(_topicViews[0]);
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
        // Os EventTriggers das setas e de Voltar encaminham Move para este método.
        public void HandleNavigation(BaseEventData eventData)
        {
            if (eventData is AxisEventData axis) OnMove(axis);
        }
        public void OnMove(AxisEventData eventData)
        {
            if (!IsVisible) return;
            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (eventData.moveDir == MoveDirection.Right)
                Focus(_topicViews[Mathf.Max(0, _activeTopic)]);
            else if (selected == _previous.gameObject)
                Focus(eventData.moveDir == MoveDirection.Up ? _back : _next);
            else if (selected == _next.gameObject)
                Focus(eventData.moveDir == MoveDirection.Up ? _previous : _back);
            else if (selected == _back.gameObject)
                Focus(eventData.moveDir == MoveDirection.Up ? _topicViews[TopicCount - 1] : _previous);
            else Focus(_topicViews[0]);
            eventData.Use();
        }
        internal void NavigateTopic(int index, AxisEventData eventData)
        {
            if (!IsVisible) return;
            switch (eventData.moveDir)
            {
                case MoveDirection.Up: Focus(index > 0 ? _topicViews[index - 1] : _previous); break;
                case MoveDirection.Down: Focus(index + 1 < TopicCount ? _topicViews[index + 1] : _back); break;
                case MoveDirection.Left: Focus(_previous); break;
            }
            eventData.Use();
        }
        private int TopicCount => _characters[_index].Topics.Length;
        private static void Focus(Selectable target) => EventSystem.current?.SetSelectedGameObject(target.gameObject);
        private void Previous() => Browse(-1);
        private void Next() => Browse(1);
        private void Browse(int step)
        {
            if (!IsVisible) return;
            PiGame.Sound.UiAudioManager.PlayCharacterBrowse();
            _index = (_index + step + _characters.Length) % _characters.Length;
            Refresh();
            Focus(_topicViews[0]);
        }

        private void Refresh()
        {
            StopVideo();
            _activeTopic = -1;
            CharacterPreview entry = _characters[_index];
            _name.text = entry.Name;
            _name.color = entry.AccentColor;
            _story.text = entry.Story;
            _counter.text = $"{_index + 1:00} / {_characters.Length:00}";
            _portrait.sprite = entry.Character.Portrait;
            _portrait.rectTransform.anchoredPosition = _portraitPosition + entry.PortraitOffset;
            for (int i = 0; i < _topicViews.Length; i++)
            {
                bool visible = i < entry.Topics.Length;
                _topicViews[i].gameObject.SetActive(visible);
                if (!visible) continue;
                TutorialTopic topic = entry.Topics[i];
                _topicViews[i].SetContent(topic.Heading, topic.Description, topic.Icon, entry.AccentColor);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_topicsRoot.parent);
            SetArrow(_previous, entry.Character.LobbyUp, entry.Character.LobbyUpPressed);
            SetArrow(_next, entry.Character.LobbyDown, entry.Character.LobbyDownPressed);
            SelectTopic(0);
        }
        internal void SelectTopic(int index)
        {
            if (!IsVisible || index < 0 || index >= TopicCount || _activeTopic == index) return;
            _activeTopic = index;
            for (int i = 0; i < _topicViews.Length; i++) _topicViews[i].SetHighlighted(i == index);
            VideoClip[] clips = _characters[_index].Topics[index].Videos ?? Array.Empty<VideoClip>();
            bool same = _playlist.Length == clips.Length;
            for (int i = 0; same && i < clips.Length; i++) same &= clips[i] == _playlist[i];
            if (same)
            {
                UpdateVideoHeading();
                return;
            }
            StopVideo();
            _playlist = Array.FindAll(clips, clip => clip);
            _clipIndex = 0;
            UpdateVideoHeading();
            if (_playlist.Length > 0) PrepareClip();
        }

        private void UpdateVideoHeading()
        {
            _videoHeading.text = _characters[_index].Topics[_activeTopic].Heading
                + (_playlist.Length > 1 ? $"  {_clipIndex + 1}/{_playlist.Length}" : string.Empty);
        }
        private void PrepareClip()
        {
            DetachVideoCallbacks();
            _videoPlayer.Stop();
            int version = ++_videoVersion;
            VideoClip clip = _playlist[_clipIndex];
            _videoScreen.enabled = false;
            if (!_videoTexture)
            {
                _videoTexture = new RenderTexture(960, 540, 0) { name = "CharacterGuideVideo" };
                _videoTexture.Create();
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _videoTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
            _videoScreen.texture = _videoTexture;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
            _videoPlayer.targetTexture = _videoTexture;
            _videoPlayer.source = VideoSource.VideoClip;
            _videoPlayer.clip = clip;
            _videoPlayer.isLooping = _playlist.Length == 1;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.sendFrameReadyEvents = true;
            bool IsCurrent(VideoPlayer player) => version == _videoVersion && IsVisible
                && _videoTexture && player.clip == clip;
            _prepared = player => { if (IsCurrent(player) && player.isPrepared) player.Play(); };
            _frameReady = (player, frame) => { if (IsCurrent(player) && player.isPrepared) _videoScreen.enabled = true; };
            _failed = (player, error) =>
            {
                if (!IsCurrent(player)) return;
                Debug.LogWarning($"Vídeo do guia indisponível: {error}", this);
                StopVideo();
            };
            _finished = player =>
            {
                if (!IsCurrent(player) || _playlist.Length < 2) return;
                _clipIndex = (_clipIndex + 1) % _playlist.Length;
                UpdateVideoHeading();
                PrepareClip();
            };
            _videoPlayer.prepareCompleted += _prepared;
            _videoPlayer.frameReady += _frameReady;
            _videoPlayer.errorReceived += _failed;
            _videoPlayer.loopPointReached += _finished;
            _videoPlayer.Prepare();
        }
        private static void SetArrow(Button button, Sprite normal, Sprite highlighted)
        {
            button.image.sprite = normal;
            SpriteState state = button.spriteState;
            state.highlightedSprite = state.selectedSprite = state.pressedSprite = highlighted;
            button.spriteState = state;
        }
        private void DetachVideoCallbacks()
        {
            if (!_videoPlayer) return;
            if (_prepared != null) _videoPlayer.prepareCompleted -= _prepared;
            if (_frameReady != null) _videoPlayer.frameReady -= _frameReady;
            if (_failed != null) _videoPlayer.errorReceived -= _failed;
            if (_finished != null) _videoPlayer.loopPointReached -= _finished;
            _prepared = _finished = null;
            _frameReady = null;
            _failed = null;
        }
        private void StopVideo()
        {
            ++_videoVersion;
            DetachVideoCallbacks();
            _playlist = Array.Empty<VideoClip>();
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
        private void OnDestroy() => StopVideo();
    }
}
