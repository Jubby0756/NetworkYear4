using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerNetworkData))]
public class PlayerWeapon : NetworkBehaviour
{
    [Header("Weapon Settings")]
    [SerializeField, Min(0.01f)]
    private float fireInterval = 0.25f;

    [SerializeField, Min(0f)]
    private float projectileSpawnOffset = 0.65f;

    [SerializeField]
    private GameObject projectilePrefab;

    private PlayerNetworkData playerData;
    private NetworkProjectile projectileComponent;
    private NetworkObject projectileNetworkObject;

    private float nextFireTime;
    private float nextServerFireTime;

    private void Awake()
    {
        playerData = GetComponent<PlayerNetworkData>();
    }

    public override void OnNetworkSpawn()
    {
        nextFireTime = 0f;
        nextServerFireTime = 0f;

        if (IsServer)
        {
            ValidateProjectilePrefab();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
            return;

        if (!playerData.IsAlive.Value)
            return;

        if (Mouse.current == null)
            return;

        if (!Mouse.current.leftButton.isPressed)
            return;

        if (Time.time < nextFireTime)
            return;

        nextFireTime = Time.time + fireInterval;

        RequestFireRpc();
    }

    private void ValidateProjectilePrefab()
    {
        projectileComponent = null;
        projectileNetworkObject = null;

        if (projectilePrefab == null)
        {
            Debug.LogError(
                "PlayerWeapon: No projectile prefab assigned.");
            return;
        }

        if (!projectilePrefab.TryGetComponent(
            out projectileComponent))
        {
            Debug.LogError(
                "PlayerWeapon: Projectile prefab is missing NetworkProjectile.");
        }

        if (!projectilePrefab.TryGetComponent(
            out projectileNetworkObject))
        {
            Debug.LogError(
                "PlayerWeapon: Projectile prefab is missing NetworkObject.");
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestFireRpc()
    {
        if (!IsSpawned || !IsServer)
            return;

        if (!playerData.IsAlive.Value)
            return;

        if (projectilePrefab == null ||
            projectileComponent == null ||
            projectileNetworkObject == null)
            return;

        if (Time.time < nextServerFireTime)
            return;

        float angle = playerData.FacingAngle.Value;

        if (float.IsNaN(angle) || float.IsInfinity(angle))
            return;

        nextServerFireTime = Time.time + fireInterval;

        SpawnProjectile(angle);
    }

    private void SpawnProjectile(float angle)
    {
        Quaternion spawnRotation =
            Quaternion.Euler(0f, 0f, angle);

        Vector3 spawnPosition =
            transform.position +
            spawnRotation * Vector3.right * projectileSpawnOffset;

        GameObject projectile = Instantiate(
            projectilePrefab,
            spawnPosition,
            spawnRotation
        );

        NetworkProjectile projectileScript =
            projectile.GetComponent<NetworkProjectile>();

        NetworkObject networkObject =
            projectile.GetComponent<NetworkObject>();

        if (projectileScript == null || networkObject == null)
        {
            Debug.LogError(
                "PlayerWeapon: Spawned projectile is missing required components.");

            Destroy(projectile);
            return;
        }

        projectileScript.SetShooter(OwnerClientId);
        networkObject.Spawn();
    }
}
