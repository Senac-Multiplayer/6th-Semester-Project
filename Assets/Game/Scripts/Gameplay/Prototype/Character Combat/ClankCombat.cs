using System.Collections;
using PiGame.Gameplay;
using PiGame.Sound;
using Unity.Netcode;
using UnityEngine;

public class ClankCombat : NetworkBehaviour, ICharacterCombat
{
    [Header("Projectile Settings")]
    [SerializeField, Tooltip("Defini��o do proj�til disparado pelo jogador.")]
    private ProjectileDefinition _projectile;

    [SerializeField, Min(1), Tooltip("Quantidade m�xima de muni��o de proj�teis.")]
    private int _maximumAmmo = 4;

    [SerializeField, Min(0f), Tooltip("Tempo de espera em segundos entre cada disparo.")]
    private float _shotCooldownSeconds = 0.35f;

    [SerializeField, Min(0f), Tooltip("Dist�ncia � frente do jogador onde o proj�til � instanciado.")]
    private float _projectileSpawnDistance = 0.5f;

    [SerializeField, Tooltip("Indicadores visuais da muni��o na interface ou sobre o jogador.")]
    private SpriteRenderer[] _ammoIndicators;

    [SerializeField, Min(0.1f), Tooltip("Tempo agachado necess�rio para recarregar uma muni��o.")]
    private float _crouchReloadInterval = 0.6f;

    [Header("Special Settings")]
    [SerializeField, Min(1), Tooltip("Quantidade m�xima de cargas da habilidade especial.")]
    private int _maxAbilityCharges = 2;

    [SerializeField, Min(0.1f), Tooltip("Tempo em segundos para recarregar uma carga da habilidade especial.")]
    private float _abilityCooldownSeconds = 3.5f;

    [SerializeField, Min(1f), Tooltip("For�a do impulso aplicado ao pr�prio jogador ao usar o especial.")]
    private float _hammerLaunchForce = 14f;

    [SerializeField, Min(0.1f), Tooltip("Raio da �rea circular para detectar e empurrar jogadores pr�ximos.")]
    private float _knockbackRadius = 2.5f;

    [SerializeField, Min(1f), Tooltip("For�a de repuls�o aplicada aos jogadores atingidos.")]
    private float _knockbackForce = 8f;

    [SerializeField, Min(0.01f), Tooltip("Dist�ncia do Raycast para verificar se o jogador est� em contato com o ch�o.")]
    private float _groundCheckDistance = 0.2f;

    [SerializeField, Tooltip("Layer de colis�o utilizada para identificar outros jogadores no alcance.")]
    private LayerMask _playerLayer;

    [SerializeField, Tooltip("Layer de colis�o utilizada para identificar plataformas e o ch�o.")]
    private LayerMask _groundLayer;

    [Header("Gizmo Settings")]
    [SerializeField, Tooltip("Cor do contorno do Gizmo do raio de repuls�o no Editor.")]
    private Color _knockbackWireColor = Color.red;

    private readonly NetworkVariable<int> _ammo = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _abilityCharges = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkPlayerState _playerState;
    private PlayerMove _playerMove;
    private Rigidbody2D _rigidbody;
    private Coroutine _knockbackDisableRoutine;
    private FumaceiroSoundManager _fumaceiroSound;

    private float _nextShotAt;
    private float _crouchTimer;
    private float _abilityCooldownTimer;

    private void Awake()
    {
        _playerState = GetComponent<NetworkPlayerState>();
        _playerMove = GetComponent<PlayerMove>();
        _rigidbody = GetComponent<Rigidbody2D>();
        _fumaceiroSound = GetComponent<FumaceiroSoundManager>();
        if (_projectile == null || _projectile.Prefab == null
            || _ammoIndicators == null || _ammoIndicators.Length != _maximumAmmo)
        {
            Debug.LogError("Configure o proj�til e os indicadores de muni��o no ClankCombat.", this);
            enabled = false;
            return;
        }

        foreach (SpriteRenderer indicator in _ammoIndicators)
        {
            if (indicator == null)
            {
                Debug.LogError("Todos os indicadores de muni��o devem estar atribu�dos no ClankCombat.", this);
                enabled = false;
                return;
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!enabled)
        {
            return;
        }

        _ammo.OnValueChanged += HandleAmmoChanged;
        _playerState.StateChanged += RefreshAmmoIndicators;

        if (IsServer)
        {
            _ammo.Value = _maximumAmmo;
            _abilityCharges.Value = _maxAbilityCharges;
            _playerState.SetAbilityCooldownServer(0f);
        }

        foreach (SpriteRenderer indicator in _ammoIndicators)
        {
            indicator.gameObject.SetActive(true);
        }

        RefreshAmmoIndicators();
    }

    public override void OnNetworkDespawn()
    {
        _ammo.OnValueChanged -= HandleAmmoChanged;
        _playerState.StateChanged -= RefreshAmmoIndicators;
    }

    private void Update()
    {
        if (!IsServer || !enabled)
        {
            return;
        }

        if (!_playerState.CanAct)
        {
            _crouchTimer = 0f;
            return;
        }

        UpdateCrouchReloadServer();
        UpdateAbilityCooldownServer();
    }

    private void UpdateCrouchReloadServer()
    {
        if (_playerMove.IsCrouching && _ammo.Value < _maximumAmmo)
        {
            _crouchTimer += Time.deltaTime;
            if (_crouchTimer >= _crouchReloadInterval)
            {
                _ammo.Value = Mathf.Min(_maximumAmmo, _ammo.Value + 1);
                if(_fumaceiroSound != null)
                {
                    _fumaceiroSound.PlayFumaceiroCatch();
                }
                _crouchTimer = 0f;
            }
        }
        else
        {
            _crouchTimer = 0f;
        }
    }

    private void UpdateAbilityCooldownServer()
    {
        if (_abilityCharges.Value < _maxAbilityCharges)
        {
            _abilityCooldownTimer += Time.deltaTime;
            if (_abilityCooldownTimer >= _abilityCooldownSeconds)
            {
                _abilityCharges.Value++;
                _abilityCooldownTimer = 0f;
                _playerState.SetAbilityCooldownServer(_abilityCharges.Value < _maxAbilityCharges
                    ? _abilityCooldownSeconds : 0f);
            }
        }
        else
        {
            _abilityCooldownTimer = 0f;
        }
    }

    #region Metodo Do ICharacterCombat
    public void ShootServer(Vector2 direction)
    {
        if (!IsServer || !enabled || !_playerState.CanAct ||  Time.time < _nextShotAt)
        {
            return;
        }

        if(_ammo.Value <= 0 )
        {
            if(_fumaceiroSound != null)
            {
                _fumaceiroSound.PlayFumaceiroEmpty();
            }
            return;
        }


        Vector2 shotDirection = direction.sqrMagnitude > 0.01f
            ? direction.normalized
            : Vector2.right;
        Vector3 spawnPosition = transform.position + (Vector3)(shotDirection * _projectileSpawnDistance);

        if(_fumaceiroSound != null)
        {
            _fumaceiroSound.PlayFumaceiroThrow();
        }

        NetworkProjectile projectile = Instantiate(_projectile.Prefab, spawnPosition, Quaternion.identity);
        projectile.NetworkObject.Spawn(true);
        projectile.InitializeServer(OwnerClientId, shotDirection, _playerState.IndicatorColor, _projectile);

        _ammo.Value--;
        _nextShotAt = Time.time + _shotCooldownSeconds;
    }

    public void BeginAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
    {
        if (!IsServer || !enabled || !_playerState.CanAct || _abilityCharges.Value <= 0)
        {
            return;
        }

        Collider2D[] playerHits = Physics2D.OverlapCircleAll(transform.position, _knockbackRadius, _playerLayer);

        bool hitOtherPlayer = false;
        foreach (Collider2D col in playerHits)
        {
            NetworkPlayerState targetState = col.GetComponentInParent<NetworkPlayerState>();
            if (targetState != null && targetState != _playerState && targetState.CanAct)
            {
                hitOtherPlayer = true;
                break;
            }
        }

        bool isGrounded = IsGroundedRaycast();

        if (!hitOtherPlayer && !isGrounded)
        {
            return;
        }

        if (_abilityCooldownTimer == 0f)
            _playerState.SetAbilityCooldownServer(_abilityCooldownSeconds);
        _abilityCharges.Value--;

        Vector2 launchVelocity = Vector2.up * _hammerLaunchForce;

        ApplyImpulseOwnerRpc(launchVelocity);

        if(_fumaceiroSound != null)
        {
            _fumaceiroSound.PlayFumaceiroSkill();
        }

        foreach (Collider2D hitCollider in playerHits)
        {
            NetworkPlayerState targetState = hitCollider.GetComponentInParent<NetworkPlayerState>();
            if (targetState != null && targetState != _playerState && targetState.CanAct)
            {
                Vector2 knockbackDir = ((Vector2)targetState.transform.position - (Vector2)transform.position).normalized;
                if (knockbackDir.sqrMagnitude < 0.01f)
                {
                    knockbackDir = Vector2.right;
                }

                targetState.ApplyKnockbackOwnerRpc(knockbackDir * _knockbackForce);
            }
        }
    }

    public void ShootReleaseServer()
    {
    }

    public void UpdateShootAimServer(Vector2 direction)
    {
    }

    public void EndAbilityServer(Vector2 aimDirection, Vector2 moveDirection)
    {
    }
    #endregion
    public void ClearProjectilesServer() { }
    private bool IsGroundedRaycast()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            return false;
        }

        Bounds bounds = col.bounds;
        Vector2 origin = new Vector2(bounds.center.x, bounds.min.y);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, _groundCheckDistance, _groundLayer);
        return hit.collider != null;
    }

    [Rpc(SendTo.Owner)]
    private void ApplyImpulseOwnerRpc(Vector2 velocity)
    {
        ApplyKnockbackImpulse(velocity, 0.15f);
    }

    [Rpc(SendTo.Owner)]
    private void ApplyKnockbackOwnerRpc(Vector2 velocity)
    {
        ApplyKnockbackImpulse(velocity, 0.25f);
    }

    private void ApplyKnockbackImpulse(Vector2 velocity, float disableDuration)
    {
        if (_rigidbody != null)
        {
            _rigidbody.linearVelocity = velocity;
        }

        if (_playerMove != null)
        {
            if (_knockbackDisableRoutine != null)
            {
                StopCoroutine(_knockbackDisableRoutine);
            }
            _knockbackDisableRoutine = StartCoroutine(DisableMovementTemporarilyRoutine(disableDuration));
        }
    }

    private IEnumerator DisableMovementTemporarilyRoutine(float duration)
    {
        _playerMove.enabled = false;
        yield return new WaitForSeconds(duration);
        _playerMove.enabled = true;
        _knockbackDisableRoutine = null;
    }

    private void HandleAmmoChanged(int previousValue, int currentValue)
    {
        RefreshAmmoIndicators();
    }

    private void RefreshAmmoIndicators()
    {
        for (int index = 0; index < _ammoIndicators.Length; index++)
        {
            bool visible = _playerState != null && _playerState.CanAct
                && index < _ammo.Value;
            _ammoIndicators[index].enabled = visible;
            _ammoIndicators[index].gameObject.SetActive(visible);
            if (visible) _ammoIndicators[index].color = _playerState.IndicatorColor;
        }
    }
    public void RefillAmmoServer()
    {
        if (!IsServer) return;
        _ammo.Value = _maximumAmmo;
        _abilityCharges.Value = _maxAbilityCharges;
        _playerState.SetAbilityCooldownServer(0f);
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _knockbackWireColor;
        Gizmos.DrawWireSphere(transform.position, _knockbackRadius);

        Collider2D col = GetComponent<Collider2D>();
        Vector3 origin = col != null ? new Vector3(col.bounds.center.x, col.bounds.min.y, 0f) : transform.position;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + Vector3.down * _groundCheckDistance);
    }
}
