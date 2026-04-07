using UnityEngine;
using Unity.Netcode;
using UnityEngine.Animations.Rigging;
using System.Collections;
using System.Collections.Generic;

public enum ItemController
{
    None = 0,
    Phone = 1,
    Pepsi = 2,
}

[System.Serializable]
public class ItemData
{
    public string itemName;
    public GameObject itemModel;
    public Light itemLight;
    [Tooltip("Prefab-ul care cade pe jos (TREBUIE sa aiba NetworkObject si PickupableItem)")]
    public GameObject dropPrefab; 
    
    [Header("Animatie")]
    public int animationID = 1; 
    
    [Header("Custom")]
    [Tooltip("Bifează dacă itemul poate fi controlat cu sistemul de toggle (ex: lanternă / telefon cu ecran).")]
    public bool isToggleable = false;

    [Header("Controller")]
    [Tooltip("Alege ce controller va controla acest item (Phone, Pepsi etc).")]
    public ItemController controller = ItemController.None;

    [Header("Drop")]
    public bool canDrop = true;
}

public class Inventory : NetworkBehaviour
{
    [Header("Baza de Date Iteme")]
    public List<ItemData> allItemsDatabase = new List<ItemData>();
    public int maxSlots = 5;
    private int[] inventorySlots;
    private int currentEquippedSlot = -1;

    [Header("Setari de Start")]
    public List<int> startingItems = new List<int>();

    [Header("Referinte IK (Rigging)")]
    public TwoBoneIKConstraint armIKConstraint;
    public Transform ikPivot; 
    [Header("External Control")]
    public float ikWeightMultiplier = 1f;
    public Vector3 ikTwistOffset = Vector3.zero;

    [Header("DEBUG IK")]
    public bool debug_hasItem;
    public bool debug_isAnimatingEquip;
    public bool debug_shouldHaveIK;

    [Header("Setari Rotatie")]
    [Range(0f, 1f)] public float itemLookMultiplier = 0.3f; 
    public float minItemPitch = -20f; 
    public float maxItemPitch = 30f;
    public float verticalRotationOffset = 0f;

    [Header("Interactiune & Drop")]
    public float pickupDistance = 3f; 
    public Vector3 dropOffset = new Vector3(0, 1.2f, 1.2f);
    public LayerMask ignoreLayers; 

    [Header("Cooldown & Animatii")]
    public float equipCooldown = 0.5f;
    public float equipAnimationTime = 0.3f;
    public float unequipAnimationTime = 0.3f;

    private Animator animator;
    private Transform playerCamera;
    private bool isBusy = false;
    public bool isInputBlocked = false;
    public bool isIKSuppressed = false;
    public bool isAnyToggleInProgress = false;
    
    private float equipIKDelayTimer = 0f;

    private NetworkVariable<float> networkPitch = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> currentEquippedItemDBIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private bool isUnequipping = false;

    public override void OnNetworkSpawn()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (IsOwner)
        {
            playerCamera = GetComponentInChildren<Camera>()?.transform;
            if (playerCamera == null) playerCamera = Camera.main.transform;

            inventorySlots = new int[maxSlots];
            for (int i = 0; i < maxSlots; i++) inventorySlots[i] = -1;

            foreach (int itemId in startingItems) GiveItem(itemId);
        }

        foreach (var item in allItemsDatabase)
        {
            if (item.itemModel != null) item.itemModel.SetActive(false);
        }

        currentEquippedItemDBIndex.OnValueChanged += OnItemStateChanged;
    }

    void Update()
    {
        if (equipIKDelayTimer > 0) equipIKDelayTimer -= Time.deltaTime;

        if (armIKConstraint != null)
        {
            debug_hasItem = currentEquippedItemDBIndex.Value != -1;
            debug_isAnimatingEquip = equipIKDelayTimer > 0;
            debug_shouldHaveIK = debug_hasItem && !isIKSuppressed && !isBusy && !debug_isAnimatingEquip && !isUnequipping;

            if (ikWeightMultiplier >= 1f)
            {
                float targetWeight = debug_shouldHaveIK ? 1f : 0f;
                armIKConstraint.weight = Mathf.Lerp(armIKConstraint.weight, targetWeight, Time.deltaTime * 15f);
            }
            else
            {
                armIKConstraint.weight = ikWeightMultiplier;
            }
        }

        if (ikPivot != null && currentEquippedItemDBIndex.Value != -1)
        {
            float clampedPitch = Mathf.Clamp(networkPitch.Value * itemLookMultiplier, minItemPitch, maxItemPitch);
            Quaternion baseRot = Quaternion.Euler(clampedPitch + verticalRotationOffset, 0, 0);
            ikPivot.localRotation = baseRot * Quaternion.Euler(ikTwistOffset);
        }

        if (!IsOwner || isInputBlocked) return;

        if (playerCamera != null)
        {
            float pitch = playerCamera.localEulerAngles.x;
            if (pitch > 180) pitch -= 360f;
            networkPitch.Value = pitch;
        }

        if (!isBusy && !isAnyToggleInProgress)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) TryEquipFromSlot(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) TryEquipFromSlot(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) TryEquipFromSlot(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) TryEquipFromSlot(3);
            if (Input.GetKeyDown(KeyCode.Alpha5)) TryEquipFromSlot(4);
            
            if (Input.GetKeyDown(KeyCode.G)) DropCurrentItem();
            if (Input.GetKeyDown(KeyCode.E)) TryPickupItem();
        }
    }
    
    public void ResetIKState()
    {
        // Resetează stările interne care pot afecta IK-ul
        isBusy = false;
        isUnequipping = false;
        // Dacă există un item echipat, asigură-te că animatorul și IK-ul sunt setate corect
        if (currentEquippedItemDBIndex.Value != -1)
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetFloat("ItemAnimID", (float)allItemsDatabase[currentEquippedItemDBIndex.Value].animationID);
                animator.SetBool("HasPhone", true);
            }
        }
    }

    private bool GiveItem(int dbIndex)
    {
        if (dbIndex < 0 || dbIndex >= allItemsDatabase.Count) return false;
        for (int i = 0; i < maxSlots; i++)
        {
            if (inventorySlots[i] == -1)
            {
                inventorySlots[i] = dbIndex;
                return true; 
            }
        }
        return false; 
    }

    private void TryEquipFromSlot(int slotIndex)
    {
        if (slotIndex >= maxSlots) return;
        int targetItemId = inventorySlots[slotIndex];
        if (targetItemId == -1) return;

        if (isBusy || isAnyToggleInProgress) return;

        if (currentEquippedSlot == slotIndex)
        {
            StartCoroutine(PerformUnequip());
        }
        else 
        {
            StartCoroutine(PerformEquip(slotIndex, targetItemId));
        }
    }

    private IEnumerator PerformUnequip()
    {
        if (isInputBlocked) yield break;
        isBusy = true;
        isUnequipping = true;

        if (animator != null)
        {
            animator.SetBool("HasPhone", false);
        }

        yield return new WaitForSeconds(unequipAnimationTime);

        // Modelul va fi dezactivat în OnItemStateChanged când currentEquippedItemDBIndex.Value devine -1
        // Așa că nu mai facem aici.

        currentEquippedSlot = -1;
        currentEquippedItemDBIndex.Value = -1;

        isUnequipping = false;
        isBusy = false;
    }

    private IEnumerator PerformEquip(int slotIndex, int targetItemId)
    {
        if (isInputBlocked) yield break;
        isBusy = true;

        if (currentEquippedSlot != -1)
        {
            yield return StartCoroutine(PerformUnequip());
            isBusy = true;
        }

        if (animator != null)
        {
            animator.SetFloat("ItemAnimID", (float)allItemsDatabase[targetItemId].animationID);
            animator.SetBool("HasPhone", true);
        }

        // Nu mai activăm modelul aici – o facem în OnItemStateChanged
        currentEquippedSlot = slotIndex;
        currentEquippedItemDBIndex.Value = targetItemId;

        yield return new WaitForSeconds(equipAnimationTime);

        isBusy = false;
    }

    private void DropCurrentItem()
    {
        if (isInputBlocked) return;
        if (currentEquippedItemDBIndex.Value == -1) return;
        if (isBusy || isAnyToggleInProgress) return;

        int dbIndexToDrop = currentEquippedItemDBIndex.Value;
        if (!allItemsDatabase[dbIndexToDrop].canDrop)
        {
            Debug.Log("Acest item nu poate fi aruncat.");
            return;
        }
        
        if(currentEquippedSlot != -1) inventorySlots[currentEquippedSlot] = -1;

        Vector3 spawnPos = transform.position + (transform.forward * dropOffset.z) + (transform.up * dropOffset.y);
        DropItemServerRpc(dbIndexToDrop, spawnPos, transform.rotation);
        
        if (!isBusy)
            StartCoroutine(PerformUnequip());
        else
        {
            currentEquippedSlot = -1;
            currentEquippedItemDBIndex.Value = -1;
        }
    }

    [ServerRpc] private void DropItemServerRpc(int index, Vector3 pos, Quaternion rot)
    {
        GameObject dropped = Instantiate(allItemsDatabase[index].dropPrefab, pos, rot);
        dropped.GetComponent<NetworkObject>().Spawn();
        if(dropped.TryGetComponent(out PickupableItem p)) p.inventoryIndex.Value = index;
    }

    public bool RemoveEquippedItemSilent()
    {
        if (!IsOwner)
        {
            Debug.LogWarning("[Inventory] RemoveEquippedItemSilent called but not owner.");
            return false;
        }

        int dbIndex = currentEquippedItemDBIndex.Value;
        if (dbIndex == -1) return false;

        if (currentEquippedSlot != -1 && inventorySlots[currentEquippedSlot] == dbIndex)
        {
            inventorySlots[currentEquippedSlot] = -1;
        }
        else
        {
            for (int i = 0; i < inventorySlots.Length; i++)
            {
                if (inventorySlots[i] == dbIndex)
                {
                    inventorySlots[i] = -1;
                    break;
                }
            }
        }

        // Modelul va fi dezactivat în OnItemStateChanged când currentEquippedItemDBIndex.Value devine -1
        // Așa că nu mai facem aici.

        currentEquippedSlot = -1;
        currentEquippedItemDBIndex.Value = -1;

        return true;
    }

    private void TryPickupItem()
    {
        if (isInputBlocked) return;
        if (playerCamera == null) return;
        if (isBusy || isAnyToggleInProgress) return;

        if (Physics.Raycast(playerCamera.position, playerCamera.forward, out RaycastHit hit, pickupDistance, ~ignoreLayers))
        {
            PickupableItem pickup = hit.collider.GetComponentInParent<PickupableItem>();
            if (pickup != null)
            {
                int dbId = pickup.inventoryIndex.Value;
                if (GiveItem(dbId)) 
                {
                    int slot = -1;
                    for (int i = 0; i < maxSlots; i++)
                    {
                        if (inventorySlots[i] == dbId)
                        {
                            slot = i;
                            break;
                        }
                    }

                    if (slot != -1 && !isBusy)
                    {
                        StartCoroutine(PerformEquip(slot, dbId));
                    }
                    else if (slot != -1 && isBusy)
                    {
                        Debug.Log("Player is busy, cannot auto-equip picked item. Item added to inventory.");
                    }

                    RequestDespawnServerRpc(pickup.NetworkObjectId);
                }
            }
        }
    }

    [ServerRpc] private void RequestDespawnServerRpc(ulong netId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out NetworkObject obj)) obj.Despawn();
    }

    // MODIFICAT: Acum gestionează vizibilitatea modelului pentru toți clienții
    private void OnItemStateChanged(int oldDbIndex, int nextDbIndex)
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        
        bool holdsItem = nextDbIndex != -1;

        if (animator.layerCount > 1) 
        {
            animator.SetLayerWeight(1, holdsItem ? 1f : 0f);
        }

        if (holdsItem)
        {
            animator.SetFloat("ItemAnimID", (float)allItemsDatabase[nextDbIndex].animationID);
            animator.SetBool("HasPhone", true);
            
            if (this.gameObject.activeInHierarchy)
            {
                equipIKDelayTimer = 0.1f;
            }

            // Activează modelul după un scurt delay pentru a se sincroniza cu animația
            StartCoroutine(ActivateModelAfterDelay(nextDbIndex, equipAnimationTime * 0.5f));
        }
        else
        {
            if (armIKConstraint != null) armIKConstraint.weight = 0f;

            // Dezactivează modelul vechi
            if (oldDbIndex != -1 && oldDbIndex < allItemsDatabase.Count)
            {
                allItemsDatabase[oldDbIndex].itemModel?.SetActive(false);
            }
        }
    }

    private IEnumerator ActivateModelAfterDelay(int itemIndex, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (itemIndex >= 0 && itemIndex < allItemsDatabase.Count)
        {
            allItemsDatabase[itemIndex].itemModel?.SetActive(true);
        }
    }

    public override void OnDestroy()
    {
        if (currentEquippedItemDBIndex != null) currentEquippedItemDBIndex.OnValueChanged -= OnItemStateChanged;
    }
}
