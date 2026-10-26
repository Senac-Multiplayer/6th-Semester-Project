using TMPro;
using UnityEngine;

namespace PiGame.Gameplay
{
    [RequireComponent(typeof(NetworkPlayerState))]
    public class PlayerIndicator : MonoBehaviour
    {
        [SerializeField] private Vector3 _localPosition = new Vector3(0f, 1.45f, 0f);
        [SerializeField] private Vector3 _labelLocalPosition = new Vector3(0f, 2f, 0f);
        [SerializeField] private Material _labelMaterial;

        private NetworkPlayerState _playerState;
        private MeshRenderer _renderer;
        private TextMeshPro _playerLabel;

        private void Awake()
        {
            _playerState = GetComponent<NetworkPlayerState>();
            CreateIndicator();
        }

        private void OnEnable()
        {
            _playerState.StateChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            _playerState.StateChanged -= Refresh;
        }

        private void CreateIndicator()
        {
            GameObject indicator = new GameObject("PlayerIndicator");
            indicator.transform.SetParent(transform, false);
            indicator.transform.localPosition = _localPosition;

            Mesh mesh = new Mesh
            {
                name = "PlayerIndicatorTriangle",
                vertices = new[]
                {
                    new Vector3(-0.28f, 0.23f, 0f),
                    new Vector3(0.28f, 0.23f, 0f),
                    new Vector3(0f, -0.25f, 0f),
                    new Vector3(-0.22f, 0.18f, 0f),
                    new Vector3(0.22f, 0.18f, 0f),
                    new Vector3(0f, -0.18f, 0f),
                    new Vector3(-0.22f, 0.18f, 0f),
                    new Vector3(0.22f, 0.18f, 0f),
                    new Vector3(0f, -0.18f, 0f)
                },
                // Black border and tinted fill share the existing mesh and renderer.
                colors = new[]
                {
                    Color.black, Color.black, Color.black,
                    Color.black, Color.black, Color.black,
                    Color.white, Color.white, Color.white
                },
                triangles = new[]
                {
                    0, 1, 4, 0, 4, 3,
                    1, 2, 5, 1, 5, 4,
                    2, 0, 3, 2, 3, 5,
                    6, 7, 8
                }
            };

            indicator.AddComponent<MeshFilter>().sharedMesh = mesh;
            _renderer = indicator.AddComponent<MeshRenderer>();
            _renderer.material = new Material(Shader.Find("Sprites/Default"));
            SpriteRenderer playerRenderer = GetComponentInChildren<SpriteRenderer>();
            if (playerRenderer != null)
            {
                _renderer.sortingLayerID = playerRenderer.sortingLayerID;
            }
            _renderer.sortingOrder = 30;

            GameObject labelObject = new GameObject(
                "PlayerSlotLabel",
                typeof(RectTransform));
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = _labelLocalPosition;

            _playerLabel = labelObject.AddComponent<TextMeshPro>();
            _playerLabel.fontSharedMaterial = _labelMaterial;
            _playerLabel.alignment = TextAlignmentOptions.Center;
            _playerLabel.enableAutoSizing = false;
            _playerLabel.fontSize = 5f;
            _playerLabel.fontStyle = FontStyles.Bold;
            _playerLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _playerLabel.overflowMode = TextOverflowModes.Overflow;
            _playerLabel.outlineColor = Color.black;
            _playerLabel.outlineWidth = 0.3f;
            _playerLabel.rectTransform.sizeDelta = new Vector2(2.2f, 0.8f);
            _playerLabel.renderer.sortingLayerID = _renderer.sortingLayerID;
            _playerLabel.renderer.sortingOrder = 31;
        }

        private void Refresh()
        {
            if (_renderer == null)
            {
                return;
            }

            bool shouldShow = _playerState.IsAlive && _playerState.PlayerSlot >= 0;
            Color playerColor = _playerState.IndicatorColor;

            _renderer.enabled = shouldShow;
            _renderer.material.color = playerColor;

            if (_playerLabel == null)
            {
                return;
            }

            _playerLabel.enabled = shouldShow;
            _playerLabel.text = shouldShow
                ? $"P{_playerState.PlayerSlot + 1}"
                : string.Empty;
            _playerLabel.color = playerColor;
        }
    }
}
