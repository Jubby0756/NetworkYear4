using System.Collections.Generic;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField]
    private Transform[] spawnPoints;

    private readonly List<Transform> validSpawnPoints = new();

    private void Awake()
    {
        CacheSpawnPoints();
    }

    private void CacheSpawnPoints()
    {
        validSpawnPoints.Clear();

        if (spawnPoints == null)
        {
            Debug.LogWarning(
                "SpawnManager: No spawn points configured.");
            return;
        }

        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint != null)
            {
                validSpawnPoints.Add(spawnPoint);
            }
        }

        if (validSpawnPoints.Count == 0)
        {
            Debug.LogWarning(
                "SpawnManager: No valid spawn points available.");
        }
    }

    public Vector3 GetRandomSpawnPosition()
    {
        if (validSpawnPoints.Count == 0)
        {
            return Vector3.zero;
        }

        int randomIndex =
            Random.Range(0, validSpawnPoints.Count);

        Transform selectedSpawnPoint =
            validSpawnPoints[randomIndex];

        // Handle a spawn point destroyed after caching.
        if (selectedSpawnPoint == null)
        {
            CacheSpawnPoints();

            if (validSpawnPoints.Count == 0)
            {
                return Vector3.zero;
            }

            randomIndex =
                Random.Range(0, validSpawnPoints.Count);

            selectedSpawnPoint =
                validSpawnPoints[randomIndex];
        }

        return selectedSpawnPoint.position;
    }
}
