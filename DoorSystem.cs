using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections;

public class DoorSystem : NetworkBehaviour 
{
    [Header("Referinte Vizuale")]
    public Transform doorMesh;         
    public GameObject exitUI;          
    public Transform openPoint;        
    public Transform closedPoint;      
    
    [Header("Puncte de Camera")]
    public Transform doorViewPoint;    
    public Transform doorLeanPoint;    
    public Transform roomReturnPoint; 
    public Slider progressSlider;      

    [Header("Setari")]
    public float transitionSpeed = 5f; 
    public float closeSpeed = 2f;
    public float lookLimitX = 25f;
    public float lookLimitY = 18f;

    [Header("Setari Stamina Speciale")]
    [Tooltip("Cata stamina consuma pe secunda cand tii usa asta.")]
    public float doorStaminaConsumption = 25f; // Aici alegi cat vrei tu!

    [HideInInspector] public bool isAtDoor = false;
    private bool isTransitioning = false;
    
    public NetworkVariable<float> currentProgress = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> isDoorFullyClosed = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> isOccupied = new NetworkVariable<bool>(false);
    public NetworkVariable<ulong> occupierId = new NetworkVariable<ulong>(0);

    private GameObject currentPlayer;
    private Camera currentCamera;
    private float predictedProgress = 0f;

    public override void OnNetworkSpawn()
    {
        // RESETARE TOTALA la spawn pentru a opri teleportarea
        isAtDoor = false;
        isTransitioning = false;
        currentPlayer = null;
        currentCamera = null;

        if (IsServer)
        {
            isOccupied.Value = false;
            occupierId.Value = 999; // Un ID care nu exista
        }

        currentProgress.OnValueChanged += (oldValue, newValue) => 
        {
            if (!isAtDoor)
            {
                predictedProgress = newValue;
                UpdateDoorVisuals(newValue);
            }
        };
    
        UpdateDoorVisuals(currentProgress.Value);
    }

    void Update()
    {
        if (!isAtDoor) UpdateDoorVisuals(currentProgress.Value);

        if (!isAtDoor || isTransitioning || currentPlayer == null) return;
        // Verificare extra: daca suntem pe retea, doar cel care ocupa usa are voie sa ruleze HandleFNAF
        if (occupierId.Value != NetworkManager.Singleton.LocalClientId) return;

        PlayerMovement pm = currentPlayer.GetComponent<PlayerMovement>();
        
        // Verificam daca jucatorul tine mouse-ul si daca mai ARE stamina
        bool isHoldingDoor = Input.GetMouseButton(0) && (pm != null && !pm.IsExhausted);

        if (isHoldingDoor) 
        {
            predictedProgress += closeSpeed * Time.deltaTime;
            
            // REPARATIE: Acum folosim variabila noastra customizabila
            if (pm != null) pm.ConsumeStamina(doorStaminaConsumption); 
        }
        else 
        {
            predictedProgress -= closeSpeed * Time.deltaTime;
        }

        predictedProgress = Mathf.Clamp01(predictedProgress);
        if(progressSlider != null) progressSlider.value = predictedProgress;

        UpdateDoorVisuals(predictedProgress);
        SetDoorProgressServerRpc(predictedProgress);
        HandleFNAFLookAndLean();

        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) ExitDoor();
    }

    void UpdateDoorVisuals(float p)
    {
        if (doorMesh != null && openPoint != null && closedPoint != null)
        {
            doorMesh.localPosition = Vector3.Lerp(openPoint.localPosition, closedPoint.localPosition, p);
            doorMesh.localRotation = Quaternion.Slerp(openPoint.localRotation, closedPoint.localRotation, p);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetDoorProgressServerRpc(float p)
    {
        currentProgress.Value = p;
        isDoorFullyClosed.Value = (p >= 0.9f);
    }

    void HandleFNAFLookAndLean()
    {
        if (currentCamera == null) return;

        float mouseX = (Input.mousePosition.x / Screen.width * 2) - 1; 
        float mouseY = (Input.mousePosition.y / Screen.height * 2) - 1;

        Quaternion baseRotation = Quaternion.Lerp(doorViewPoint.rotation, doorLeanPoint.rotation, predictedProgress);
        Quaternion mouseOffset = Quaternion.Euler(-mouseY * lookLimitY, mouseX * lookLimitX, 0);
        Quaternion targetRot = baseRotation * mouseOffset;

        currentCamera.transform.rotation = Quaternion.Slerp(currentCamera.transform.rotation, targetRot, Time.deltaTime * 5f);

        if (doorLeanPoint != null)
        {
            currentPlayer.transform.position = Vector3.Lerp(doorViewPoint.position, doorLeanPoint.position, predictedProgress);
        }
    }

    public void ExitDoor()
    {
        if (isAtDoor && !isTransitioning) StartCoroutine(ExitDoorRoutine());
    }

    IEnumerator EnterDoorRoutine(GameObject player)
    {
        isTransitioning = true;
        currentPlayer = player;
        currentCamera = currentPlayer.GetComponentInChildren<Camera>();
        
        var pm = currentPlayer.GetComponent<PlayerMovement>();
        if(pm != null) pm.canMove = false; // Folosim canMove conform logicii anterioare

        var ml = currentCamera.GetComponent("MouseLook"); 
        if(ml != null) (ml as MonoBehaviour).enabled = false;

        SetOccupiedServerRpc(true, NetworkManager.Singleton.LocalClientId);

        Vector3 startPos = currentPlayer.transform.position;
        Quaternion startRot = currentPlayer.transform.rotation;

        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime * transitionSpeed;
            currentPlayer.transform.position = Vector3.Lerp(startPos, doorViewPoint.position, t);
            currentPlayer.transform.rotation = Quaternion.Slerp(startRot, doorViewPoint.rotation, t);
            yield return null;
        }

        isAtDoor = true;
        isTransitioning = false;
        
        if(progressSlider != null) progressSlider.gameObject.SetActive(true);
        if(exitUI != null) exitUI.SetActive(true); 

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    IEnumerator ExitDoorRoutine()
    {
        isTransitioning = true;
        isAtDoor = false;
        
        if(progressSlider != null) progressSlider.gameObject.SetActive(false);
        if(exitUI != null) exitUI.SetActive(false);

        Vector3 startPos = currentPlayer.transform.position;
        Quaternion startRot = currentCamera.transform.rotation;

        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime * transitionSpeed;
            currentPlayer.transform.position = Vector3.Lerp(startPos, roomReturnPoint.position, t);
            currentPlayer.transform.rotation = Quaternion.Slerp(startRot, roomReturnPoint.rotation, t);
            currentCamera.transform.localRotation = Quaternion.Slerp(currentCamera.transform.localRotation, Quaternion.identity, t);
            yield return null;
        }

        SetOccupiedServerRpc(false, 0);
        SetDoorProgressServerRpc(0);

        var pm = currentPlayer.GetComponent<PlayerMovement>();
        if(pm != null) pm.canMove = true;

        var ml = currentCamera.GetComponent("MouseLook");
        if(ml != null) (ml as MonoBehaviour).enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        isTransitioning = false;
        currentPlayer = null;
    }

    public void ForceExitThreshold()
    {
        StopAllCoroutines();
        isTransitioning = false;
        isAtDoor = false;
        predictedProgress = 0f; 

        if (progressSlider != null) progressSlider.gameObject.SetActive(false);
        if (exitUI != null) exitUI.SetActive(false);
    
        if (currentPlayer != null)
        {
            currentPlayer.transform.position = roomReturnPoint.position;
            currentPlayer.transform.rotation = roomReturnPoint.rotation;
            var pm = currentPlayer.GetComponent<PlayerMovement>();
            if(pm != null) pm.canMove = true;
            var ml = currentCamera.GetComponent("MouseLook");
            if(ml != null) (ml as MonoBehaviour).enabled = true;
        }

        if (IsClient) 
        { 
            SetOccupiedServerRpc(false, 0); 
            SetDoorProgressServerRpc(0f); 
        }
    
        currentPlayer = null; 
        currentCamera = null; 
        UpdateDoorVisuals(0f);
    }

    public void RequestInteraction(GameObject player)
    {
        // Verificăm dacă ușa e liberă și nu suntem deja în tranziție
        if (!isOccupied.Value && !isTransitioning)
        {
            // Pornim corutina de intrare
            StartCoroutine(EnterDoorRoutine(player));
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetOccupiedServerRpc(bool state, ulong clientId) { isOccupied.Value = state; occupierId.Value = clientId; }
}
