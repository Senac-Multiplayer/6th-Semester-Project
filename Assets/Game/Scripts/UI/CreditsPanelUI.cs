using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    public sealed class CreditsPanelUI : MonoBehaviour
    {
        [SerializeField] private Button[] _teamButtons;
        [SerializeField] private GameObject[] _teamPanels;
        [SerializeField] private Sprite _normalSprite;
        [SerializeField] private Sprite _selectedSprite;
        private UnityAction[] _teamActions;

        private void Awake()
        {
            if (_teamButtons == null || _teamPanels == null || _teamButtons.Length == 0
                || _teamButtons.Length != _teamPanels.Length || !_normalSprite || !_selectedSprite
                || Array.Exists(_teamButtons, button => !button) || Array.Exists(_teamPanels, panel => !panel))
            {
                Debug.LogError("Configure equipes, painéis e imagens do CreditsPanelUI.", this);
                enabled = false;
                return;
            }
            _teamActions = new UnityAction[_teamButtons.Length];
            for (int i = 0; i < _teamButtons.Length; i++)
            {
                int team = i;
                _teamActions[i] = () => ShowTeam(team);
                _teamButtons[i].onClick.AddListener(_teamActions[i]);
            }
        }

        private void OnEnable()
        {
            if (enabled && _teamActions != null) FocusDefaultTeam();
        }

        private void OnDestroy()
        {
            if (_teamActions == null) return;
            for (int i = 0; i < _teamActions.Length; i++)
                if (_teamButtons[i]) _teamButtons[i].onClick.RemoveListener(_teamActions[i]);
        }

        public void FocusDefaultTeam() => ShowTeam(0);

        private void ShowTeam(int team)
        {
            if (_teamActions == null) return;
            for (int i = 0; i < _teamPanels.Length; i++)
            {
                _teamPanels[i].SetActive(i == team);
                _teamButtons[i].image.sprite = i == team ? _selectedSprite : _normalSprite;
            }
            EventSystem.current?.SetSelectedGameObject(_teamButtons[team].gameObject);
        }
    }
}
