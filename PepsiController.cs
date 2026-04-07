using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PepsiController : NetworkBehaviour
{
    [Header("Animator / input")]
    [Tooltip("Numele trigger-ului din Animator care pornește tranzitia (ex: 'toggle')")]
    public string animatorToggleTrigger = "toggle";

    [Tooltip("Tasta pentru toggle")]
    public KeyCode toggleKey = KeyCode.F;

    [Tooltip("Durata animației (sec) — folosită pentru suprimarea IK)")]
    public float toggleAnimationDuration = 0.5f;

    [Header("Sprint effect")]
    [Tooltip("Multiplicatorul vitezei de sprint aplicat după consum (ex: 3 = triple speed)")]
    public float sprintMultiplier = 3f;

    [Tooltip("Durata boost-ului de sprint (secunde)")]
    public float sprintDuration = 10f;

    [Header("Camera Attachment")]
    [Tooltip("Transform-ul capului (de obicei în Armature) – camera va urmări acest bone pe durata animației.")]
    public Transform headBone;
    [Tooltip("Referința la camera jucătorului (de obicei Camera.main).")]
    public Transform playerCamera;
    [Tooltip("Scriptul care controlează camera (ex: FirstPersonController). Acesta va fi dezactivat pe durata animației.")]
    public MonoBehaviour cameraController;

    private bool isToggling = false;

    private NetworkVariable<bool> toggleOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    Inventory inventory;
    Animator animator;

    private Coroutine ikCoroutine;
    private Coroutine ownerEffectCoroutine;
    private Coroutine cameraAttachCoroutine;

    private void Awake()
    {
        inventory = GetComponent<Inventory>();
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (playerCamera == null && Camera.main != null)
            playerCamera = Camera.main.transform;
    }

    public override void OnNetworkSpawn()
    {
        if (inventory != null && inventory.currentEquippedItemDBIndex != null)
        {
            inventory.currentEquippedItemDBIndex.OnValueChanged += OnEquippedItemChanged;
        }

        toggleOn.OnValueChanged += OnToggleNetworkChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (inventory == null) return;
        if (inventory.currentEquippedItemDBIndex == null) return;

        int cur = inventory.currentEquippedItemDBIndex.Value;
        if (cur == -1) return;

        if (IsControllerAllowedForItem(cur))
        {
            if (Input.GetKeyDown(toggleKey))
            {
                if (isToggling) return;

                Debug.Log("[PepsiController] Toggle pressed. current = " + toggleOn.Value);
                toggleOn.Value = !toggleOn.Value;
            }
        }
    }

    private void OnEquippedItemChanged(int oldIdx, int newIdx)
    {
        if (oldIdx != -1 && IsItemToggleable(oldIdx))
        {
            if (IsOwner && toggleOn.Value)
            {
                toggleOn.Value = false;
            }
        }

        // MODIFICAT: dacă noul item nu mai e controlat de PepsiController, resetăm flag-ul global
        if (newIdx == -1 || !IsControllerAllowedForItem(newIdx))
        {
            isToggling = false;
            if (ikCoroutine != null) { StopCoroutine(ikCoroutine); ikCoroutine = null; }
            if (cameraAttachCoroutine != null) { StopCoroutine(cameraAttachCoroutine); cameraAttachCoroutine = null; }
            if (inventory != null) inventory.isAnyToggleInProgress = false;
            if (cameraController != null)
                cameraController.enabled = true;
        }
    }

    private void OnToggleNetworkChanged(bool oldValue, bool newValue)
    {

        if (animator != null && !string.IsNullOrEmpty(animatorToggleTrigger))
        {
            animator.SetTrigger(animatorToggleTrigger);
        }

        if (inventory != null)
        {
            if (ikCoroutine != null) StopCoroutine(ikCoroutine);
            isToggling = true;
            // MODIFICAT: semnalăm Inventory că a început un toggle
            inventory.isAnyToggleInProgress = true;
            ikCoroutine = StartCoroutine(SuppressIKFor(toggleAnimationDuration));
        }

        if (IsOwner && newValue == true)
        {
            if (cameraAttachCoroutine != null) StopCoroutine(cameraAttachCoroutine);
            cameraAttachCoroutine = StartCoroutine(AttachCameraDuringAnimation());

            if (ownerEffectCoroutine != null) StopCoroutine(ownerEffectCoroutine);
            ownerEffectCoroutine = StartCoroutine(OwnerPepsiEffect());
        }
    }

    private IEnumerator SuppressIKFor(float duration)
    {
        if (inventory != null) inventory.isIKSuppressed = true;
        yield return null; // un frame pentru a aplica suprimarea
        yield return new WaitForSeconds(duration);
        if (inventory != null) inventory.isIKSuppressed = false;

        // resetăm flag-urile
        isToggling = false;
        if (inventory != null) inventory.isAnyToggleInProgress = false;
        ikCoroutine = null;
    }

    private IEnumerator AttachCameraDuringAnimation()
    {
        if (playerCamera == null)
        {
            Debug.LogWarning("[PepsiController] Camera not assigned. Cannot attach.");
            yield break;
        }

        if (headBone == null)
        {
            Debug.LogWarning("[PepsiController] Head bone not assigned. Cannot attach camera.");
            yield break;
        }

        // Salvează părintele original și poziția/rotația locală față de acesta
        Transform originalParent = playerCamera.parent;
        Vector3 originalLocalPos = playerCamera.localPosition;
        Quaternion originalLocalRot = playerCamera.localRotation;

        // Salvează poziția și rotația globală curentă
        Vector3 worldPos = playerCamera.position;
        Quaternion worldRot = playerCamera.rotation;

        // Atașează camera la headBone
        playerCamera.SetParent(headBone);

        // Restaurează poziția și rotația globală (astfel camera nu se mișcă deloc în momentul atașării)
        playerCamera.position = worldPos;
        playerCamera.rotation = worldRot;

        // Dezactivează controlul camerei
        if (cameraController != null)
            cameraController.enabled = false;

        // Așteaptă durata animației
        yield return new WaitForSeconds(toggleAnimationDuration);

        // Reatașează la părintele original și restaurează poziția/rotația locală
        playerCamera.SetParent(originalParent);
        playerCamera.localPosition = originalLocalPos;
        playerCamera.localRotation = originalLocalRot;

        // Reactivează controlul camerei
        if (cameraController != null)
            cameraController.enabled = true;

        cameraAttachCoroutine = null;
    }

    // Owner-only coroutine: așteaptă sfârșitul animației, șterge itemul din inventar (silent) și pornește sprint boost
    private IEnumerator OwnerPepsiEffect()
    {
        // Așteaptă exact cât durează animația
        yield return new WaitForSeconds(toggleAnimationDuration);

        // Un mic delay pentru a permite finalizarea oricăror actualizări (inclusiv resetarea IK)
        yield return null;

        // Elimină itemul
        if (inventory != null)
        {
            bool removed = inventory.RemoveEquippedItemSilent();
            Debug.Log("[PepsiController] RemoveEquippedItemSilent returned: " + removed);
        }

        // Aplică sprint boost (dacă există componenta)
        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm != null)
        {
            float originalSprint = pm.sprintSpeed;
            pm.sprintSpeed = originalSprint * sprintMultiplier;
            Debug.Log($"[PepsiController] Sprint boosted: {originalSprint} -> {pm.sprintSpeed} for {sprintDuration}s");

            float elapsed = 0f;
            while (elapsed < sprintDuration)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            pm.sprintSpeed = originalSprint;
            Debug.Log("[PepsiController] Sprint boost ended, restored sprintSpeed to: " + originalSprint);
        }
        else
        {
            Debug.LogWarning("[PepsiController] PlayerMovement component not found — sprint boost skipped.");
        }

        ownerEffectCoroutine = null;
    }

    private bool IsItemToggleable(int dbIndex)
    {
        if (inventory == null || inventory.allItemsDatabase == null) return false;
        if (dbIndex < 0 || dbIndex >= inventory.allItemsDatabase.Count) return false;
        return inventory.allItemsDatabase[dbIndex].isToggleable;
    }

    private bool IsControllerAllowedForItem(int dbIndex)
    {
        if (inventory == null || inventory.allItemsDatabase == null) return false;
        if (dbIndex < 0 || dbIndex >= inventory.allItemsDatabase.Count) return false;

        var item = inventory.allItemsDatabase[dbIndex];

        if (item.controller != ItemController.None)
        {
            return item.controller == ItemController.Pepsi;
        }

        return item.isToggleable;
    }

    private void OnDestroy()
    {
        if (inventory != null && inventory.currentEquippedItemDBIndex != null)
            inventory.currentEquippedItemDBIndex.OnValueChanged -= OnEquippedItemChanged;

        toggleOn.OnValueChanged -= OnToggleNetworkChanged;

        if (ikCoroutine != null) StopCoroutine(ikCoroutine);
        if (ownerEffectCoroutine != null) StopCoroutine(ownerEffectCoroutine);
        if (cameraAttachCoroutine != null) StopCoroutine(cameraAttachCoroutine);
        if (inventory != null) inventory.isIKSuppressed = false;
        // MODIFICAT: asigurăm resetarea flag-ului global
        if (inventory != null) inventory.isAnyToggleInProgress = false;
        if (cameraController != null)
            cameraController.enabled = true;
    }
}
