
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNetworkData : NetworkBehaviour
{
    [Header("Player Network Data")]
    public NetworkVariable<FixedString64Bytes> PlayerName = new("Player");
    public NetworkVariable<int> Health = new(100);
    public NetworkVariable<bool> IsAlive = new(true);
    public NetworkVariable<float> RespawnTime = new(0f);
    public NetworkVariable<int> Score = new(0);
    public NetworkVariable<float> FacingAngle = new(0f);
    public NetworkVariable<bool> IsReady = new(false);

    [Header("Respawn Settings")]
    [SerializeField, Min(0.1f)]
    private float respawnDelay = 3f;

    [Header("Player UI")]
    [SerializeField] private TMP_Text playerInfoText;

    [Header("Player Visuals")]
    [SerializeField] private GameObject playerVisual;

    private const int MaxHealth = 100;

    private NetworkTransform networkTransform;
    private NetworkGameManager gameManager;
    private SpawnManager spawnManager;

    private void Awake()
    {
        networkTransform = GetComponent<NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        gameManager = FindFirstObjectByType<NetworkGameManager>();

        PlayerName.OnValueChanged += OnPlayerNameChanged;
        Health.OnValueChanged += OnHealthChanged;
        Score.OnValueChanged += OnScoreChanged;
        IsAlive.OnValueChanged += OnIsAliveChanged;
        IsReady.OnValueChanged += OnReadyChanged;

        if (IsServer)
        {
            spawnManager = FindFirstObjectByType<SpawnManager>();
        }

        if (IsOwner)
        {
            RegisterLocalPlayer();
        }

        UpdatePlayerInfo();
        UpdatePlayerVisibility(IsAlive.Value);
    }

    protected override void OnNetworkPostSpawn()
    {
        if (IsServer)
        {
            MoveToSpawnPoint();
        }
    }

    public override void OnNetworkDespawn()
    {
        PlayerName.OnValueChanged -= OnPlayerNameChanged;
        Health.OnValueChanged -= OnHealthChanged;
        Score.OnValueChanged -= OnScoreChanged;
        IsAlive.OnValueChanged -= OnIsAliveChanged;
        IsReady.OnValueChanged -= OnReadyChanged;
    }
    private void OnReadyChanged(bool previousValue, bool newValue)
    {
        UpdatePlayerInfo();
    }

    private void Update()
    {
        if (IsSpawned &&
    IsOwner &&
    Keyboard.current != null &&
    Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ToggleReadyRpc();
        }
        if (!IsSpawned || IsAlive.Value)
            return;

        UpdateRespawnDisplay();

        if (IsServer &&
            NetworkManager != null &&
            NetworkManager.ServerTime.Time >= RespawnTime.Value)
        {
            Respawn();
        }
    }

    private void RegisterLocalPlayer()
    {
        string chosenName = gameManager != null
            ? gameManager.GetPlayerName().Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(chosenName))
        {
            chosenName = $"Player {OwnerClientId}";
        }

        if (gameManager == null)
        {
            Debug.LogWarning("NetworkGameManager not found.");
            return;
        }

        RegisterSessionRpc(
            new FixedString64Bytes(gameManager.GetSessionId()),
            new FixedString64Bytes(chosenName)
        );
    }

    [Rpc(SendTo.Server)]
    private void RegisterSessionRpc(
        FixedString64Bytes sessionId,
        FixedString64Bytes requestedName)
    {
        if (!IsServer || gameManager == null)
            return;

        gameManager.RegisterPlayerSession(
            OwnerClientId,
            sessionId.ToString(),
            requestedName.ToString()
        );
    }

    public void TakeDamage(int damage)
    {
        if (!IsServer || damage <= 0 || !IsAlive.Value)
            return;

        Health.Value = Mathf.Max(0, Health.Value - damage);

        if (Health.Value == 0)
        {
            Die();
        }
    }

    public void AwardPoint()
    {
        if (!IsServer)
            return;

        Score.Value++;
        UpdateServerPlayerRecord();
    }

    public void RestoreScore(int score)
    {
        if (!IsServer)
            return;

        Score.Value = Mathf.Max(0, score);
    }

    public void RestorePlayerName(string name)
    {
        if (!IsServer || string.IsNullOrWhiteSpace(name))
            return;

        PlayerName.Value = new FixedString64Bytes(name);
    }

    private void UpdateServerPlayerRecord()
    {
        if (!IsServer || gameManager == null)
            return;

        gameManager.UpdatePlayerRecord(
            OwnerClientId,
            PlayerName.Value.ToString(),
            Score.Value
        );
    }

    private void Die()
    {
        if (!IsServer || !IsAlive.Value)
            return;

        IsAlive.Value = false;

        RespawnTime.Value =
            (float)NetworkManager.ServerTime.Time + respawnDelay;
    }

    private void Respawn()
    {
        if (!IsServer || IsAlive.Value)
            return;

        MoveToSpawnPoint();

        Health.Value = MaxHealth;
        IsAlive.Value = true;
    }

    private void MoveToSpawnPoint()
    {
        if (!IsServer)
            return;

        if (spawnManager == null || networkTransform == null)
        {
            Debug.LogWarning(
                "Cannot spawn player: missing SpawnManager or NetworkTransform.");
            return;
        }

        if (!networkTransform.CanCommitToTransform)
        {
            Debug.LogWarning(
                "Cannot teleport player without NetworkTransform authority.");
            return;
        }

        Vector3 spawnPosition = spawnManager.GetRandomSpawnPosition();

        networkTransform.Teleport(
            spawnPosition,
            transform.rotation,
            transform.localScale
        );
    }

    private void UpdatePlayerInfo()
    {
        if (playerInfoText == null)
            return;

        string readyStatus = IsReady.Value
            ? "READY"
            : "NOT READY";

        playerInfoText.text =
            $"{PlayerName.Value}\n" +
            $"HP: {Health.Value} | Score: {Score.Value}\n" +
            readyStatus;
    }

    private void UpdateRespawnDisplay()
    {
        if (playerInfoText == null || NetworkManager == null)
            return;

        float remainingTime = Mathf.Max(
            0f,
            RespawnTime.Value - (float)NetworkManager.ServerTime.Time
        );

        playerInfoText.text =
            $"{PlayerName.Value}\n" +
            $"Respawning in {remainingTime:F1}s";
    }

    private void UpdatePlayerVisibility(bool isAlive)
    {
        if (playerVisual != null)
        {
            playerVisual.SetActive(isAlive);
        }
    }

    private void OnPlayerNameChanged(
        FixedString64Bytes previousValue,
        FixedString64Bytes newValue)
    {
        UpdatePlayerInfo();
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        UpdatePlayerInfo();
    }

    private void OnScoreChanged(int previousValue, int newValue)
    {
        UpdatePlayerInfo();
    }

    private void OnIsAliveChanged(bool previousValue, bool newValue)
    {
        UpdatePlayerVisibility(newValue);

        if (newValue)
        {
            UpdatePlayerInfo();
        }
    }

    [Rpc(SendTo.Server)]
    private void ToggleReadyRpc()
    {
        if (!IsServer)
            return;

        IsReady.Value = !IsReady.Value;

        if (gameManager != null)
        {
            gameManager.CheckAllPlayersReady();
        }
    }
}
