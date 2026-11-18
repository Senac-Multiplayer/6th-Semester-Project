using System.Collections;
using PiGame.Lobby;
using UnityEngine;
using UnityEngine.UI;

namespace PiGame.UI
{
    public sealed class LobbySelectionBackgroundUI : MonoBehaviour
    {
        [SerializeField] private bool _useMenuBackground;
        [SerializeField] private RawImage _menuArtwork;
        [SerializeField] private CanvasGroup _characterGroup;
        [SerializeField] private Image _characterBackground;
        [SerializeField] private Image _characterDetails;
        [SerializeField, Min(0f)] private float _transitionSeconds = 0.5f;

        private LobbyCharacterDefinition _character;
        private Coroutine _transition;

        private void Awake()
        {
            if (!_menuArtwork || !_characterGroup || !_characterBackground || !_characterDetails)
            {
                Debug.LogError("Configure as imagens e o grupo do fundo do seletor no Inspector.", this);
                enabled = false;
                return;
            }

            _menuArtwork.gameObject.SetActive(_useMenuBackground);
            _characterGroup.gameObject.SetActive(!_useMenuBackground);
        }

        public void ShowCharacter(LobbyCharacterDefinition character)
        {
            if (!enabled || _useMenuBackground || !character || character == _character) return;

            bool firstCharacter = !_character;
            _character = character;
            if (_transition != null) StopCoroutine(_transition);

            if (firstCharacter || _transitionSeconds <= 0f)
            {
                ApplyCharacter(character);
                _characterGroup.alpha = 1f;
                _transition = null;
                return;
            }

            _transition = StartCoroutine(ChangeCharacter(character));
        }

        private IEnumerator ChangeCharacter(LobbyCharacterDefinition character)
        {
            float fadeSpeed = 2f / _transitionSeconds;
            while (_characterGroup.alpha > 0f)
            {
                _characterGroup.alpha = Mathf.MoveTowards(_characterGroup.alpha, 0f, Time.unscaledDeltaTime * fadeSpeed);
                yield return null;
            }

            ApplyCharacter(character);
            while (_characterGroup.alpha < 1f)
            {
                _characterGroup.alpha = Mathf.MoveTowards(_characterGroup.alpha, 1f, Time.unscaledDeltaTime * fadeSpeed);
                yield return null;
            }
            _transition = null;
        }

        private void ApplyCharacter(LobbyCharacterDefinition character)
        {
            _characterBackground.sprite = character.LobbyBackground;
            _characterDetails.sprite = character.LobbyBackgroundDetails;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            _transition = null;
            _character = null;
        }
    }
}
