using UnityEngine;
using Unity.Netcode;

public class PlayerAnimationController : NetworkBehaviour
{
    [Header("Referinte Componente")]
    private Animator animator;
    private PlayerMovement movementScript;
    public Transform playerCamera;

    [Header("Referinte Oase")]
    [Tooltip("Trage aici osul capului (ex: Head)")]
    public Transform headBone;

    [Header("Setari Rotatie Cap")]
    [Tooltip("Ajusteaza daca vrei ca rotirea capului sa fie mai limitata decat camera")]
    public float lookMultiplier = 0.8f; 
    public float maxPitch = 60f;
    public float minPitch = -60f;

    // Sincronizam unghiul privirii pentru toti jucatorii
    private NetworkVariable<float> networkPitch = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        movementScript = GetComponent<PlayerMovement>();
        
        if (animator == null) Debug.LogError("EROARE: Nu am gasit Animatorul!");
        
        // Daca suntem owner, gasim camera principala
        if (IsOwner && playerCamera == null) playerCamera = Camera.main.transform;
    }

    void Update()
    {
        if (!IsOwner) return; 

        // 1. Sincronizam Pitch-ul Camerei
        if (playerCamera != null)
        {
            float pitch = playerCamera.localEulerAngles.x;
            if (pitch > 180) pitch -= 360f; // Convertim in range -180 la 180
            networkPitch.Value = pitch;
        }

        // 2. Logica de Blend Tree (Mers/Sprint)
        float x = Input.GetAxis("Horizontal"); 
        float z = Input.GetAxis("Vertical");
        float multiplier = (movementScript != null && movementScript.IsActualSprinting) ? 2f : 1f;

        if (animator != null)
        {
            animator.SetFloat("VelX", x * multiplier, 0.1f, Time.deltaTime);
            animator.SetFloat("VelZ", z * multiplier, 0.1f, Time.deltaTime);
        }
    }

    // LateUpdate se executa DUPA ce animatorul a miscat corpul
    void LateUpdate()
    {
        if (headBone == null) return;

        // Limitam unghiul sa nu isi rupa gatul (exorcist style)
        float finalPitch = Mathf.Clamp(networkPitch.Value * lookMultiplier, minPitch, maxPitch);

        // Cream rotatia pe axa "Right" a jucatorului (stanga-dreapta)
        // Folosim transform.right al player-ului pentru a ne asigura ca se uita sus-jos corect
        Quaternion headRotation = Quaternion.AngleAxis(finalPitch, transform.right);

        // Aplicam rotatia peste cea a animatiei
        headBone.rotation = headRotation * headBone.rotation;
    }
}
