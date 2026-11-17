using System;
using PiGame.Gameplay;
using PiGame.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class ResultPlayerView : MonoBehaviour
    {
        [Serializable]
        private class CharacterVisual
        {
            public LobbyCharacterId Id;
            public string Name;
            public RuntimeAnimatorController Idle;
            public RuntimeAnimatorController Victory;
            public RuntimeAnimatorController Defeat;
            public float FeetOffsetY;
            public float OffsetX;
            public bool DefeatInFront;
            public float DefeatOffsetY;
        }

        [SerializeField] private RectTransform _podium;
        [SerializeField] private CanvasGroup _information;
        [SerializeField] private CanvasGroup _spotlight;
        [SerializeField] private Image _frame;
        [SerializeField] private TMP_Text _place;
        [SerializeField] private TMP_Text _playerTag;
        [SerializeField] private TMP_Text _playerName;
        [SerializeField] private TMP_Text _characterName;
        [SerializeField] private TMP_Text _points;
        [SerializeField] private RectTransform _pointsPlate;
        [SerializeField] private GameObject _seatedPlateCover;
        [SerializeField] private float _seatedPointsY = -90f;
        [SerializeField] private Animator _character;
        [SerializeField] private Animator _foregroundCharacter;
        [SerializeField] private CharacterVisual[] _characters;
        [SerializeField] private Color _winnerColor = new Color32(231, 186, 113, 255);

        [Header("Subida dos pilares")]
        [SerializeField, Min(0f)] private float _baseHeight = 70f;
        [SerializeField, Min(0f)] private float _maximumRise = 210f;
        [SerializeField, Range(0f, 8f)] private float _shakePixels = 1.5f;

        [SerializeField, Min(0f)] private float _feetBehindPillar = 24f;

        private Vector2 _pointsRestPosition;
        private CharacterVisual _visual;
        private bool _winner;
        private bool _draw;
        private int _playerSlot;
        private int _kills;
        private int _shownPoints = -1;
        private int _pose = -1;
        private float _riseAmount;

        private void Awake()
        {
            if (!_podium || !_information || !_spotlight || !_frame || !_place || !_playerTag
                || !_playerName || !_characterName || !_points || !_pointsPlate || !_seatedPlateCover
                || !_character || !_foregroundCharacter || _characters == null
                || _characters.Length == 0 || Array.Exists(_characters, c => c == null || !c.Idle))
            {
                Debug.LogError("Configure o pilar, cartão, luz, textos e animações no ResultPlayerView.", this);
                enabled = false;
                return;
            }
            _pointsRestPosition = _pointsPlate.anchoredPosition;
        }

        private void OnDisable()
        {
            _visual = null;
            _pose = -1;
            _shownPoints = -1;
        }

        public void Bind(MatchPlayerScoreData player, int place, bool winner, int highestScore, bool draw = false)
        {
            gameObject.SetActive(true);
            if (!enabled) return;
            CharacterVisual visual = Array.Find(_characters, c => c.Id == player.CharacterId);
            if (visual == null)
            {
                Debug.LogError($"Personagem {player.CharacterId} sem visual de resultado.", this);
                return;
            }

            if (_visual != visual)
            {
                _visual = visual;
                _pose = -1;
            }
            _winner = winner;
            _draw = draw;
            _playerSlot = player.PlayerSlot;
            _kills = Mathf.Max(0, player.Kills);
            _riseAmount = _maximumRise * Mathf.Clamp01((float)_kills / Mathf.Max(1, highestScore));
            _place.text = $"{place}º";
            _place.color = winner ? _winnerColor : Color.white;
            _playerTag.text = $"P{player.PlayerSlot + 1}";
            _playerTag.color = PlayerSlotColors.Get(player.PlayerSlot);
            _playerName.text = player.PlayerName.ToString();
            _characterName.text = visual.Name;
            _frame.color = winner ? _winnerColor : PlayerSlotColors.Get(player.PlayerSlot);
        }

        public void Present(float rise, float light, float information)
        {
            if (!enabled || _visual == null) return;
            rise = Mathf.Clamp01(rise);
            float shake = _shakePixels * Mathf.Sin(Mathf.PI * rise);
            float offset = Mathf.Sin(rise * 42f + _playerSlot * 7f) * shake;
            _podium.anchoredPosition = new Vector2(offset, _baseHeight + _riseAmount * rise);
            int points = Mathf.FloorToInt(_kills * rise + 0.0001f);
            if (_shownPoints != points)
            {
                _shownPoints = points;
                _points.text = points.ToString();
            }
            _spotlight.alpha = _winner ? Mathf.Clamp01(light) : 0f;
            _information.alpha = Mathf.Clamp01(information);

            int pose = light <= 0f || _draw ? 0 : _winner ? 1 : 2;
            bool inFront = pose == 2 && _visual.DefeatInFront && _visual.Defeat;
            float plateY = inFront
                ? Mathf.Max(_seatedPointsY, -_podium.anchoredPosition.y + _pointsPlate.rect.height * 0.5f)
                : _pointsRestPosition.y;
            _pointsPlate.anchoredPosition = new Vector2(_pointsRestPosition.x, plateY);
            if (_pose == pose) return;
            _pose = pose;
            _seatedPlateCover.SetActive(inFront);
            RuntimeAnimatorController controller = pose == 1 ? _visual.Victory
                : pose == 2 ? _visual.Defeat : _visual.Idle;
            if (!controller) controller = _visual.Idle;
            _character.gameObject.SetActive(!inFront);
            _foregroundCharacter.gameObject.SetActive(inFront);
            Animator actor = inFront ? _foregroundCharacter : _character;
            if (actor.runtimeAnimatorController != controller)
            {
                actor.runtimeAnimatorController = controller;
                actor.Rebind();
                actor.Update(0f);
            }
            // O clip compensa os quadros; aplique a posição base apenas ao trocar a pose.
            ((RectTransform)_character.transform).anchoredPosition =
                new Vector2(_visual.OffsetX, _visual.FeetOffsetY - _feetBehindPillar);
            ((RectTransform)_foregroundCharacter.transform).anchoredPosition =
                new Vector2(_visual.OffsetX, _visual.DefeatOffsetY);
        }
    }
}
