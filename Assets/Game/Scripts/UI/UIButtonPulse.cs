using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PiGame.UI
{
    [RequireComponent(typeof(Selectable))]
    public class UIButtonPulse : MonoBehaviour,
        ISelectHandler,
        IDeselectHandler,
        IPointerEnterHandler
    {
        private const float SelectedScale = 1.1f;
        private const float PulseAmount = 0.012f;
        private const float PulseSpeed = 2f;
        private const float TransitionSpeed = 14f;
        private const float ScaleComparisonTolerance = 0.0001f;

        private static Transform _selectedTransform;
        private static Vector3 _selectedBaseScale;
        private static Vector3 _lastAppliedScale;

        private Selectable _selectable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializePulseSystem()
        {
            RestorePreviousScale();
            Canvas.willRenderCanvases -= UpdateSelectedPulse;
            Canvas.willRenderCanvases += UpdateSelectedPulse;
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        public void OnSelect(BaseEventData eventData) { }
        public void OnDeselect(BaseEventData eventData) { }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_selectable != null && _selectable.IsInteractable())
            {
                EventSystem.current?.SetSelectedGameObject(gameObject);
            }
        }

        private static void UpdateSelectedPulse()
        {
            GameObject selectedObject = EventSystem.current?.currentSelectedGameObject;
            Selectable selected = selectedObject != null
                ? selectedObject.GetComponent<Selectable>()
                : null;
            Transform nextTransform = selected != null
                && selected.IsActive()
                && selected.IsInteractable()
                && !(selected is InputField)
                && !(selected is TMPro.TMP_InputField)
                && selected.GetComponentInParent<MapVotingPanelUI>() == null
                    ? selected.transform
                    : null;

            if (nextTransform != _selectedTransform)
            {
                RestorePreviousScale();
                _selectedTransform = nextTransform;
                if (_selectedTransform == null)
                {
                    return;
                }

                _selectedBaseScale = _selectedTransform.localScale;
                _lastAppliedScale = _selectedBaseScale;
            }

            if (_selectedTransform == null)
            {
                return;
            }

            float pulse = 1f
                + Mathf.Sin(Time.unscaledTime * Mathf.PI * PulseSpeed) * PulseAmount;
            bool alreadyEmphasized = _selectedBaseScale.x > 1.02f
                || _selectedBaseScale.y > 1.02f;
            float focusScale = alreadyEmphasized ? 1f : SelectedScale;
            Vector3 targetScale = _selectedBaseScale * (focusScale * pulse);
            float interpolation = 1f - Mathf.Exp(-TransitionSpeed * Time.unscaledDeltaTime);
            _selectedTransform.localScale = Vector3.Lerp(
                _selectedTransform.localScale,
                targetScale,
                interpolation);
            _lastAppliedScale = _selectedTransform.localScale;
        }

        private static void RestorePreviousScale()
        {
            if (_selectedTransform != null
                && (_selectedTransform.localScale - _lastAppliedScale).sqrMagnitude
                    <= ScaleComparisonTolerance)
            {
                _selectedTransform.localScale = _selectedBaseScale;
            }

            _selectedTransform = null;
            _selectedBaseScale = Vector3.one;
            _lastAppliedScale = Vector3.one;
        }
    }
}
