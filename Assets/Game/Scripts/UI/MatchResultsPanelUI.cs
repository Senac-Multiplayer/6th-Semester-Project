using System;
using System.Collections;
using System.Collections.Generic;
using PiGame.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public class MatchResultsPanelUI : MonoBehaviour, ICancelHandler
    {
        [SerializeField] private MatchController _match;
        [SerializeField] private PauseMenuController _pause;
        [SerializeField] private NetcodeMatchDisconnectService _disconnect;
        [SerializeField] private GameObject _root;
        [SerializeField] private CanvasGroup _content;
        [SerializeField] private CanvasGroup _headingGroup;
        [SerializeField] private CanvasGroup _actions;
        [Header("Apresentação do resultado")]
        [SerializeField, Min(0.1f)] private float _riseSeconds = 2.4f;
        [SerializeField, Min(0.01f)] private float _spotlightSeconds = 0.45f;
        [SerializeField, Min(0.01f)] private float _informationFadeSeconds = 0.3f;
        [SerializeField] private AnimationCurve _riseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private TMP_Text _heading;
        [SerializeField] private TMP_Text _hostHint;
        [SerializeField] private ResultPlayerView[] _players;
        [SerializeField] private Button _returnButton;
        [SerializeField] private Button _exitButton;
        [SerializeField] private ConfirmationPanelUI _confirmation;

        private readonly List<MatchPlayerScoreData> _scores = new();
        private bool _busy;
        private bool _configured;
        private Coroutine _selection;
        private bool _presentationComplete;
        public float PresentationDuration => _riseSeconds + _spotlightSeconds + _informationFadeSeconds;

        private void Awake()
        {
            if (!_match || !_pause || !_disconnect || !_root || !_content || !_headingGroup || !_actions || !_heading || !_hostHint
                || !_returnButton || !_exitButton || !_confirmation || _players == null
                || _players.Length == 0 || Array.Exists(_players, p => !p))
            {
                Debug.LogError("Configure as referências do MatchResultsPanelUI na cena/prefab.", this);
                enabled = false;
                return;
            }
            _configured = true;
        }

        private void OnEnable()
        {
            if (!_configured) return;
            _match.PhaseChanged += Refresh;
            _match.ScoresChanged += Refresh;
            _returnButton.onClick.AddListener(ReturnToLobby);
            _exitButton.onClick.AddListener(AskExit);
            _confirmation.Confirmed += ExitToMenu;
            _confirmation.Canceled += CancelExit;
            Refresh();
        }

        private void OnDisable()
        {
            if (!_configured) return;
            _match.PhaseChanged -= Refresh;
            _match.ScoresChanged -= Refresh;
            _returnButton.onClick.RemoveListener(ReturnToLobby);
            _exitButton.onClick.RemoveListener(AskExit);
            _confirmation.Confirmed -= ExitToMenu;
            _confirmation.Canceled -= CancelExit;
            if (_selection != null) StopCoroutine(_selection);
        }

        private void Refresh()
        {
            bool visible = _match.Phase == MatchPhase.ShowingResult;
            bool opening = visible && !_root.activeSelf;
            _root.SetActive(visible);
            if (!visible)
            {
                _busy = false;
                _presentationComplete = false;
                return;
            }
            if (opening)
            {
                _pause.ClosePause();
                _confirmation.Hide();
                _busy = false;
                _presentationComplete = false;
                EventSystem.current?.SetSelectedGameObject(null);
            }

            _scores.Clear();
            int highestScore = 0;
            for (int i = 0; i < _match.ScoreCount; i++)
            {
                MatchPlayerScoreData score = _match.GetScore(i);
                _scores.Add(score);
                highestScore = Mathf.Max(highestScore, score.Kills);
            }
            int winner = _match.WinningPlayerSlot;
            _scores.Sort((a, b) =>
            {
                if ((a.PlayerSlot == winner) != (b.PlayerSlot == winner))
                    return a.PlayerSlot == winner ? -1 : 1;
                int points = b.Kills.CompareTo(a.Kills);
                return points != 0 ? points : a.PlayerSlot.CompareTo(b.PlayerSlot);
            });
            _heading.text = winner < 0 ? "EMPATE!" : "RESULTADO";
            for (int slot = 0; slot < _players.Length; slot++)
            {
                int index = _scores.FindIndex(score => score.PlayerSlot == slot);
                if (index < 0)
                {
                    _players[slot].gameObject.SetActive(false);
                    continue;
                }
                int place = 1;
                for (int i = 1; i <= index; i++)
                    if (_scores[i].Kills != _scores[i - 1].Kills || _scores[i - 1].PlayerSlot == winner)
                        place = i + 1;
                MatchPlayerScoreData player = _scores[index];
                _players[slot].Bind(player, place, slot == winner, highestScore, winner < 0);
                if (slot == winner) _heading.text = $"{player.PlayerName} VENCEU!";
            }
            RenderPresentation(_match.ResultElapsed);
        }

        private void Update()
        {
            if (_configured && _root.activeSelf) RenderPresentation(_match.ResultElapsed);
        }

        private void RenderPresentation(float elapsed)
        {
            float rise = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _riseSeconds));
            float light = Mathf.Clamp01((elapsed - _riseSeconds) / Mathf.Max(0.01f, _spotlightSeconds));
            float information = Mathf.Clamp01((elapsed - _riseSeconds - _spotlightSeconds)
                / Mathf.Max(0.01f, _informationFadeSeconds));
            foreach (ResultPlayerView player in _players)
                if (player.gameObject.activeSelf) player.Present(_riseCurve.Evaluate(rise), light, information);
            _headingGroup.alpha = information;
            _actions.alpha = information;
            bool wasComplete = _presentationComplete;
            _presentationComplete = elapsed >= PresentationDuration;
            bool interactive = _presentationComplete && !_busy && !_confirmation.gameObject.activeSelf;
            _content.interactable = interactive;
            _content.blocksRaycasts = !_confirmation.gameObject.activeSelf;
            _actions.interactable = interactive;
            _actions.blocksRaycasts = interactive;
            _exitButton.interactable = interactive;
            RefreshReturnHint();
            if (_presentationComplete && !wasComplete) QueueSelection();
        }

        private void RefreshReturnHint()
        {
            _returnButton.interactable = _presentationComplete && _match.CanReturnToLobby && !_busy;
            _hostHint.text = _match.CanReturnToLobby
                ? $"Retorno automático ao lobby em {Mathf.CeilToInt(_match.ResultSecondsRemaining)}s."
                : "Aguarde a apresentação do resultado...";
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (_root.activeSelf && _presentationComplete && !_confirmation.gameObject.activeSelf) AskExit();
        }

        private void ReturnToLobby()
        {
            if (_busy || !_presentationComplete || !_match.CanReturnToLobby) return;
            _busy = _match.RequestReturnToLobby();
            _content.interactable = !_busy;
        }

        private void AskExit()
        {
            if (_busy || !_presentationComplete) return;
            _content.interactable = false;
            _content.blocksRaycasts = false;
            _confirmation.Show(_match.IsServer
                ? "Sair para o menu?\nOutro jogador assumirá a sala."
                : "Sair da sala e voltar para o menu?");
        }

        private void CancelExit()
        {
            if (_busy) return;
            _confirmation.Hide();
            _content.interactable = true;
            _content.blocksRaycasts = true;
            QueueSelection();
        }

        private async void ExitToMenu()
        {
            if (_busy) return;
            _busy = true;
            _confirmation.Hide();
            try { await _disconnect.DisconnectAsync(); }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                _busy = false;
                CancelExit();
            }
        }

        private void QueueSelection()
        {
            if (_selection != null) StopCoroutine(_selection);
            _selection = StartCoroutine(SelectNextFrame());
        }

        private IEnumerator SelectNextFrame()
        {
            yield return null;
            EventSystem.current.SetSelectedGameObject(
                _returnButton.interactable ? _returnButton.gameObject : _exitButton.gameObject);
            _selection = null;
        }
    }
}
