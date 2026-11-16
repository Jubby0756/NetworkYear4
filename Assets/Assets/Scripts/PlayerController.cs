
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerNetworkData))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField, Min(0f)]
    private float moveSpeed = 5f;

    [Header("Aiming Settings")]
    [SerializeField] private Transform playerVisual;
    [SerializeField] private float aimAngleOffset = 0f;

    [Header("Network Optimisation")]
    [SerializeField, Min(0.01f)]
    private float aimUpdateInterval = 0.05f;

    [SerializeField, Range(0f, 180f)]
    private float aimChangeThreshold = 2f;

    [SerializeField, Min(0.01f)]
    private float movementRefreshInterval = 0.1f;

    [SerializeField, Min(0.1f)]
    private float movementTimeout = 0.5f;

    private Rigidbody2D rb;
    private PlayerNetworkData playerData;
    private Camera playerCamera;

    private Vector2 movementInput;
    private Vector2 serverMovementInput;
    private Vector2 lastSentMovementInput;

    private float lastSentAimAngle;
    private float nextAimUpdateTime;
    private float nextMovementRefreshTime;
    private float lastServerMovementTime;

    private bool hasSentAim;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerData = GetComponent<PlayerNetworkData>();
    }

    public override void OnNetworkSpawn()
    {
        rb.simulated = IsServer;

        ResetInputState();

        playerData.FacingAngle.OnValueChanged +=
            OnFacingAngleChanged;

        ApplyFacingAngle(playerData.FacingAngle.Value);

        if (IsOwner)
        {
            playerCamera = Camera.main;

            CameraFollow cameraFollow =
                FindFirstObjectByType<CameraFollow>();

            if (cameraFollow != null)
            {
                cameraFollow.SetTarget(transform);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        playerData.FacingAngle.OnValueChanged -=
            OnFacingAngleChanged;

        ResetInputState();

        playerCamera = null;
    }

    private void ResetInputState()
    {
        movementInput = Vector2.zero;
        serverMovementInput = Vector2.zero;
        lastSentMovementInput = Vector2.zero;

        lastSentAimAngle = 0f;
        nextAimUpdateTime = 0f;
        nextMovementRefreshTime = 0f;
        lastServerMovementTime = Time.time;

        hasSentAim = false;
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner)
            return;

        if (!playerData.IsAlive.Value)
        {
            StopMovement();
            return;
        }

        ReadMovementInput();
        SendMovementIfNeeded();
        HandleAiming();
    }

    private void ReadMovementInput()
    {
        if (Keyboard.current == null)
        {
            movementInput = Vector2.zero;
            return;
        }

        Vector2 direction = Vector2.zero;

        if (Keyboard.current.wKey.isPressed)
            direction.y += 1f;

        if (Keyboard.current.sKey.isPressed)
            direction.y -= 1f;

        if (Keyboard.current.aKey.isPressed)
            direction.x -= 1f;

        if (Keyboard.current.dKey.isPressed)
            direction.x += 1f;

        movementInput = direction.normalized;
    }

    private void SendMovementIfNeeded()
    {
        bool directionChanged =
            movementInput != lastSentMovementInput;

        // Refresh active movement so the server knows
        // the player is still holding a movement key.
        bool refreshDue =
            movementInput != Vector2.zero &&
            Time.time >= nextMovementRefreshTime;

        if (!directionChanged && !refreshDue)
            return;

        SubmitMovementRpc(movementInput);

        lastSentMovementInput = movementInput;

        nextMovementRefreshTime =
            Time.time + movementRefreshInterval;
    }

    private void StopMovement()
    {
        movementInput = Vector2.zero;

        if (lastSentMovementInput == Vector2.zero)
            return;

        SubmitMovementRpc(Vector2.zero);

        lastSentMovementInput = Vector2.zero;
    }

    [Rpc(SendTo.Server)]
    private void SubmitMovementRpc(Vector2 direction)
    {
        if (!IsSpawned || !IsServer ||
            !playerData.IsAlive.Value)
        {
            serverMovementInput = Vector2.zero;
            return;
        }

        // Reject malformed input before using it
        // in Rigidbody2D movement calculations.
        if (!IsFinite(direction.x) ||
            !IsFinite(direction.y))
        {
            serverMovementInput = Vector2.zero;
            return;
        }

        serverMovementInput =
            Vector2.ClampMagnitude(direction, 1f);

        lastServerMovementTime = Time.time;
    }

    [Rpc(SendTo.Server)]
    private void SubmitAimRpc(float angle)
    {
        if (!IsSpawned || !IsServer ||
            !playerData.IsAlive.Value)
            return;

        if (!IsFinite(angle))
            return;

        playerData.FacingAngle.Value =
            Mathf.Repeat(angle, 360f);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
            return;

        if (!playerData.IsAlive.Value)
        {
            serverMovementInput = Vector2.zero;
            return;
        }

        if (Time.time - lastServerMovementTime >
            movementTimeout)
        {
            serverMovementInput = Vector2.zero;
        }

        if (serverMovementInput == Vector2.zero)
            return;

        Vector2 newPosition =
            rb.position +
            serverMovementInput *
            moveSpeed *
            Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
    }

    private void HandleAiming()
    {
        if (playerVisual == null ||
            playerCamera == null ||
            Mouse.current == null)
            return;

        Vector2 mousePosition =
            Mouse.current.position.ReadValue();

        Vector3 mouseWorldPosition =
            playerCamera.ScreenToWorldPoint(mousePosition);

        Vector2 aimDirection =
            (Vector2)(mouseWorldPosition - transform.position);

        if (aimDirection.sqrMagnitude < 0.001f)
            return;

        float angle =
            Mathf.Atan2(aimDirection.y, aimDirection.x) *
            Mathf.Rad2Deg;

        // Immediate local feedback for the owning player.
        ApplyFacingAngle(angle);

        float angleDifference = Mathf.Abs(
            Mathf.DeltaAngle(lastSentAimAngle, angle)
        );

        bool angleChanged =
            !hasSentAim ||
            angleDifference >= aimChangeThreshold;

        if (!angleChanged ||
            Time.time < nextAimUpdateTime)
            return;

        SubmitAimRpc(angle);

        lastSentAimAngle = angle;
        nextAimUpdateTime =
            Time.time + aimUpdateInterval;

        hasSentAim = true;
    }

    private void OnFacingAngleChanged(
        float previousAngle,
        float newAngle)
    {
        if (!IsOwner)
        {
            ApplyFacingAngle(newAngle);
        }
    }

    private void ApplyFacingAngle(float angle)
    {
        if (playerVisual == null)
            return;

        playerVisual.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                angle + aimAngleOffset
            );
    }
}
