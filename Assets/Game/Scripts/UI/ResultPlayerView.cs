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
            [Tooltip("Ajuste vertical adicional na pose de vitória.")]
            public float VictoryOffsetY;
            [Tooltip("Ajuste vertical adicional somente na pose de derrota.")]
            public float DefeatOffsetY;
            public float OffsetX;
        }

        [SerializeField] private RectTransform _podium;
        [SerializeField] private CanvasGroup _information;
        [SerializeField] private CanvasGroup _spotlight;
        [SerializeField] private Image _frame;
        [Tooltip("Ordem: ouro (1º), prata (2º), bronze (3º) e ferro (4º).")]
        [SerializeField] private Sprite[] _placementFrames;
        [SerializeField] private TMP_Text _place;
        [SerializeField] private TMP_Text _playerTag;
        [SerializeField] private TMP_Text _teamLabel;
        [SerializeField] private TMP_Text _playerName;
        [SerializeField] private TMP_Text _characterName;
        [SerializeField] private TMP_Text _points;
        [SerializeField] private RectTransform _pointsPlate;
        [SerializeField] private Animator _character;
        [SerializeField] private CharacterVisual[] _characters;
        [SerializeField] private Color _winnerColor = new Color32(231, 186, 113, 255);

        [Header("Subida dos pilares")]
        [SerializeField, Min(0f)] private float _baseHeight = 140f;
        [SerializeField, Min(0f)] private float _maximumRise = 140f;
        [SerializeField, Range(0f, 8f)] private float _shakePixels = 1.5f;

        [Tooltip("Altura dos pés na superfície do pilar; o personagem fica à frente da arte.")]
        [SerializeField] private float _characterSurfaceOffsetY = -42f;

        private CharacterVisual _visual;
        private bool _winner;
        private bool _draw;
        private int _playerSlot;
        private int _kills;
        private int _shownPoints = -1;
        private int _pose = -1;
        private float _riseAmount;
        private RectTransform _spotlightRect;
        private float _spotlightHeight;
        private RectTransform _spotlightCeiling;
        private RectTransform _pillarViewport;

        private void Awake()
        {
            if (!_podium || !_information || !_spotlight || !_frame || !_place || !_playerTag
                || !_teamLabel || !_playerName || !_characterName || !_points || !_pointsPlate
                || _placementFrames == null || _placementFrames.Length != 4
                || Array.Exists(_placementFrames, frame => !frame)
                || !_character || _characters == null
                || _characters.Length == 0 || Array.Exists(_characters, c => c == null || !c.Idle))
            {
                Debug.LogError("Configure o pilar, cartão, molduras de colocação, luz, textos e animações no ResultPlayerView.", this);
                enabled = false;
                return;
            }
            _spotlightRect = _spotlight.GetComponent<RectTransform>();
            _spotlightHeight = _spotlightRect.rect.height;
            _pillarViewport = (RectTransform)_podium.parent;
            Image spotlightImage = _spotlight.GetComponent<Image>();
            spotlightImage.preserveAspect = false;
            // Preserve o oval inferior ao estender somente a parte superior do feixe.
            spotlightImage.type = Image.Type.Sliced;
            float referencePixels = spotlightImage.canvas ? spotlightImage.canvas.referencePixelsPerUnit : 100f;
            float pixelsPerUnit = spotlightImage.sprite.pixelsPerUnit / referencePixels;
            spotlightImage.pixelsPerUnitMultiplier = spotlightImage.sprite.rect.height
                / Mathf.Max(0.001f, _spotlightHeight * pixelsPerUnit);
        }

        private void OnDisable()
        {
            _visual = null;
            _pose = -1;
            _shownPoints = -1;
        }

        public void SetSpotlightCeiling(RectTransform ceiling) => _spotlightCeiling = ceiling;

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
            bool hasTeam = LobbyTeams.IsValid(player.Team);
            _teamLabel.gameObject.SetActive(hasTeam);
            _teamLabel.text = hasTeam ? LobbyTeams.Name(player.Team) : string.Empty;
            _teamLabel.color = LobbyTeams.Color(player.Team);
            _frame.sprite = _placementFrames[Mathf.Clamp(place - 1, 0, _placementFrames.Length - 1)];
            _frame.color = Color.white;
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
            if (_spotlightCeiling)
            {
                Vector3 ceiling = _spotlightCeiling.TransformPoint(_spotlightCeiling.rect.center);
                // A área de recorte também precisa alcançar a barra nas telas mais altas.
                float viewportTop = _pillarViewport.InverseTransformPoint(ceiling).y;
                _pillarViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(0f, viewportTop - _pillarViewport.rect.yMin));
                _spotlightRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(0f, _spotlightRect.InverseTransformPoint(ceiling).y));
            }
            else
            {
                _spotlightRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    _spotlightHeight + _maximumRise - _riseAmount * rise);
            }
            _spotlight.alpha = _winner ? Mathf.Clamp01(light) : 0f;
            _information.alpha = Mathf.Clamp01(information);

            int pose = light <= 0f || _draw ? 0 : _winner ? 1 : 2;
            if (_pose == pose) return;
            _pose = pose;
            RuntimeAnimatorController controller = pose == 1 ? _visual.Victory
                : pose == 2 ? _visual.Defeat : _visual.Idle;
            if (!controller) controller = _visual.Idle;
            Animator actor = _character;
            if (actor.runtimeAnimatorController != controller)
            {
                actor.runtimeAnimatorController = controller;
                actor.Rebind();
                actor.Update(0f);
            }
            // O clip compensa os quadros; aplique a posição base apenas ao trocar a pose.
            ((RectTransform)_character.transform).anchoredPosition =
                new Vector2(_visual.OffsetX, _characterSurfaceOffsetY + _visual.FeetOffsetY
                    + (pose == 1 ? _visual.VictoryOffsetY : pose == 2 ? _visual.DefeatOffsetY : 0f));
        }
    }
}
