using PiGame.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    [DefaultExecutionOrder(-100)]
    public sealed class LoadingScreenUI : MonoBehaviour
    {
        [SerializeField] private GameObject _content;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private GameObject _mapDetails;
        [SerializeField] private Image _mapPreview;
        [SerializeField] private TMP_Text _mapName;
        [SerializeField] private TMP_Text _message;
        [SerializeField] private RectTransform _spinner;
        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.25f;
        [SerializeField] private float _spinnerDegreesPerSecond = 180f;

        public static LoadingScreenUI Instance { get; private set; }
        public bool IsVisible => _content.activeSelf;
        private bool _closing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (!_content || !_group || !_mapDetails || !_mapPreview || !_mapName || !_message || !_spinner)
            {
                Debug.LogError("Configure conteúdo, grupo, preview, nome, mensagem e círculo no LoadingScreenUI.", this);
                enabled = false;
                return;
            }
            Instance = this;
            _content.SetActive(false);
        }

        public void Show(LobbyMapDefinition map = null, string message = "CARREGANDO...")
        {
            _closing = false;
            _group.alpha = 1f;
            _message.text = message;
            _mapDetails.SetActive(map != null);
            _mapPreview.sprite = map != null ? map.Preview : null;
            _mapName.text = map != null ? map.DisplayName.ToUpperInvariant() : string.Empty;
            _content.SetActive(true);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        public void Hide() => _closing = true;

        private void Update()
        {
            if (!IsVisible) return;
            _spinner.Rotate(0f, 0f, -_spinnerDegreesPerSecond * Time.unscaledDeltaTime);
            if (!_closing) return;
            _group.alpha = _fadeOutSeconds <= 0f ? 0f
                : Mathf.MoveTowards(_group.alpha, 0f, Time.unscaledDeltaTime / _fadeOutSeconds);
            if (_group.alpha <= 0f) _content.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
