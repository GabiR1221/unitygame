using UnityEngine;

public class NPCInteractable : MonoBehaviour
{
    [Header("Setari Dialog")]
    public GameObject pressEPrompt; // Trage textul de "Press E" din UI aici
    
    [Tooltip("Aici scrii tot dialogul. Elementul 0 este prima replică.")]
    public DialogNode[] dialogNodes;

    private bool playerInRange = false;

    void Start()
    {
        if (pressEPrompt != null) pressEPrompt.SetActive(false);
    }

    void Update()
    {
        // Verificăm dacă jucătorul e în zonă, a apăsat E și NU e deja în dialog
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            if (!DialogManager.instance.IsDialogActive())
            {
                if (pressEPrompt != null) pressEPrompt.SetActive(false); // Ascundem "Press E"
                DialogManager.instance.StartDialog(dialogNodes);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            // Arătăm promptul doar dacă nu vorbim deja
            if (!DialogManager.instance.IsDialogActive() && pressEPrompt != null)
            {
                pressEPrompt.SetActive(true);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            if (pressEPrompt != null) pressEPrompt.SetActive(false);
            
            // Opțional: Oprește dialogul dacă jucătorul fuge de lângă NPC
            if (DialogManager.instance.IsDialogActive())
            {
                DialogManager.instance.EndDialog();
            }
        }
    }
}
