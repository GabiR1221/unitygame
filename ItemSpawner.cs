using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class ItemSpawner : NetworkBehaviour
{
    [Header("Referinte")]
    public Inventory inventoryDatabaseSource;

    [Header("Setari Interactiune")]
    public string interactionMessage = "Press E to spawn item"; // Textul personalizat
    public int itemIDToSpawn = 0; 
    public Transform spawnPoint; 
    public float cooldown = 3f;

    [Header("Audio")]
    public AudioClip spawnSound; 
    [Range(0f, 1f)] public float soundVolume = 0.7f;

    private float lastSpawnTime;

    // --- FUNCTIA APELATA DE PLAYER INTERACTION ---
    public void Interact()
    {
        if (Time.time >= lastSpawnTime + cooldown)
        {
            if (inventoryDatabaseSource == null) return;

            SpawnItemServerRpc(itemIDToSpawn);
            lastSpawnTime = Time.time;
        }
        else
        {
            Debug.Log("Spawner is on cooldown.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SpawnItemServerRpc(int dbIndex)
    {
        if (dbIndex < 0 || dbIndex >= inventoryDatabaseSource.allItemsDatabase.Count) return;

        ItemData data = inventoryDatabaseSource.allItemsDatabase[dbIndex];

        if (data.dropPrefab != null)
        {
            Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion rot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

            GameObject spawnedObj = Instantiate(data.dropPrefab, pos, rot);
            spawnedObj.GetComponent<NetworkObject>().Spawn();

            if (spawnedObj.TryGetComponent(out PickupableItem pItem))
            {
                pItem.inventoryIndex.Value = dbIndex;
            }

            PlaySpawnSoundClientRpc(pos);
        }
    }

    [ClientRpc]
    private void PlaySpawnSoundClientRpc(Vector3 position)
    {
        if (spawnSound != null)
        {
            AudioSource.PlayClipAtPoint(spawnSound, position, soundVolume);
        }
    }
}
