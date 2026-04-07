using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class PlayerInteraction : NetworkBehaviour
{
    public Camera playerCamera; 
    public float interactDistance = 3f;
    public float proximityRadius = 2.0f; 
    
    private GameObject interactionPrompt; 
    private Text promptText; 

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Căutăm obiectul după nume, chiar dacă este DEZACTIVAT (fără bifă)
            interactionPrompt = FindInactiveObjectByName("InteractionUI");
            
            if (interactionPrompt != null)
            {
                promptText = interactionPrompt.GetComponentInChildren<Text>();
                // Îl forțăm să fie stins la început
                interactionPrompt.SetActive(false); 
            }
            else
            {
                Debug.LogError("NU AM GĂSIT InteractionUI! Verifică dacă numele este scris exact așa în Ierarhie.");
            }
        }
    }

    // Funcție specială care găsește obiecte stinse după nume
    GameObject FindInactiveObjectByName(string name)
    {
        Transform[] objs = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < objs.Length; i++)
        {
            if (objs[i].hideFlags == HideFlags.None && objs[i].name == name)
            {
                return objs[i].gameObject;
            }
        }
        return null;
    }

    void Update()
    {
        if (!IsOwner) return;

        // FIX MENIU: Dacă jocul NU a început (lobby), forțăm UI-ul să stea stins
        if (GameManager.instance != null && !GameManager.instance.isGameStarted.Value)
        {
            if (interactionPrompt != null && interactionPrompt.activeSelf) 
                interactionPrompt.SetActive(false);
            return;
        }

        CheckForInteractable(); 

        if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0))
        {
            PerformInteraction();
        }
    }

    void CheckForInteractable()
    {
        if (playerCamera == null || interactionPrompt == null) return;

        string message = "";

        // 1. Raycast (Prioritate pentru ce vedem direct)
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, interactDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            message = GetInteractableMessage(hit.collider.gameObject);
        }

        // 2. Proximitate (Dacă nu ne uităm direct la nimic)
        if (string.IsNullOrEmpty(message))
        {
            Collider[] nearby = Physics.OverlapSphere(transform.position, proximityRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            foreach (var col in nearby)
            {
                message = GetInteractableMessage(col.gameObject);
                if (!string.IsNullOrEmpty(message)) break;
            }
        }

        // Afișare UI
        if (!string.IsNullOrEmpty(message))
        {
            if (promptText != null) promptText.text = message;
            interactionPrompt.SetActive(true);
        }
        else
        {
            interactionPrompt.SetActive(false);
        }
    }

    string GetInteractableMessage(GameObject obj)
    {
        // --- VERIFICĂM CAMERELE ---
        // Folosim direct GetComponent (nu InParent) ca să nu detecteze ușa din spate
        CCTVSystem cctv = obj.GetComponent<CCTVSystem>();
        if (cctv != null) return "Press E to use"; 

        // --- VERIFICĂM UȘA ---
        DoorSystem door = obj.GetComponentInParent<DoorSystem>();
        if (door != null && !door.isOccupied.Value) 
        {
            // Dacă am găsit o ușă, ne asigurăm că obiectul lovit nu este totuși un buton de cameră
            if (obj.name.ToLower().Contains("camera") || obj.name.ToLower().Contains("button"))
                return "Press E to use";

            return "Press E to use Door";
        }

        // --- VERIFICAM SPAWNER-UL ---
        ItemSpawner spawner = obj.GetComponent<ItemSpawner>();
        if (spawner != null) return spawner.interactionMessage;
        
        return "";
    }

    void PerformInteraction()
    {
        if (playerCamera == null) return;
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            if (TryExecute(hit.collider.gameObject)) return;
        }

        Collider[] nearby = Physics.OverlapSphere(transform.position, proximityRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        foreach (var col in nearby)
        {
            if (TryExecute(col.gameObject)) return;
        }
    }

    bool TryExecute(GameObject obj)
    {
        // Prioritate executie Camere
        CCTVSystem cctv = obj.GetComponent<CCTVSystem>();
        if (cctv != null) { cctv.NextCamera(); return true; }

        // Execuție Ușă
        DoorSystem door = obj.GetComponentInParent<DoorSystem>();
        if (door != null && !door.isOccupied.Value) { door.RequestInteraction(gameObject); return true; }
        
        // --- EXECUTIE SPAWNER ---
        ItemSpawner spawner = obj.GetComponent<ItemSpawner>();
        if (spawner != null) 
        { 
            spawner.Interact(); 
            return true; 
        }

        return false;
    }
}
