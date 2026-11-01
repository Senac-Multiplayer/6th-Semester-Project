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

        private void Awake()
        {
            if (!_match || !_pause || !_disconnect || !_root || !_content || !_heading || !_hostHint
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
            if (!visible) return;

            if (opening)
            {
                _pause.ClosePause();
                _confirmation.Hide();
                _content.interactable = true;
                _content.blocksRaycasts = true;
            }

            _scores.Clear();
            for (int i = 0; i < _match.ScoreCount; i++) _scores.Add(_match.GetScore(i));
            int winner = _match.WinningPlayerSlot;
            _scores.Sort((a, b) =>
            {
                if ((a.PlayerSlot == winner) != (b.PlayerSlot == winner))
                    return a.PlayerSlot == winner ? -1 : 1;
                int points = b.Kills.CompareTo(a.Kills);
                return points != 0 ? points : a.PlayerSlot.CompareTo(b.PlayerSlot);
            });

            int place = 1;
            _heading.text = winner < 0 ? "EMPATE!" : "RESULTADO";
            for (int i = 0; i < _players.Length; i++)
            {
                if (i >= _scores.Count) { _players[i].gameObject.SetActive(false); continue; }
                MatchPlayerScoreData score = _scores[i];
                if (i == 0 || score.Kills != _scores[i - 1].Kills
                    || _scores[i - 1].PlayerSlot == winner) place = i + 1;
                bool animate = opening || !_players[i].gameObject.activeSelf;
                _players[i].Show(score, place, score.PlayerSlot == winner, animate);
                if (score.PlayerSlot == winner) _heading.text = $"{score.PlayerName} VENCEU!";
            }

            RefreshReturnHint();
            _exitButton.interactable = !_busy;
            if (opening) QueueSelection();
        }

        private void Update()
        {
            if (_root.activeSelf && !_busy) RefreshReturnHint();
        }

        private void RefreshReturnHint()
        {
            _returnButton.interactable = _match.CanReturnToLobby && !_busy;
            _hostHint.text = _match.CanReturnToLobby
                ? $"Retorno automático ao lobby em {Mathf.CeilToInt(_match.ResultSecondsRemaining)}s."
                : "Aguarde a apresentação do resultado...";
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (_root.activeSelf && !_confirmation.gameObject.activeSelf) AskExit();
        }

        private void ReturnToLobby()
        {
            if (_busy) return;
            _busy = _match.RequestReturnToLobby();
            _content.interactable = !_busy;
        }

        private void AskExit()
        {
            if (_busy) return;
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
