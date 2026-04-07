using UnityEngine;
using Unity.Netcode;

public class PickupableItem : NetworkBehaviour
{
    // Acest NetworkVariable va tine minte ce item este (0 pentru telefon, 1 pentru cub, etc.)
    public NetworkVariable<int> inventoryIndex = new NetworkVariable<int>(-1);
}
