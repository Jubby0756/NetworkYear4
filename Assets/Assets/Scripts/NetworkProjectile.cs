using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(NetworkObject))]
public class NetworkProjectile : NetworkBehaviour
{
    [Header("Projectile Settings")]
    [SerializeField, Min(0f)]
    private float moveSpeed = 10f;

    [SerializeField, Min(0.1f)]
    private float lifetime = 3f;

    [SerializeField, Min(1)]
    private int damage = 10;

    private Rigidbody2D rb;

    private float despawnTime;
    private ulong shooterClientId;

    private bool hasHit;
    private bool shooterAssigned;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        rb.simulated = IsServer;

        hasHit = false;

        if (IsServer)
        {
            despawnTime = Time.time + lifetime;
        }
    }

    public override void OnNetworkDespawn()
    {
        hasHit = false;
        shooterAssigned = false;
    }

    public void SetShooter(ulong clientId)
    {
        // Only the server should assign projectile ownership.
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
            return;

        // Prevent the shooter being reassigned.
        if (shooterAssigned)
            return;

        shooterClientId = clientId;
        shooterAssigned = true;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer || hasHit)
            return;

        if (Time.time >= despawnTime)
        {
            DespawnProjectile();
            return;
        }

        if (moveSpeed <= 0f)
            return;

        Vector2 direction = transform.right;

        Vector2 newPosition =
            rb.position +
            direction * moveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsSpawned || !IsServer || hasHit)
            return;

        PlayerNetworkData target =
            other.GetComponentInParent<PlayerNetworkData>();

        if (!IsValidTarget(target))
            return;

        hasHit = true;

        bool wasAlive = target.IsAlive.Value;

        target.TakeDamage(damage);

        if (wasAlive && !target.IsAlive.Value)
        {
            AwardKillPoint();
        }

        DespawnProjectile();
    }

    private bool IsValidTarget(PlayerNetworkData target)
    {
        if (target == null || !target.IsSpawned)
            return false;

        if (!target.IsAlive.Value)
            return false;

        if (!shooterAssigned)
            return false;

        // Prevent players from damaging themselves.
        if (target.OwnerClientId == shooterClientId)
            return false;

        return true;
    }

    private void AwardKillPoint()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null)
            return;

        if (!manager.ConnectedClients.TryGetValue(
            shooterClientId,
            out NetworkClient shooterClient))
        {
            return;
        }

        PlayerNetworkData shooter =
            shooterClient.PlayerObject
                ?.GetComponent<PlayerNetworkData>();

        if (shooter != null && shooter.IsSpawned)
        {
            shooter.AwardPoint();
        }
    }

    private void DespawnProjectile()
    {
        if (!IsServer || !IsSpawned)
            return;

        NetworkObject.Despawn();
    }
}
