using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class SpectatorCamera : NetworkBehaviour
{
    [Header("Setări urmărire")]
    public float followDistance = 5f;
    public float heightOffset = 2f;
    public float smoothSpeed = 5f;
    public LayerMask obstructionMask; // pentru a evita pereții

    private Transform target;
    private List<Transform> alivePlayers = new List<Transform>();

    void OnEnable()
    {
        if (IsOwner)
        {
            FindNextTarget(); // caută imediat o țintă când se activează
        }
    }
    
    void OnDisable()
    {
        target = null; // Resetăm ținta când camera se oprește
        alivePlayers.Clear();
    }

    void Update()
    {
        if (!IsOwner) return;
        Debug.Log("SpectatorCamera Update ruleaza"); // vezi dacă apare în consolă

        // Dacă nu avem țintă sau ținta a murit, căutăm alta
        if (target == null || target.GetComponent<PlayerState>()?.isAlive.Value == false)
        {
            FindNextTarget();
        }

        if (target != null)
        {
            // Calculează poziția dorită (în spatele țintei, la înălțime)
            Vector3 desiredPosition = target.position - target.forward * followDistance + Vector3.up * heightOffset;

            // Verifică dacă linia de vedere este obstrucționată (de exemplu de pereți)
            RaycastHit hit;
            if (Physics.Raycast(target.position + Vector3.up * 1.5f, desiredPosition - target.position, out hit, followDistance, obstructionMask))
            {
                // Dacă da, apropie camera de țintă
                desiredPosition = hit.point + hit.normal * 0.5f;
            }

            // Mișcare lină
            transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * smoothSpeed);
            transform.LookAt(target.position + Vector3.up * 1.5f);
        }
    }

    void FindNextTarget()
    {
        alivePlayers.Clear();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var state = client.PlayerObject.GetComponent<PlayerState>();
                if (state != null && state.isAlive.Value)
                {
                    alivePlayers.Add(client.PlayerObject.transform);
                }
            }
        }

        if (alivePlayers.Count > 0)
        {
            // Alege cel mai apropiat jucător (sau poți implementa ciclare cu taste)
            float closestDist = float.MaxValue;
            Transform closest = null;
            foreach (var t in alivePlayers)
            {
                float dist = Vector3.Distance(transform.position, t.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = t;
                }
            }
            target = closest;
        }
        else
        {
            target = null; // niciun jucător viu – camera rămâne pe loc
        }
    }

    // Opțional: poți adăuga ciclare între jucători cu rotița mouse-ului
    private int currentTargetIndex = 0;

    void CycleTarget(int direction)
    {
        if (alivePlayers.Count == 0) return;
        currentTargetIndex = (currentTargetIndex + direction + alivePlayers.Count) % alivePlayers.Count;
        target = alivePlayers[currentTargetIndex];
    }

    void LateUpdate()
    {
        if (!IsOwner) return;

        // Exemplu: schimbă ținta cu rotița
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f) CycleTarget(1);
        else if (scroll < 0f) CycleTarget(-1);
    }
}
