using System.Text;
using TMPro;
using UnityEngine;

namespace PiGame.Gameplay
{
    public class MatchUIManager : MonoBehaviour
    {
        [SerializeField] private MatchController _matchController;
        [SerializeField] private TMP_Text _timerText;
        [SerializeField] private TMP_Text _scoreText;
        private bool _configured;

        private void Awake()
        {
            if (!_matchController || !_timerText || !_scoreText)
            {
                Debug.LogError("Configure MatchController, Timer e Score do MatchUIManager na cena.", this);
                enabled = false;
                return;
            }
            _configured = true;
        }

        private void OnEnable()
        {
            _matchController.ScoresChanged += RefreshScores;
            _matchController.PhaseChanged += RefreshVisibility;
            RefreshScores();
            RefreshVisibility();
        }

        private void OnDisable()
        {
            if (!_configured) return;
            _matchController.ScoresChanged -= RefreshScores;
            _matchController.PhaseChanged -= RefreshVisibility;
        }

        private void Update()
        {
            float time = _matchController.RemainingTime;
            _timerText.text = time < 0f ? "INFINITO"
                : $"{Mathf.FloorToInt(time / 60f):00}:{Mathf.FloorToInt(time % 60f):00}";
        }

        private void RefreshVisibility()
        {
            bool playing = _matchController.Phase != MatchPhase.ShowingResult;
            _timerText.gameObject.SetActive(playing);
            _scoreText.gameObject.SetActive(playing);
        }

        private void RefreshScores()
        {
            var text = new StringBuilder();
            for (int i = 0; i < _matchController.ScoreCount; i++)
            {
                MatchPlayerScoreData score = _matchController.GetScore(i);
                if (i > 0) text.Append("   ");
                text.Append($"<color=#{PlayerSlotColors.GetHtml(score.PlayerSlot)}>P{score.PlayerSlot + 1}: "
                    + $"{score.Kills}/{_matchController.KillLimit}</color>");
            }
            _scoreText.text = text.ToString();
        }
    }
}
