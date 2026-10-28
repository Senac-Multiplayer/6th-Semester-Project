using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    // Bloqueia os Selectables de trás; a navegação continua sendo a do Unity UI.
    public class PanelInputScope : MonoBehaviour
    {
        [SerializeField] private Selectable _firstSelection;

        private static readonly List<PanelInputScope> OpenPanels = new();
        private readonly List<Selectable> _blocked = new();
        private GameObject _previousSelection;
        private GameObject _lastSelection;

        private void Awake()
        {
            if (_firstSelection != null) return;
            Debug.LogError("Configure a primeira seleção do painel no PanelInputScope.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            _previousSelection = EventSystem.current?.currentSelectedGameObject;
            _lastSelection = _firstSelection.gameObject;
            OpenPanels.Add(this);
            BlockBackground();
        }

        private void LateUpdate()
        {
            if (OpenPanels.Count == 0 || OpenPanels[^1] != this) return;
            BlockBackground();
            var events = EventSystem.current;
            if (events == null) return;
            GameObject selected = events.currentSelectedGameObject;
            if (selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(transform))
            {
                _lastSelection = selected;
                return;
            }
            if (_lastSelection == null || !_lastSelection.activeInHierarchy)
                _lastSelection = _firstSelection.gameObject;
            events.SetSelectedGameObject(_lastSelection);
        }

        private void BlockBackground()
        {
            foreach (Selectable selectable in Selectable.allSelectablesArray)
            {
                if (!selectable.interactable || selectable.transform.IsChildOf(transform)) continue;
                _blocked.Add(selectable);
                selectable.interactable = false;
            }
        }

        private void OnDisable()
        {
            OpenPanels.Remove(this);
            foreach (Selectable selectable in _blocked)
                if (selectable != null) selectable.interactable = true;
            _blocked.Clear();
            if (_previousSelection != null && _previousSelection.activeInHierarchy)
                EventSystem.current?.SetSelectedGameObject(_previousSelection);
        }
    }
}
