using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI; // Dacă folosești butoane clasice

public class EmoteManager : NetworkBehaviour
{
    [Header("Referințe")]
    public Animator animator;
    public PlayerMovement movementScript;
    public GameObject emoteMenuUI; // Trage aici Canvas-ul/Panel-ul cu meniul de emote-uri

    private bool isMenuOpen = false;

    void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (movementScript == null) movementScript = GetComponent<PlayerMovement>();
        
        // Asigură-te că meniul e ascuns la început
        if (emoteMenuUI != null) emoteMenuUI.SetActive(false);
    }

    void Update()
    {
        if (!IsOwner) return;

        // Verificăm dacă jocul a început (opțional, dar recomandat)
        if (GameManager.instance == null || !GameManager.instance.isGameStarted.Value) return;
        
        // Nu putem face emote-uri dacă suntem ragdoll
        if (movementScript != null && movementScript.isRagdolled.Value) return;

        HandleInput();
    }

    private void HandleInput()
    {
        // Când APĂSĂM tasta B (sau o ținem apăsată)
        if (Input.GetKeyDown(KeyCode.B))
        {
            OpenEmoteMenu();
        }

        // Când ELIBERĂM tasta B
        if (Input.GetKeyUp(KeyCode.B))
        {
            CloseEmoteMenu();
        }
    }

    private void OpenEmoteMenu()
    {
        isMenuOpen = true;
        if (emoteMenuUI != null) emoteMenuUI.SetActive(true);

        // Oprim mișcarea camerei/jucătorului cât timp e în meniu (opțional, dar ajută la click-uri)
        if (movementScript != null) movementScript.canMove = false;
        
        // Deblocăm cursorul ca să putem da click pe butoane
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CloseEmoteMenu()
    {
        isMenuOpen = false;
        if (emoteMenuUI != null) emoteMenuUI.SetActive(false);

        // Reluăm mișcarea
        if (movementScript != null) movementScript.canMove = true;

        // Blocăm cursorul la loc pentru joc
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Această metodă va fi apelată de BUTOANELE tale din UI (On Click)
    public void PlayEmote(string animationTriggerName)
    {
        // Închidem meniul automat după ce ai ales un emote (dacă vrei)
        CloseEmoteMenu();

        // Trimitem cererea la server să ne animeze caracterul pe toate ecranele
        PlayEmoteServerRpc(animationTriggerName);
    }

    [ServerRpc]
    private void PlayEmoteServerRpc(string triggerName)
    {
        // Serverul primește cererea și o dă mai departe către TOȚI clienții
        PlayEmoteClientRpc(triggerName);
    }

    [ClientRpc]
    private void PlayEmoteClientRpc(string triggerName)
    {
        // Această bucată de cod rulează pe calculatoarele tuturor (inclusiv al tău)
        if (animator != null)
        {
            // Resetăm trigger-ele vechi ca să nu se încalece animațiile
            animator.ResetTrigger("Emote1"); 
            animator.ResetTrigger("Emote2"); // Adaugă aici numele tuturor trigger-elor tale

            // Declanșăm animația nouă
            animator.SetTrigger(triggerName);
        }
    }
}
