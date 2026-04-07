using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class PlayerMovement : NetworkBehaviour
{
    public CharacterController controller;

    [Header("Ragdoll Setup")]
    public Animator animator;
    public Transform ragdollRoot; 
    private Rigidbody[] ragdollRigidbodies;
    private Collider[] ragdollColliders;

    [Header("Referinte Vizuale")]
    public GameObject playerHead; // Trage AICI obiectul capului din ierarhie

    public NetworkVariable<bool> isRagdolled = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );

    [Header("Referinte UI")]
    public Slider staminaBar; 

    [HideInInspector] public bool canMove = true;

    [Header("Setari Miscare")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float gravity = -19.81f;

    [Header("Setari Stamina")]
    public float maxStamina = 100f;
    public float currentStamina; 
    public float consumptionRate = 30f; 
    public float regenerationRate = 15f;
    
    private Vector3 velocity;
    public bool IsActualSprinting { get; private set; }
    private bool isExhausted = false; 
    public bool IsExhausted => isExhausted;

    void Awake()
    {
        if (ragdollRoot != null)
        {
            ragdollRigidbodies = ragdollRoot.GetComponentsInChildren<Rigidbody>();
            ragdollColliders = ragdollRoot.GetComponentsInChildren<Collider>();
        }
        ToggleRagdollLocally(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isRagdolled.OnValueChanged += OnRagdollChanged;
        ToggleRagdollLocally(isRagdolled.Value);
    }

    public override void OnNetworkDespawn()
    {
        isRagdolled.OnValueChanged -= OnRagdollChanged;
    }

    private void OnRagdollChanged(bool previousValue, bool newValue)
    {
        ToggleRagdollLocally(newValue);
    }

    void Start()
    {
        currentStamina = maxStamina;

        if (IsOwner)
        {
            FindStaminaBar();
            ApplyHeadLayer(); 
        }
    }

    void ApplyHeadLayer()
    {
        if (playerHead != null)
        {
            int localPlayerLayer = LayerMask.NameToLayer("LocalPlayer");
            SetLayerRecursively(playerHead, localPlayerLayer);
        }
    }

    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        if (GameManager.instance == null || !GameManager.instance.isGameStarted.Value)
        {
            if (staminaBar != null && staminaBar.gameObject.activeSelf) 
                staminaBar.gameObject.SetActive(false);
            return;
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            isRagdolled.Value = !isRagdolled.Value;
        }

        if (isRagdolled.Value) return;

        if (staminaBar != null && !staminaBar.gameObject.activeSelf) 
            staminaBar.gameObject.SetActive(true);

        if (canMove) HandleMovement();
        HandleStamina();
    }

    void ToggleRagdollLocally(bool state)
    {
        if (animator != null) animator.enabled = !state;
        if (controller != null) controller.enabled = !state;

        if (ragdollRigidbodies == null) return;

        if (IsOwner && playerHead != null)
        {
            int layer = state ? LayerMask.NameToLayer("Default") : LayerMask.NameToLayer("LocalPlayer");
            SetLayerRecursively(playerHead, layer);
        }

        foreach (var rb in ragdollRigidbodies)
        {
            rb.isKinematic = !state;
        }

        foreach (var col in ragdollColliders)
        {
            col.enabled = state;
        }
    
        if (!state) AlignPositionToRagdoll();
    }

    void AlignPositionToRagdoll()
    {
        if (ragdollRigidbodies != null && ragdollRigidbodies.Length > 0)
        {
            Transform hips = ragdollRigidbodies[0].transform;
            transform.position = new Vector3(hips.position.x, hips.position.y + 0.2f, hips.position.z);
        }
    }

    void HandleMovement()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        Vector3 move = (transform.right * x + transform.forward * z);
        if (move.magnitude > 1f) move.Normalize(); 
        float speed = IsActualSprinting ? sprintSpeed : walkSpeed;
        controller.Move(move * speed * Time.deltaTime);
        if (controller.isGrounded && velocity.y < 0) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void HandleStamina()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        bool isMoving = (new Vector3(x, 0, z)).magnitude > 0.1f;
        if (currentStamina <= 0f) isExhausted = true;
        if (isExhausted && currentStamina > 30f) isExhausted = false;
        
        if (Input.GetKey(KeyCode.LeftShift) && isMoving && !isExhausted)
        {
            IsActualSprinting = true;
            
            // --- MODIFICARE AICI: Citim multiplicatorul din GameManager (1f = normal, 2f = dublu etc.) ---
            float multiplier = GameManager.instance != null ? GameManager.instance.currentStaminaMultiplier.Value : 1f;
            currentStamina -= (consumptionRate * multiplier) * Time.deltaTime;
        }
        else
        {
            IsActualSprinting = false;
            if (currentStamina < maxStamina)
                currentStamina += regenerationRate * Time.deltaTime;
        }
        
        currentStamina = Mathf.Clamp(currentStamina, 0, maxStamina);
        if (staminaBar != null) staminaBar.value = currentStamina;
    }

    public void ConsumeStamina(float amount)
    {
        if (!IsOwner) return;
        currentStamina -= amount * Time.deltaTime;
        if (currentStamina < 0) currentStamina = 0;
    }

    void FindStaminaBar()
    {
        if (staminaBar == null)
        {
            Slider[] allSliders = Resources.FindObjectsOfTypeAll<Slider>();
            foreach (Slider s in allSliders)
            {
                if (s.CompareTag("StaminaBar"))
                {
                    staminaBar = s;
                    break;
                }
            }
        }
        if (staminaBar != null)
        {
            staminaBar.maxValue = maxStamina;
            staminaBar.value = maxStamina;
            staminaBar.gameObject.SetActive(false); 
        }
    }
}
