using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Linq;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems; // pentru RectTransformUtility

public class PhoneController : NetworkBehaviour
{
    [Header("Setup")]
    [Tooltip("Numele trigger-ului din Animator care pornește tranzitia 'flashlighttoggle'")]
    public string animatorToggleTrigger = "flashlighttoggle";

    [Tooltip("Numele trigger-ului din Animator pentru aprins ecran (right click)")]
    public string animatorScreenToggleTrigger = "screentoggle";

    [Tooltip("Tasta pentru toggle flashlight")]
    public KeyCode toggleKey = KeyCode.F;

    [Tooltip("Durata animației de toggle (în secunde). Seteaz-o să se potrivească animației din Animator)")]
    public float toggleAnimationDuration = 0.5f;

    [Header("Optional (dacă vrei să setezi manual)")]
    [Tooltip("Dacă ai un GameObject 'screen' în ierarhie, poți trage referința aici. Dacă e null, scriptul va încerca să îl găsească în itemModel.")]
    public GameObject screenObject;

    [Header("Phone Messages")]
    [Tooltip("Lista de GameObjects (de ex. panouri cu text) care corespund indicilor mesajelor. Fiecare element TREBUIE să aibă o componentă TextMeshProUGUI (textul se va lua de acolo).")]
    public List<GameObject> messageDisplays;

    [Tooltip("Referință către ScrollView-ul care conține mesajele (pentru a permite scroll cu rotita).")]
    public ScrollRect messageScrollView;

    [Header("Scroll Settings")]
    [Tooltip("Sensibilitatea scroll-ului cu rotita.")]
    public float scrollSensitivity = 0.1f;

    [Header("Notification Sound")]
    [Tooltip("Sunetul redat când sosește un mesaj nou.")]
    public AudioClip notificationSound;
    [Tooltip("AudioSource folosit pentru redarea sunetului. Dacă nu este setat, scriptul va încerca să găsească un AudioSource pe acest GameObject.")]
    public AudioSource notificationAudioSource;

    private bool isToggling = false;

    // Networked state (owner poate scrie, toți pot citi)
    private NetworkVariable<bool> flashlightOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<bool> screenOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    Inventory inventory;
    Animator animator;

    private const float visibilityDelay = 0.15f; // potrivit cu Inventory.ChangeVisibilityDelay

    // referință la coroutine pentru a o anula dacă e nevoie
    private Coroutine ikCoroutine;

    private void Awake()
    {
        inventory = GetComponent<Inventory>();
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // Dacă nu avem AudioSource setat, încercăm să îl obținem
        if (notificationAudioSource == null)
            notificationAudioSource = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        if (inventory != null && inventory.currentEquippedItemDBIndex != null)
        {
            inventory.currentEquippedItemDBIndex.OnValueChanged += OnEquippedItemChanged;
        }

        flashlightOn.OnValueChanged += OnFlashlightNetworkChanged;
        screenOn.OnValueChanged += OnScreenNetworkChanged;

        // Asigur starea inițială
        StartCoroutine(ApplyVisualsAfterDelay(0.05f));
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (inventory == null || inventory.currentEquippedItemDBIndex == null) return;

        int cur = inventory.currentEquippedItemDBIndex.Value;
        if (cur == -1) return;

        if (IsControllerAllowedForItem(cur))
        {
            // flashlight toggle (F)
            if (Input.GetKeyDown(toggleKey))
            {
                // MODIFICAT: dacă suntem deja în toggling, ignorăm
                if (isToggling) return;

                Debug.Log("[PhoneController] Toggle key pressed (owner). Current network value: " + flashlightOn.Value);
                flashlightOn.Value = !flashlightOn.Value;
                Debug.Log("[PhoneController] flashlightOn set to: " + flashlightOn.Value);
            }

            // screen toggle (right click)
            if (Input.GetMouseButtonDown(1))
            {
                // MODIFICAT: dacă suntem deja în toggling, ignorăm
                if (isToggling) return;

                Debug.Log("[PhoneController] Right-click toggle (owner). Current screen value: " + screenOn.Value);
                screenOn.Value = !screenOn.Value;
                Debug.Log("[PhoneController] screenOn set to: " + screenOn.Value);
            }
        }

        // Handle scroll wheel for ScrollView when screen is on (regardless of mouse position)
        if (screenOn.Value && messageScrollView != null)
        {
            float scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                // Ajustăm poziția verticală; înmulțim cu sensibilitatea
                messageScrollView.verticalNormalizedPosition += scrollDelta * scrollSensitivity;
                // Limităm între 0 și 1
                messageScrollView.verticalNormalizedPosition = Mathf.Clamp01(messageScrollView.verticalNormalizedPosition);
            }
        }
    }

    private void OnEquippedItemChanged(int oldIdx, int newIdx)
    {
        Debug.Log($"[PhoneController] OnEquippedItemChanged old:{oldIdx} new:{newIdx}");

        if (oldIdx != -1 && IsItemToggleable(oldIdx))
        {
            SetLightForIndex(oldIdx, false);
            SetScreenForIndex(oldIdx, false);

            if (IsOwner)
            {
                if (flashlightOn.Value) flashlightOn.Value = false;
                if (screenOn.Value) screenOn.Value = false;
            }
        }

        if (newIdx != -1 && IsItemToggleable(newIdx))
        {
            StartCoroutine(ApplyVisualsAfterDelay(visibilityDelay));
        }

        if (newIdx == -1 || !IsControllerAllowedForItem(newIdx))
        {
            isToggling = false;
            if (ikCoroutine != null) { StopCoroutine(ikCoroutine); ikCoroutine = null; }
            if (inventory != null) inventory.isAnyToggleInProgress = false;
        }

        if (newIdx == -1 && inventory != null)
        {
            inventory.isIKSuppressed = false;
        }
    }

    private void OnFlashlightNetworkChanged(bool oldValue, bool newValue)
    {

        if (inventory != null && inventory.currentEquippedItemDBIndex != null)
        {
            int cur = inventory.currentEquippedItemDBIndex.Value;
            if (cur != -1 && IsItemToggleable(cur))
            {
                SetLightForIndex(cur, newValue);
            }
        }

        if (animator != null && !string.IsNullOrEmpty(animatorToggleTrigger))
        {
            animator.SetTrigger(animatorToggleTrigger);

            if (ikCoroutine != null) StopCoroutine(ikCoroutine);
            isToggling = true;
            // MODIFICAT: semnalăm Inventory că a început un toggle
            if (inventory != null) inventory.isAnyToggleInProgress = true;
            ikCoroutine = StartCoroutine(SmoothIKDip());
        }
    }

    private void OnScreenNetworkChanged(bool oldValue, bool newValue)
    {

        if (inventory != null && inventory.currentEquippedItemDBIndex != null)
        {
            int cur = inventory.currentEquippedItemDBIndex.Value;
            if (cur != -1 && IsItemToggleable(cur))
            {
                SetScreenForIndex(cur, newValue);
            }
        }

        if (animator != null && !string.IsNullOrEmpty(animatorScreenToggleTrigger))
        {
            animator.SetTrigger(animatorScreenToggleTrigger);

            if (ikCoroutine != null) StopCoroutine(ikCoroutine);
            isToggling = true;
            // MODIFICAT: semnalăm Inventory că a început un toggle
            if (inventory != null) inventory.isAnyToggleInProgress = true;
            ikCoroutine = StartCoroutine(SmoothIKDip());
        }
    }

    public void ReceiveMessageByIndex(int index)
    {
        if (!IsOwner) return;

        Debug.Log($"[PhoneController] Received message index: {index}");

        // Activăm doar noul mesaj (dacă există), lăsând celelalte active
        if (messageDisplays != null && index >= 0 && index < messageDisplays.Count)
        {
            GameObject targetDisplay = messageDisplays[index];
            if (targetDisplay != null)
            {
                targetDisplay.SetActive(true);

                // Dacă există un ScrollView, îl activăm și resetăm poziția de scroll la început
                if (messageScrollView != null)
                {
                    messageScrollView.gameObject.SetActive(true);
                    Canvas.ForceUpdateCanvases();
                    messageScrollView.verticalNormalizedPosition = 1f; // sus
                }
            }
        }

        // Redă sunetul de notificare
        PlayNotificationSound();
    }

    private void PlayNotificationSound()
    {
        if (notificationSound == null) return;

        // Dacă avem AudioSource, redăm sunetul
        if (notificationAudioSource != null)
        {
            notificationAudioSource.PlayOneShot(notificationSound);
        }
        else
        {
            // Fallback: creează un obiect temporar pentru a reda sunetul (mai puțin eficient)
            AudioSource.PlayClipAtPoint(notificationSound, transform.position);
        }
    }

    private IEnumerator SmoothIKDip()
    {
        if (inventory != null) inventory.isIKSuppressed = true;
        yield return null;
        yield return new WaitForSeconds(toggleAnimationDuration);
        if (inventory != null) inventory.isIKSuppressed = false;

        // MODIFICAT: resetăm flag-urile
        isToggling = false;
        if (inventory != null) inventory.isAnyToggleInProgress = false;
        ikCoroutine = null;
    }

    private IEnumerator ApplyVisualsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (inventory == null || inventory.currentEquippedItemDBIndex == null) yield break;

        int cur = inventory.currentEquippedItemDBIndex.Value;
        if (cur == -1) yield break;

        // Aplicăm starea actuală a flashlight & screen după delay
        SetLightForIndex(cur, flashlightOn.Value);
        SetScreenForIndex(cur, screenOn.Value);
    }

    private bool IsItemToggleable(int dbIndex)
    {
        if (inventory == null || inventory.allItemsDatabase == null) return false;
        if (dbIndex < 0 || dbIndex >= inventory.allItemsDatabase.Count) return false;
        return inventory.allItemsDatabase[dbIndex].isToggleable;
    }

    private void SetLightForIndex(int dbIndex, bool on)
    {
        if (inventory == null || inventory.allItemsDatabase == null) return;
        if (dbIndex < 0 || dbIndex >= inventory.allItemsDatabase.Count) return;

        Light target = inventory.allItemsDatabase[dbIndex].itemLight;
        if (target == null && inventory.allItemsDatabase[dbIndex].itemModel != null)
        {
            target = inventory.allItemsDatabase[dbIndex].itemModel.GetComponentInChildren<Light>(true);
        }

        if (target != null)
        {
            target.enabled = on;
        }
        else
        {
            Debug.LogWarning("[PhoneController] No Light found for itemDBIndex " + dbIndex);
        }
    }

    private void SetScreenForIndex(int dbIndex, bool on)
    {
        // Dacă ai setat manual screenObject în inspector, îl folosim
        if (screenObject != null)
        {
            screenObject.SetActive(on);
            return;
        }

        // Altfel încercăm să găsim un obiect "screen" în itemModel (căutare case-insensitive)
        if (inventory == null || inventory.allItemsDatabase == null) return;
        if (dbIndex < 0 || dbIndex >= inventory.allItemsDatabase.Count) return;

        GameObject model = inventory.allItemsDatabase[dbIndex].itemModel;
        if (model == null) return;

        // caută recursiv un child care conține "screen" în nume
        Transform found = FindChildByNameContains(model.transform, "screen");
        if (found != null)
        {
            found.gameObject.SetActive(on);
            return;
        }

        // fallback: caută orice Canvas sau Renderer în model și activează/dezactivează
        var canvas = model.GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            canvas.gameObject.SetActive(on);
            return;
        }
        var renderer = model.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
        {
            renderer.gameObject.SetActive(on);
            return;
        }

        Debug.LogWarning("[PhoneController] No screen GameObject found for itemDBIndex " + dbIndex + " (set screenObject in inspector to avoid this).");
    }

    private Transform FindChildByNameContains(Transform root, string partialName)
    {
        if (root == null) return null;
        if (root.name.ToLower().Contains(partialName.ToLower())) return root;
        foreach (Transform child in root)
        {
            var r = FindChildByNameContains(child, partialName);
            if (r != null) return r;
        }
        return null;
    }

    private bool IsControllerAllowedForItem(int dbIndex)
    {
        if (inventory == null || inventory.allItemsDatabase == null) return false;
        if (dbIndex < 0 || dbIndex >= inventory.allItemsDatabase.Count) return false;

        var item = inventory.allItemsDatabase[dbIndex];

        // dacă e setat explicit controller -> răspunde doar la Phone
        if (item.controller != ItemController.None)
        {
            return item.controller == ItemController.Phone;
        }

        // fallback: compatibilitate cu vechiul behavior
        return item.isToggleable;
    }

    private void OnDestroy()
    {
        if (inventory != null && inventory.currentEquippedItemDBIndex != null)
            inventory.currentEquippedItemDBIndex.OnValueChanged -= OnEquippedItemChanged;

        flashlightOn.OnValueChanged -= OnFlashlightNetworkChanged;
        screenOn.OnValueChanged -= OnScreenNetworkChanged;

        if (ikCoroutine != null) StopCoroutine(ikCoroutine);
        if (inventory != null) inventory.isIKSuppressed = false;
        // MODIFICAT: asigurăm resetarea flag-ului global
        if (inventory != null) inventory.isAnyToggleInProgress = false;
    }
}
