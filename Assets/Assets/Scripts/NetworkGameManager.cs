
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkGameManager : MonoBehaviour
{
    [Header("Player Settings")]
    [SerializeField] private TMP_InputField playerNameInput;

    private readonly Dictionary<ulong, PlayerData> connectedPlayers = new();
    private readonly Dictionary<string, PlayerData> savedSessions = new();

    private NetworkManager subscribedManager;
    private string sessionId;
    private bool roundStarted = false;

    private void Awake()
    {
        sessionId = Guid.NewGuid().ToString();
    }

    private void OnEnable()
    {
        SubscribeToNetworkEvents();
    }

    private void Start()
    {
        // Also handles cases where NetworkManager was not
        // available when OnEnable was called.
        SubscribeToNetworkEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromNetworkEvents();
    }

    private void SubscribeToNetworkEvents()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || subscribedManager == manager)
            return;

        UnsubscribeFromNetworkEvents();

        manager.OnClientConnectedCallback += OnPlayerConnected;
        manager.OnClientDisconnectCallback += OnPlayerDisconnected;

        subscribedManager = manager;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (subscribedManager == null)
            return;

        subscribedManager.OnClientConnectedCallback -= OnPlayerConnected;
        subscribedManager.OnClientDisconnectCallback -= OnPlayerDisconnected;

        subscribedManager = null;
    }

    private PlayerData GetOrCreatePlayer(ulong clientId)
    {
        if (!connectedPlayers.TryGetValue(clientId, out PlayerData player))
        {
            player = new PlayerData
            {
                PlayerName = $"Player {clientId}"
            };

            connectedPlayers.Add(clientId, player);
        }

        return player;
    }

    private void OnPlayerConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
            return;

        PlayerData player = GetOrCreatePlayer(clientId);

        Debug.Log($"Player {player.PlayerName} registered. " +
                  $"Total players: {connectedPlayers.Count}");
    }

    private void OnPlayerDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
            return;

        if (!connectedPlayers.TryGetValue(clientId, out PlayerData player))
            return;

        if (!string.IsNullOrEmpty(player.SessionId))
        {
            savedSessions[player.SessionId] = player;

            Debug.Log($"Saved session for {player.PlayerName}, " +
                      $"Score: {player.Score}");
        }

        connectedPlayers.Remove(clientId);

        Debug.Log($"Player {player.PlayerName} disconnected. " +
                  $"Connected players: {connectedPlayers.Count}");
    }

    public void UpdatePlayerRecord(
        ulong clientId,
        string playerName,
        int score)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
            return;

        PlayerData player = GetOrCreatePlayer(clientId);

        player.PlayerName = playerName;
        player.Score = score;

        Debug.Log($"Updated player {clientId}: " +
                  $"{player.PlayerName}, Score: {player.Score}");
    }


    public void RegisterPlayerSession(
        ulong clientId,
        string newSessionId,
        string requestedName)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || !manager.IsServer)
            return;

        if (string.IsNullOrWhiteSpace(newSessionId))
        {
            Debug.LogWarning("Rejected empty session ID.");
            return;
        }

        if (!manager.ConnectedClients.TryGetValue(
            clientId, out var client))
        {
            return;
        }

        PlayerNetworkData networkData =
            client.PlayerObject?.GetComponent<PlayerNetworkData>();

        if (networkData == null)
            return;

        PlayerData player = GetOrCreatePlayer(clientId);

        if (!string.IsNullOrEmpty(player.SessionId))
        {
            if (player.SessionId == newSessionId)
                return;

            Debug.LogWarning(
                $"Client {clientId} attempted to change sessions.");
            return;
        }

        foreach (var entry in connectedPlayers)
        {
            if (entry.Key != clientId &&
                entry.Value.SessionId == newSessionId)
            {
                Debug.LogWarning("Session is already active.");
                return;
            }
        }

        player.SessionId = newSessionId;

        if (savedSessions.TryGetValue(
            newSessionId, out PlayerData savedPlayer))
        {
            player.PlayerName = savedPlayer.PlayerName;
            player.Score = savedPlayer.Score;
            player.Kills = savedPlayer.Kills;
            player.Deaths = savedPlayer.Deaths;

            networkData.RestorePlayerName(player.PlayerName);
            networkData.RestoreScore(player.Score);

            savedSessions.Remove(newSessionId);

            Debug.Log($"Restored player: {player.PlayerName}");
        }
        else
        {
            player.PlayerName = string.IsNullOrWhiteSpace(requestedName)
                ? $"Player {clientId}"
                : requestedName;

            networkData.RestorePlayerName(player.PlayerName);

            Debug.Log($"Registered new player: {player.PlayerName}");
        }
    }


    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (Keyboard.current == null ||
            !Keyboard.current.escapeKey.wasPressedThisFrame ||
            manager == null)
            return;

        if (manager.IsClient && !manager.IsHost)
        {
            manager.Shutdown();
        }
    }

    public void StartHost()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || manager.IsListening)
            return;

        if (!manager.StartHost())
        {
            Debug.LogError("Failed to start Host.");
        }
    }

    public void StartClient()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || manager.IsListening)
            return;

        if (!manager.StartClient())
        {
            Debug.LogError("Failed to start Client.");
        }
    }

    public void StopConnection()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager != null && manager.IsListening)
        {
            manager.Shutdown();
        }
    }

    public string GetPlayerName()
    {
        return playerNameInput != null
            ? playerNameInput.text
            : string.Empty;
    }

    public string GetSessionId()
    {
        return sessionId;
    }

    public bool TryGetSavedSession(
        string requestedSessionId,
        out PlayerData player)
    {
        if (string.IsNullOrWhiteSpace(requestedSessionId))
        {
            player = null;
            return false;
        }

        return savedSessions.TryGetValue(requestedSessionId, out player);
    }

    public void CheckAllPlayersReady()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || !manager.IsServer || roundStarted)
            return;

        // Require at least two connected players.
        if (manager.ConnectedClients.Count < 2)
            return;

        // Every connected player must be ready.
        foreach (NetworkClient client in manager.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                return;

            PlayerNetworkData player =
                client.PlayerObject.GetComponent<PlayerNetworkData>();

            if (player == null || !player.IsReady.Value)
                return;
        }

        roundStarted = true;

        Debug.Log("All players ready! Round starting...");
    }
}
