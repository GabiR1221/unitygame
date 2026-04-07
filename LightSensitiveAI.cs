using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class LightSensitiveAI : NetworkBehaviour 
{
    public enum AIState { Idle, Moving, AtRandomSpot, PreparingAttack, Fleeing }
    
    [Header("Stare Curenta (Debug)")]
    private AIState currentState = AIState.Idle;

    public bool disableAI = false;
    public Transform[] waypoints; 

    [Header("Rute Random (Dupa Waypoint 3)")]
    public Transform[] randomWaypoints;      
    public Transform[] randomAttackPositions; 
    
    [Header("Setari Miscare")]
    public float normalSpeed = 2f;    
    public float fleeSpeed = 8f;      
    public float moveInterval = 5f;   

    [Header("Setari Lumina")]
    public float lightRequiredTime = 0.2f; 
    private float currentLightTimer = 0f;

    // MODIFICARE: Schimbat din int in NetworkVariable pentru compatibilitate cu GameManager
    public NetworkVariable<int> aiLevel = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); 

    private int currentSpotIndex = 0;
    private int selectedRandomIndex = -1; 
    private float lastHitTime = 0f;
    private Coroutine currentRoutine;

    void Update()
    {
        if (!IsServer || disableAI || !GameManager.instance.isNightActive.Value) return;

        // Verificam lumina
        bool beingHitRightNow = (Time.time - lastHitTime) < 0.2f;

        if (beingHitRightNow && (currentState == AIState.AtRandomSpot || currentState == AIState.PreparingAttack))
        {
            currentLightTimer += Time.deltaTime;
            if (currentLightTimer >= lightRequiredTime) 
            {
                ScareAway();
                return;
            }
        }
        else
        {
            currentLightTimer = 0f;
        }

        // Daca robotul nu face nimic, porneste ciclul de miscare
        if (currentState == AIState.Idle)
        {
            currentRoutine = StartCoroutine(MovementLogic());
        }
    }

    IEnumerator MovementLogic()
    {
        currentState = AIState.Moving;

        // MODIFICARE: Folosim aiLevel.Value
        float dynamicInterval = Mathf.Max(0.5f, moveInterval - (aiLevel.Value * 0.5f));
        yield return new WaitForSeconds(dynamicInterval);

        // MODIFICARE: Folosim aiLevel.Value
        float dynamicSpeed = normalSpeed + (aiLevel.Value * 0.5f);

        // 1. Mergem spre Waypoint 3
        if (currentSpotIndex < 3)
        {
            currentSpotIndex++;
            yield return StartCoroutine(MoveSmoothly(waypoints[currentSpotIndex].position, dynamicSpeed));
            currentState = AIState.Idle;
        }
        // 2. Alegem ruta Random
        else if (selectedRandomIndex == -1)
        {
            Random.InitState(System.DateTime.Now.Millisecond + (int)transform.position.x);
            selectedRandomIndex = Random.Range(0, randomWaypoints.Length);
            
            Debug.Log($"[AI] Am ales ruta random: {selectedRandomIndex}");

            yield return StartCoroutine(MoveSmoothly(randomWaypoints[selectedRandomIndex].position, dynamicSpeed));
            currentState = AIState.AtRandomSpot;
            
            yield return new WaitForSeconds(3f); 
            
            if(currentState == AIState.AtRandomSpot) 
            {
                currentState = AIState.Moving;
                yield return StartCoroutine(MoveSmoothly(randomAttackPositions[selectedRandomIndex].position, dynamicSpeed + 2f));
                StartCoroutine(AttackRoutine());
            }
        }
    }

    IEnumerator AttackRoutine()
    {
        currentState = AIState.PreparingAttack;
        // MODIFICARE: Folosim aiLevel.Value
        float waitTime = Mathf.Max(1f, 4.5f - (aiLevel.Value * 0.3f));
        yield return new WaitForSeconds(waitTime); 

        if (currentState == AIState.PreparingAttack && IsServer)
        {
            GameManager.instance.JumpscareResetServerRpc();
        }
    }

    void ScareAway()
    {
        if (currentRoutine != null) StopCoroutine(currentRoutine);
        StopAllCoroutines(); 
        
        currentLightTimer = 0;
        StartCoroutine(FleeRoutine());
    }

    IEnumerator FleeRoutine()
    {
        currentState = AIState.Fleeing;
        
        yield return StartCoroutine(MoveSmoothly(waypoints[0].position, fleeSpeed));
        
        currentSpotIndex = 0;
        selectedRandomIndex = -1;
        currentState = AIState.Idle; 
    }

    IEnumerator MoveSmoothly(Vector3 targetPos, float speed)
    {
        while (Vector3.Distance(transform.position, targetPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);
            yield return null;
        }
        transform.position = targetPos;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReceiveFlashlightHitServerRpc() { lastHitTime = Time.time; }

    public void ResetAI()
    {
        if (!IsServer) return;
        StopAllCoroutines();
        currentSpotIndex = 0;
        selectedRandomIndex = -1;
        currentState = AIState.Idle;
        if (waypoints.Length > 0) transform.position = waypoints[0].position;
    }
}
