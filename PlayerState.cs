using Unity.Netcode;
using UnityEngine;

public class PlayerState : NetworkBehaviour
{
    public NetworkVariable<bool> isAlive = new NetworkVariable<bool>(true);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            isAlive.Value = true;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void DieServerRpc()
    {
        if (!IsServer) return;
        if (!isAlive.Value) return;
        isAlive.Value = false;
        // Aici poți declanșa efecte vizuale/sunet pentru toți
    }

    [ServerRpc(RequireOwnership = false)]
    public void ResurrectServerRpc()
    {
        if (!IsServer) return;
        isAlive.Value = true;
    }
}
