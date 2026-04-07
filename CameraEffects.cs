using UnityEngine;
using Unity.Netcode;

public class CameraEffects : NetworkBehaviour
{
    [Header("Head Bob Settings")]
    public float walkSpeed = 10f;
    public float sprintSpeed = 14f;
    public float bobAmount = 0.05f;
    
    [Header("Tilt Settings")]
    public float tiltAmount = 2f;
    public float tiltSpeed = 5f;

    private float defaultPosY = 0;
    private float timer = 0;
    private float currentTiltZ = 0; // Variabila noua pentru a stoca tilt-ul separat
    private PlayerMovement pm; 

    void Start()
    {
        if (!IsOwner) { enabled = false; return; }
        
        defaultPosY = transform.localPosition.y;
        pm = GetComponentInParent<PlayerMovement>();
    }

    void Update()
    {
        if (!IsOwner) return;

        float inputX = Input.GetAxis("Horizontal");
        float inputZ = Input.GetAxis("Vertical");
        float moveMagnitude = new Vector2(inputX, inputZ).magnitude;

        HandleHeadBob(moveMagnitude);
        HandleTilt(inputX);
    }

    void HandleHeadBob(float magnitude)
    {
        if (magnitude > 0.1f)
        {
            float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
            timer += Time.deltaTime * currentSpeed;
            
            transform.localPosition = new Vector3(
                transform.localPosition.x,
                defaultPosY + Mathf.Sin(timer) * bobAmount,
                transform.localPosition.z
            );
        }
        else
        {
            timer = 0;
            transform.localPosition = new Vector3(
                transform.localPosition.x,
                Mathf.Lerp(transform.localPosition.y, defaultPosY, Time.deltaTime * 8f),
                transform.localPosition.z
            );
        }
    }

    void HandleTilt(float xInput)
    {
        // 1. Calculam tinta tilt-ului (Z) separat de X si Y
        float targetTilt = -xInput * tiltAmount;

        // 2. Facem tranzitia doar pentru valoarea Z
        currentTiltZ = Mathf.Lerp(currentTiltZ, targetTilt, Time.deltaTime * tiltSpeed);

        // 3. Aplicam rotatia pastrand X si Y intacte (cele controlate de Mouse/MouseLook)
        // Folosim localEulerAngles pentru a modifica DOAR axa Z intr-un mod sigur
        Vector3 rot = transform.localEulerAngles;
        rot.z = currentTiltZ;
        transform.localRotation = Quaternion.Euler(rot);
    }
}
