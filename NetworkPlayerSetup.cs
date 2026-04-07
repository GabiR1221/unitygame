using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerSetup : NetworkBehaviour
{
    public GameObject playerModel; 
    public MonoBehaviour movementScript; 
    public Camera playerCamera;
    public AudioListener audioListener;
    
    [Header("Adaugă Spectator Camera aici din Inspector")]
    public GameObject spectatorCamera; 

    public override void OnNetworkSpawn()
    {
        // 1. DACA NU SUNTEM NOI (E alt player care a intrat in meci)
        if (!IsOwner)
        {
            // Oprim ABSOLUT TOT ce tine de camere si controale la ceilalti
            // ca sa nu ne uitam prin ochii lor sau sa ii miscam noi!
            if (playerCamera != null) playerCamera.gameObject.SetActive(false);
            if (spectatorCamera != null) spectatorCamera.SetActive(false);
            if (audioListener != null) audioListener.enabled = false;
            if (movementScript != null) movementScript.enabled = false;
            return; // Ne oprim aici pentru ceilalti playeri
        }

        // 2. DACA SUNTEM NOI (Owner)
        // La început, dezactivăm TOT ca sa vedem camera de Meniu
        DisablePlayer();
    }

    void Update()
    {
        if (!IsOwner) return;

        // Verificăm constant dacă a început jocul
        if (GameManager.instance != null && GameManager.instance.isGameStarted.Value)
        {
            // Pornim camera doar dacă obiectul nu este activ în ierarhie și dacă suntem în viață
            var state = GetComponent<PlayerState>();
            bool amAlive = state != null ? state.isAlive.Value : true;

            if (playerCamera != null && !playerCamera.gameObject.activeInHierarchy && amAlive)
            {
                EnablePlayer();
            }
        }
    }

    private void DisablePlayer()
    {
        if (playerCamera != null) 
        {
            playerCamera.enabled = false;
            playerCamera.gameObject.SetActive(false); // Oprim obiectul cu totul
        }
        if (spectatorCamera != null) spectatorCamera.SetActive(false);
        
        if (audioListener != null) audioListener.enabled = false;
        if (movementScript != null) movementScript.enabled = false;
        
        // Deblocăm mouse-ul pentru Lobby
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void EnablePlayer()
    {
        if (playerCamera != null) 
        {
            playerCamera.gameObject.SetActive(true);
            playerCamera.enabled = true;
        }
        if (audioListener != null) audioListener.enabled = true;
        if (movementScript != null) movementScript.enabled = true;

        // Blocăm mouse-ul pentru joc
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
