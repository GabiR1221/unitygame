using UnityEngine;
using Unity.Netcode;

public class MouseLook : NetworkBehaviour
{
    public float mouseSensitivity = 100f;
    public Transform playerBody;

    float xRotation = 0f;

    void Start()
    {
        // Blochează mouse-ul în mijlocul ecranului și îl face invizibil
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (!IsOwner) return;
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -70f, 70f); // Ne asigurăm că nu ne dăm capul peste cap

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);
    }
}
