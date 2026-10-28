using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PiGame.UI
{
    public class PauseMenuController : MonoBehaviour
    {
        [SerializeField] private PauseMenuUI _view;
        [SerializeField] private MonoBehaviour _contextSource;
        [SerializeField] private PauseMenuDefinition _definition;

        private IPauseContext _context;
        private bool _isOpen;
        private bool _isExiting;

        private void Awake()
        {
            _context = _contextSource as IPauseContext;

            if (_view == null || _context == null || _definition == null)
            {
                Debug.LogError("PauseMenuController precisa de View, Context e Definition no Inspector.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_view == null)
            {
                return;
            }

            _view.ResumeRequested += ClosePause;
            _view.ExitRequested += ShowExitConfirmation;
            _view.ExitConfirmed += HandleExitConfirmed;
            _view.ExitCanceled += HideExitConfirmation;
        }

        private void Start()
        {
            if (!_view.Initialize(_definition))
            {
                enabled = false;
                return;
            }

            _view.SetVisible(false);
        }

        private void OnDisable()
        {
            if (_view != null)
            {
                _view.ResumeRequested -= ClosePause;
                _view.ExitRequested -= ShowExitConfirmation;
                _view.ExitConfirmed -= HandleExitConfirmed;
                _view.ExitCanceled -= HideExitConfirmation;
            }

            if (_isOpen && !_isExiting)
            {
                _context.ExitPause();
            }

            _isOpen = false;
        }

        private void Update()
        {
            if (_isExiting)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleBackOrToggle();
                return;
            }

            if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
            {
                HandleBackOrToggle();
                return;
            }

            if (!_isOpen)
            {
                return;
            }

            if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
            {
                if (_view.IsOptionsVisible)
                {
                    _view.HandleOptionsBack();
                }
                else if (_view.IsControlsVisible)
                {
                    if (!_view.BlocksControlsBackShortcut)
                    {
                        _view.HandleControlsBack();
                    }
                }
                else if (_view.IsConfirmationVisible)
                {
                    HideExitConfirmation();
                }
                else
                {
                    ClosePause();
                }
            }
        }

        private void HandleBackOrToggle()
        {
            if (!_isOpen)
            {
                OpenPause();
                return;
            }

            if (_view.IsConfirmationVisible)
            {
                HideExitConfirmation();
                return;
            }

            if (_view.IsOptionsVisible)
            {
                _view.HandleOptionsBack();
                return;
            }

            if (_view.IsControlsVisible)
            {
                if (!_view.BlocksControlsBackShortcut)
                {
                    _view.HandleControlsBack();
                }
                return;
            }

            ClosePause();
        }

        private void OpenPause()
        {
            if (_isOpen || !_context.CanOpenPause)
            {
                return;
            }

            _isOpen = true;
            _context.EnterPause();
            _view.SetVisible(true);
        }

        public void ClosePause()
        {
            if (!_isOpen || _isExiting)
            {
                return;
            }

            _isOpen = false;
            _view.SetVisible(false);
            _context.ExitPause();
        }

        private void ShowExitConfirmation()
        {
            if (_isOpen && !_isExiting)
            {
                _view.ShowConfirmation();
            }
        }

        private void HideExitConfirmation()
        {
            if (_isOpen && !_isExiting)
            {
                _view.HideConfirmation();
            }
        }

        private async void HandleExitConfirmed()
        {
            if (!_isOpen || _isExiting)
            {
                return;
            }

            _isExiting = true;
            _view.SetBusy(true);

            try
            {
                await _context.ExitContextAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _isExiting = false;
                _view.SetBusy(false);
                _view.HideConfirmation();
            }
        }
    }
}
