using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class AIWaypoint
{
    public string roomName;
    public Transform pointTransform;
    public List<int> adjacentWaypoints;
    public bool isPreAttackSpot;
}

[System.Serializable]
public struct PathStep
{
    public string stepName;
    public Transform point;
    public float waitTimeAtPoint;
    public float moveSpeed;
}

public class AnimatronicAI : NetworkBehaviour
{
    [Header("Referințe Componente")]
    public BoxCollider pathCollider;
    public DoorSystem doorScript;

    [Header("Configurare Căi")]
    public List<AIWaypoint> waypointMap;
    public Transform attackPosition;

    [Header("Dificultate")]
    public NetworkVariable<int> aiLevel = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public bool disableAI = false;
    public float moveInterval = 5f;
    public int moveChance = 50;

    [Header("Setări Mod Special (aiLevel = 100)")]
    public Transform specialTeleportPoint;
    public string specialAnimationName = "ScarePose";
    public float specialAnimationDuration = 3.0f;

    [Header("Setări Mod Traseu (aiLevel = 101)")]
    public Transform pathStartPoint;
    public List<PathStep> consecutivePath;

    [Header("Setări Mod Atac Direct (aiLevel = 102)")]
    public float timeBeforeAttack102 = 3.0f;
    public Transform retreatPosition102;

    [Header("Animații")]
    public string normalStateName = "Movement";

    [Header("JUMPSCARE CLONING (pentru mod general)")]
    [Tooltip("Prefab vizual pentru clone de jumpscare (fără NetworkObject) — instanțiată local pe fiecare client.")]
    public GameObject jumpscareClonePrefab;
    [Tooltip("Numele animației folosit implicit pentru jumpscare clone/animatronic (poți override la call).")]
    public string generalJumpscareAnimName = "ScarePose_General";
    [Tooltip("Durata implicită pentru jumpscare general (folosită pentru a sincroniza kill-ul).")]
    public float generalJumpscareDuration = 2.5f;

    [Header("Animație Player (poți alege din Inspector)")]
    [Tooltip("Dacă e gol, se va folosi animația clonei/animatronicului.")]
    public string playerJumpscareAnimName = "";

    // stare internă
    private bool wasInSpecialMode = false;
    private bool isInPathMode = false;
    private bool isInDirectAttackMode = false;

    private int currentSpotIndex = 0;
    private float timer;
    private bool isPreparingAttack = false;
    private Animator anim;
    private NetworkTransform netTransform;
    private Vector3 positionBeforeSpecial;
    private Quaternion rotationBeforeSpecial;
    // Pe fiecare client, ține evidența corutinei de jumpscare active pentru a preveni suprapunerea
    private Coroutine activeJumpscareCoroutine;
    private bool[] uiPreviousStates;

    // --- NOI ---
    private bool isInJumpscare = false; // dacă e în jumpscare — pause AI behavior
    private Vector3 posBeforeJumpscare;
    private Quaternion rotBeforeJumpscare;
    private bool savedWasInSpecialMode, savedIsInPathMode, savedIsInDirectAttackMode, savedIsPreparingAttack;
    private int savedCurrentSpotIndex;
    private float savedTimer;

    // MODIFICAT: Adăugat referință pentru corutina modului 101
    private Coroutine pathModeCoroutine;

    public List<GameObject> uisToHideDuringEvents;

    public void HideAllUI()
    {
        uiPreviousStates = new bool[uisToHideDuringEvents.Count];
        for (int i = 0; i < uisToHideDuringEvents.Count; i++)
        {
            if (uisToHideDuringEvents[i] != null)
            {
                uiPreviousStates[i] = uisToHideDuringEvents[i].activeSelf;
                uisToHideDuringEvents[i].SetActive(false);
            }
        }
    }

    public void RestoreUI()
    {
        if (uiPreviousStates == null) return;
        for (int i = 0; i < uisToHideDuringEvents.Count && i < uiPreviousStates.Length; i++)
        {
            if (uisToHideDuringEvents[i] != null && uiPreviousStates[i])
                uisToHideDuringEvents[i].SetActive(true);
        }
        uiPreviousStates = null;
    }

    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        netTransform = GetComponent<NetworkTransform>();

        if (pathCollider != null)
        {
            pathCollider.isTrigger = true;
            pathCollider.enabled = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ResetAI();
            CheckSpecialStatus(aiLevel.Value);
        }

        aiLevel.OnValueChanged += (oldVal, newVal) =>
        {
            if (IsServer)
            {
                Debug.Log($"[AI] Dificultate schimbata la {newVal}. Resetare pozitii...");
                CheckSpecialStatus(newVal);
            }
        };
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (aiLevel.Value == 101 && other.CompareTag("Player"))
        {
            Debug.Log("<color=red>[AI 101]</color> JUMPSCARE! Jucătorul a fost prins pe traseu.");
            // folosim flow-ul centralizat care respectă aiLevel == 101 (va apela rutina 101 single)
            ulong victimId = ulong.MaxValue;
            foreach (var c in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (c.PlayerObject != null && c.PlayerObject.gameObject == other.gameObject)
                {
                    victimId = c.ClientId;
                    break;
                }
            }
            if (victimId != ulong.MaxValue)
            {
                StartJumpscareSingleServerRpc(victimId, specialAnimationName, playerJumpscareAnimName == "" ? specialAnimationName : playerJumpscareAnimName, specialAnimationDuration);
            }
        }
    }

    private void CheckSpecialStatus(int level)
    {
        ResetAllFlags();

        if (pathCollider != null) pathCollider.enabled = (level == 101);

        if (level < 100)
        {
            currentSpotIndex = 0;
            if (waypointMap != null && waypointMap.Count > 0)
                UpdatePosition(waypointMap[0].pointTransform);

            PlayAnimationClientRpc(normalStateName);
        }
        else if (level == 100)
        {
            wasInSpecialMode = true;
            if (specialTeleportPoint != null) UpdatePosition(specialTeleportPoint);
            StartCoroutine(SpecialMode100Routine());
        }
        else if (level == 101)
        {
            isInPathMode = true;
            if (pathStartPoint != null) UpdatePosition(pathStartPoint);
            if (pathModeCoroutine != null) StopCoroutine(pathModeCoroutine);
            pathModeCoroutine = StartCoroutine(EnterPathMode101(0)); // pornește de la început
        }
        else if (level == 102)
        {
            isInDirectAttackMode = true;
            if (attackPosition != null) UpdatePosition(attackPosition);
            StartCoroutine(DirectAttackMode102Routine());
        }
    }

    void ResetAllFlags()
    {
        // MODIFICAT: Oprește corutina modului 101 când se resetează starea
        if (pathModeCoroutine != null)
        {
            StopCoroutine(pathModeCoroutine);
            pathModeCoroutine = null;
        }

        wasInSpecialMode = false;
        isInPathMode = false;
        isInDirectAttackMode = false;
        isPreparingAttack = false;
        timer = 0;
    }

    IEnumerator SpecialMode100Routine()
    {
        positionBeforeSpecial = transform.position;
        rotationBeforeSpecial = transform.rotation;

        if (specialTeleportPoint != null)
        {
            UpdatePosition(specialTeleportPoint);
            PlayAnimationClientRpc(specialAnimationName);

            float elapsed = 0f;
            while (elapsed < specialAnimationDuration)
            {
                if (!isInJumpscare) elapsed += Time.deltaTime;
                yield return null;
            }

            if (aiLevel.Value == 100)
            {
                UpdatePosition(positionBeforeSpecial, rotationBeforeSpecial);
                PlayAnimationClientRpc(normalStateName);
                wasInSpecialMode = false;
            }
        }
    }

    IEnumerator EnterPathMode101(int startIndex = 0)
    {
        for (int i = startIndex; i < consecutivePath.Count; i++)
        {
            PathStep step = consecutivePath[i];
            if (step.point != null)
            {
                // Deplasează-te către punct
                while (Vector3.Distance(transform.position, step.point.position) > 0.1f)
                {
                    while (isInJumpscare) yield return null;
                    if (aiLevel.Value != 101) yield break;

                    transform.position = Vector3.MoveTowards(transform.position, step.point.position, step.moveSpeed * Time.deltaTime);
                    Vector3 direction = (step.point.position - transform.position).normalized;
                    if (direction != Vector3.zero)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 5f);

                    yield return null;
                }
                UpdatePosition(step.point);

                // Așteaptă la punct
                float waited = 0f;
                while (waited < step.waitTimeAtPoint)
                {
                    while (isInJumpscare) yield return null;
                    waited += Time.deltaTime;
                    yield return null;
                }
                if (aiLevel.Value != 101) yield break;
            }
        }
        // După ultimul punct, corutina se termină – animatronicul rămâne acolo
    }

    IEnumerator DirectAttackMode102Routine()
    {
        UpdatePosition(attackPosition);
        float elapsed = 0f;
        while (elapsed < timeBeforeAttack102)
        {
            while (isInJumpscare) yield return null;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (aiLevel.Value != 102 || !GameManager.instance.isNightActive.Value) yield break;

        if (doorScript != null && doorScript.isDoorFullyClosed.Value)
        {
            if (retreatPosition102 != null) UpdatePosition(retreatPosition102);
            else ResetToRandomEarlySpot();
        }
        else
        {
            // MODIFICAT: În loc să lovească doar cel mai apropiat jucător, lovește pe toți
            JumpscareAllPlayers();
        }
    }

    void Update()
    {
        if (!IsServer) return;
        if (disableAI || GameManager.instance == null || !GameManager.instance.isNightActive.Value) return;
        if (aiLevel.Value >= 100 || isPreparingAttack) return;
        if (isInJumpscare) return; // dacă e jumpscare, pauzăm logica Update

        timer += Time.deltaTime;
        float dynamicInterval = Mathf.Max(0.5f, moveInterval - (aiLevel.Value * 0.2f));

        if (timer >= dynamicInterval)
        {
            timer = 0;
            if (Random.Range(0, 100) < (moveChance + (aiLevel.Value * 2f))) MoveToNextSpot();
        }
    }

    void MoveToNextSpot()
    {
        if (waypointMap == null || waypointMap.Count == 0) return;
        AIWaypoint currentWaypoint = waypointMap[currentSpotIndex];

        if (currentWaypoint.isPreAttackSpot)
        {
            StartCoroutine(AttackRoutine());
            return;
        }

        if (currentWaypoint.adjacentWaypoints != null && currentWaypoint.adjacentWaypoints.Count > 0)
        {
            int nextIndex = (aiLevel.Value > 10 && Random.Range(0, 10) > 3)
                ? GetAggressiveWaypoint(currentWaypoint.adjacentWaypoints)
                : currentWaypoint.adjacentWaypoints[Random.Range(0, currentWaypoint.adjacentWaypoints.Count)];

            if (nextIndex >= 0 && nextIndex < waypointMap.Count)
            {
                currentSpotIndex = nextIndex;
                UpdatePosition(waypointMap[currentSpotIndex].pointTransform);
            }
        }
    }

    IEnumerator AttackRoutine()
    {
        isPreparingAttack = true;
        UpdatePosition(attackPosition);
        float reactionTime = Mathf.Max(0.8f, 3.0f - (aiLevel.Value * 0.2f));
        float elapsed = 0f;
        while (elapsed < reactionTime)
        {
            while (isInJumpscare) yield return null;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (GameManager.instance.isNightActive.Value && aiLevel.Value < 100)
        {
            if (doorScript != null && doorScript.isDoorFullyClosed.Value)
                ResetToRandomEarlySpot();
            else
                // MODIFICAT: În loc să lovească doar cel mai apropiat jucător, lovește pe toți
                JumpscareAllPlayers();
        }
        isPreparingAttack = false;
    }

    // helper: returnează indexul waypoint-ului cel mai apropiat de poziția curentă a animatronicului
    private int GetNearestWaypointIndex()
    {
        if (waypointMap == null || waypointMap.Count == 0) return 0;
        int best = 0;
        float bestDist = Vector3.Distance(transform.position, waypointMap[0].pointTransform.position);
        for (int i = 1; i < waypointMap.Count; i++)
        {
            float d = Vector3.Distance(transform.position, waypointMap[i].pointTransform.position);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // MODIFICAT: Metodă nouă pentru a face jumpscare la toți jucătorii simultan
    private void JumpscareAllPlayers()
    {
        if (!IsServer) return;
        List<ulong> allIds = new List<ulong>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
                allIds.Add(client.ClientId);
        }
        if (allIds.Count > 0)
            StartJumpscareServerRpc(allIds.ToArray(), generalJumpscareAnimName, playerJumpscareAnimName, generalJumpscareDuration);
    }

    // Păstrăm KillClosestPlayer pentru eventuale alte utilizări (ex: debug)
    private void KillClosestPlayer()
    {
        if (!IsServer) return;

        float closestDist = float.MaxValue;
        ulong targetId = ulong.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                float dist = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    targetId = client.ClientId;
                }
            }
        }

        if (targetId != ulong.MaxValue)
        {
            StartJumpscareSingleServerRpc(targetId, generalJumpscareAnimName, playerJumpscareAnimName == "" ? generalJumpscareAnimName : playerJumpscareAnimName, generalJumpscareDuration);
        }
    }

    void UpdatePosition(Transform target) { UpdatePosition(target.position, target.rotation); }

    void UpdatePosition(Vector3 pos, Quaternion rot)
    {
        transform.SetPositionAndRotation(pos, rot);
        if (netTransform != null) netTransform.Teleport(pos, rot, transform.localScale);
    }

    void ResetToRandomEarlySpot()
    {
        currentSpotIndex = Random.Range(0, Mathf.Min(3, waypointMap.Count));
        UpdatePosition(waypointMap[currentSpotIndex].pointTransform);
        timer = 0;
    }

    int GetAggressiveWaypoint(List<int> neighbors)
    {
        int bestSpot = neighbors[0];
        foreach (int index in neighbors) if (index > bestSpot) bestSpot = index;
        return bestSpot;
    }

    [ClientRpc]
    void PlayAnimationClientRpc(string animName)
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        if (anim != null && !string.IsNullOrEmpty(animName)) anim.Play(animName, 0, 0f);
    }

    public void ResetAI()
    {
        if (!IsServer) return;

        // MODIFICAT: Oprește toate corutinele înainte de a reseta
        StopAllCoroutines();
        pathModeCoroutine = null;

        currentSpotIndex = 0;
        timer = 0;
        ResetAllFlags();

        if (aiLevel.Value == 100) StartCoroutine(SpecialMode100Routine());
        else if (aiLevel.Value == 101)
        {
            if (pathStartPoint != null) UpdatePosition(pathStartPoint);
            pathModeCoroutine = StartCoroutine(EnterPathMode101(0));
        }
        else if (aiLevel.Value == 102) StartCoroutine(DirectAttackMode102Routine());
        else if (waypointMap != null && waypointMap.Count > 0)
        {
            UpdatePosition(waypointMap[0].pointTransform);
        }
    }

    // --- NOUA FUNCȚIONALITATE: JUMPSCARE TARGETED MULTI / SINGLE ---
    // trimite separat anim pentru clone/animatronic si pentru player (playerAnim nullable -> folosește cloneAnim)
    [ServerRpc(RequireOwnership = false)]
    public void StartJumpscareServerRpc(ulong[] targetClientIds, string cloneAnimName = null, string playerAnimName = null, float duration = -1f)
    {
        if (!IsServer) return;
        StartCoroutine(JumpscareDispatcherRoutine(targetClientIds, cloneAnimName, playerAnimName, duration));
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartJumpscareSingleServerRpc(ulong targetClientId, string cloneAnimName = null, string playerAnimName = null, float duration = -1f)
    {
        if (!IsServer) return;
        StartCoroutine(JumpscareDispatcherRoutine(new ulong[] { targetClientId }, cloneAnimName, playerAnimName, duration));
    }

    private IEnumerator JumpscareDispatcherRoutine(ulong[] targetClientIds, string cloneAnimName, string playerAnimName, float duration)
    {
        if (!IsServer) yield break;
        if (targetClientIds == null || targetClientIds.Length == 0) yield break;

        string usedCloneAnim = string.IsNullOrEmpty(cloneAnimName) ? generalJumpscareAnimName : cloneAnimName;
        string usedPlayerAnim = string.IsNullOrEmpty(playerAnimName) ? (string.IsNullOrEmpty(playerJumpscareAnimName) ? usedCloneAnim : playerJumpscareAnimName) : playerAnimName;
        float usedDuration = duration > 0f ? duration : generalJumpscareDuration;

        // PATH MODE (101) -> single, blocking
        if (aiLevel.Value == 101)
        {
            if (isInJumpscare)
            {
                yield break; // ignorăm dacă animatronicul e deja în jumpscare
            }

            float closest = float.MaxValue;
            ulong chosen = targetClientIds[0];
            foreach (var id in targetClientIds)
            {
                if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var cli) || cli.PlayerObject == null) continue;
                float d = Vector3.Distance(transform.position, cli.PlayerObject.transform.position);
                if (d < closest) { closest = d; chosen = id; }
            }

            yield return StartCoroutine(JumpscareRoutine101_Single(chosen, usedCloneAnim, usedPlayerAnim, usedDuration));
            yield break;
        }

        // GENERAL MODE -> spawn clone visuals on clients and tell each target client to play its player-jumpscare
        bool anyCloneSpawned = false;
        foreach (var id in targetClientIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null) continue;

            // calculează pe server poziția în fața playerului țintă (notă: + forward pentru IN FAȚĂ)
            Transform targetTransformServer = cl.PlayerObject.transform;
            Vector3 forward = targetTransformServer.forward;
            Vector3 spawnPos = targetTransformServer.position + forward.normalized * 2f + Vector3.up * 0.0f;
            Quaternion lookRot = Quaternion.LookRotation((targetTransformServer.position - spawnPos).normalized);

            // trimitem poziția + rotația la toți clienții (clientii vor instanția clone la acele coordonate)
            PlayJumpscareCloneClientRpc(spawnPos, lookRot, usedCloneAnim, usedDuration);

            // cerem clientului țintă să ruleze animația locală pe player
            PlayPlayerJumpscareClientRpc(id, usedPlayerAnim, usedDuration);

            anyCloneSpawned = true;
        }

        if (!anyCloneSpawned)
        {
            yield break;
        }

        // wait usedDuration then kill server-side
        float elapsed = 0f;
        while (elapsed < usedDuration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        foreach (var id in targetClientIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null) continue;
            GameManager.instance.KillPlayerServerRpc(id);
        }
    }

    // 101 single routine (animatronic teleports + plays on animatronic + tells player to play their anim)
    private IEnumerator JumpscareRoutine101_Single(ulong targetClientId, string animNameForAnimatronic, string animNameForPlayer, float duration)
    {
        if (!IsServer) yield break;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(targetClientId, out var client) || client.PlayerObject == null) yield break;

        var targetGO = client.PlayerObject.gameObject;
        Transform targetTransform = targetGO.transform;

        // MODIFICAT: Oprește path mode în timpul jumpscare-ului
        if (pathModeCoroutine != null)
        {
            StopCoroutine(pathModeCoroutine);
            pathModeCoroutine = null;
        }

        // salvează stare
        savedWasInSpecialMode = wasInSpecialMode;
        savedIsInPathMode = isInPathMode;
        savedIsInDirectAttackMode = isInDirectAttackMode;
        savedIsPreparingAttack = isPreparingAttack;
        savedCurrentSpotIndex = currentSpotIndex;
        savedTimer = timer;
        posBeforeJumpscare = transform.position;
        rotBeforeJumpscare = transform.rotation;

        isInJumpscare = true;

        // teleport în fața jucătorului (folosește + forward pentru IN FAȚĂ)
        Vector3 forward = targetTransform.forward;
        Vector3 spawnPos = targetTransform.position + forward.normalized * 2f + Vector3.up * 0.0f;
        Quaternion lookRot = Quaternion.LookRotation((targetTransform.position - spawnPos).normalized);

        UpdatePosition(spawnPos, lookRot);

        // rulează animația animatronic pe toți clienții (pe animatronicul real)
        PlayAnimationClientRpc(animNameForAnimatronic);

        // cere clientului țintă să ruleze animația sa locală
        PlayPlayerJumpscareClientRpc(targetClientId, animNameForPlayer, duration);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // omoară ținta (dupa animație)
        GameManager.instance.KillPlayerServerRpc(targetClientId);

        // revenire la poziția precedentă
        UpdatePosition(posBeforeJumpscare, rotBeforeJumpscare);

        // RESTAURARE STARE: folosim indexul și timer-ul salvat
        currentSpotIndex = savedCurrentSpotIndex;
        timer = savedTimer;
        wasInSpecialMode = savedWasInSpecialMode;
        isInPathMode = savedIsInPathMode;
        isInDirectAttackMode = savedIsInDirectAttackMode;
        isPreparingAttack = savedIsPreparingAttack;

        // MODIFICAT: repornește path mode de la indexul salvat, dacă mai suntem în modul 101
        if (aiLevel.Value == 101)
        {
            pathModeCoroutine = StartCoroutine(EnterPathMode101(savedCurrentSpotIndex));
        }

        // dă voie corutinelor să reia
        isInJumpscare = false;

        yield break;
    }

    // ClientRpc: instanțiază o clonă local (non-networked) în fața playerului target pentru a rula animație vizuală
    [ClientRpc]
    private void PlayJumpscareCloneClientRpc(Vector3 spawnPos, Quaternion lookRot, string animName, float duration)
    {
        if (jumpscareClonePrefab == null) return;

        GameObject go = Instantiate(jumpscareClonePrefab, spawnPos, lookRot);
        Animator cloneAnim = go.GetComponentInChildren<Animator>();
        if (cloneAnim != null && !string.IsNullOrEmpty(animName))
        {
            try { cloneAnim.Play(animName, 0, 0f); } catch { }
        }

        Destroy(go, duration + 0.1f);
    }

    // ClientRpc: cere CLIENTULUI ȚINTĂ să ruleze o rutină locală de "jumpscare" asemănătoare cutscenei
    [ClientRpc]
    private void PlayPlayerJumpscareClientRpc(ulong targetClientId, string animName, float duration)
    {
        // RULEAZĂ doar pe clientul vizat
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        // Oprește orice jumpscare anterior în desfășurare
        if (activeJumpscareCoroutine != null)
        {
            StopCoroutine(activeJumpscareCoroutine);
            activeJumpscareCoroutine = null;
        }

        activeJumpscareCoroutine = StartCoroutine(LocalPlayerJumpscareRoutine(animName, duration));
    }

    // Structură pentru a stoca starea înainte de jumpscare
    private class JumpscarePrep
    {
        public GameObject localPlayer;
        public PlayerMovement pm;
        public MouseLook ml;
        public CharacterController cc;
        public Animator playerAnim;
        public Camera playerCam;
        public bool pmEnabled, mlEnabled, ccEnabled;
        public Vector3 camOldLocalPos;
        public Quaternion camOldLocalRot;
        public Transform camOldParent;
        public Transform headBone;
        public bool usedTempCam;
        public GameObject tempCamGO;
        public bool hasModel;
        public Vector3 modelLocalPos;
        public Quaternion modelLocalRot;
        public bool wasUIActive;
    }

    private JumpscarePrep PrepareJumpscare(string animName)
    {
        var localClient = NetworkManager.Singleton.LocalClient;
        if (localClient?.PlayerObject == null) return null;

        GameObject localPlayer = localClient.PlayerObject.gameObject;
        if (localPlayer == null) return null;

        var prep = new JumpscarePrep
        {
            localPlayer = localPlayer,
            pm = localPlayer.GetComponent<PlayerMovement>(),
            ml = localPlayer.GetComponentInChildren<MouseLook>(true),
            cc = localPlayer.GetComponent<CharacterController>(),
            playerAnim = localPlayer.GetComponentInChildren<Animator>(true),
            playerCam = localPlayer.GetComponentInChildren<Camera>(true)
        };

        Inventory inv = localPlayer.GetComponent<Inventory>();
        if (inv != null) inv.isInputBlocked = true;

        // Salvare poziție model
        if (prep.playerAnim != null)
        {
            prep.hasModel = true;
            prep.modelLocalPos = prep.playerAnim.transform.localPosition;
            prep.modelLocalRot = prep.playerAnim.transform.localRotation;
        }

        // Salvare stare inițială
        prep.pmEnabled = prep.pm != null ? prep.pm.enabled : false;
        prep.mlEnabled = prep.ml != null ? prep.ml.enabled : false;
        prep.ccEnabled = prep.cc != null ? prep.cc.enabled : false;

        if (prep.playerCam != null)
        {
            prep.camOldParent = prep.playerCam.transform.parent;
            prep.camOldLocalPos = prep.playerCam.transform.localPosition;
            prep.camOldLocalRot = prep.playerCam.transform.localRotation;
        }

        // Dezactivare mișcare
        if (prep.pm != null) { prep.pm.enabled = false; prep.pm.canMove = false; }
        if (prep.ml != null) prep.ml.enabled = false;
        if (prep.cc != null) prep.cc.enabled = false;

        // Găsire head bone
        prep.headBone = FindChildRecursive(localPlayer.transform, "mixamorig12:Neck");

        // Salvare stare UI
        if (GameManager.instance != null)
        {
            GameManager.instance.HideAllUI();
        }

        // Setare cameră
        if (prep.playerCam != null && prep.headBone != null)
        {
            prep.playerCam.transform.SetParent(prep.headBone, false);
            prep.playerCam.transform.localPosition = Vector3.zero;
            prep.playerCam.transform.localRotation = Quaternion.identity;
            prep.playerCam.gameObject.SetActive(true);
            var camComp = prep.playerCam.GetComponent<Camera>();
            if (camComp != null) camComp.enabled = true;
        }
        else if (prep.playerCam == null)
        {
            prep.usedTempCam = true;
            prep.tempCamGO = new GameObject($"JumpscareTempCam_{NetworkManager.Singleton.LocalClientId}");
            var c = prep.tempCamGO.AddComponent<Camera>();
            prep.tempCamGO.transform.position = localPlayer.transform.position + Vector3.up * 1.6f;
            prep.tempCamGO.transform.rotation = localPlayer.transform.rotation;
            prep.tempCamGO.SetActive(true);
        }

        // Rulează animația de jumpscare
        if (prep.playerAnim != null && !string.IsNullOrEmpty(animName))
        {
            prep.playerAnim.enabled = true;
            try { prep.playerAnim.Play(animName, 0, 0f); } catch { Debug.LogWarning("[AnimatronicAI] Player animation play failed: " + animName); }
        }
        else if (prep.playerAnim == null)
        {
            Debug.LogWarning("[AnimatronicAI] Local player has no Animator component to play jumpscare animation.");
        }

        return prep;
    }

    private void RestoreJumpscare(JumpscarePrep prep)
    {
        if (prep.localPlayer == null) return;

        // Restaurare model
        if (prep.playerAnim != null && prep.hasModel)
        {
            prep.playerAnim.Rebind();
            prep.playerAnim.Update(0f);
            prep.playerAnim.transform.localPosition = prep.modelLocalPos;
            prep.playerAnim.transform.localRotation = prep.modelLocalRot;
            try { prep.playerAnim.Play("Movement", 0, 0f); } catch { }
        }

        // Restaurare cameră
        if (prep.playerCam != null && prep.headBone != null)
        {
            prep.playerCam.transform.SetParent(prep.camOldParent, false);
            prep.playerCam.transform.localPosition = prep.camOldLocalPos;
            prep.playerCam.transform.localRotation = prep.camOldLocalRot;
        }
        if (prep.usedTempCam && prep.tempCamGO != null) Destroy(prep.tempCamGO);

        // Reactivare componente
        if (prep.cc != null) prep.cc.enabled = prep.ccEnabled;
        if (prep.ml != null) prep.ml.enabled = prep.mlEnabled;
        if (prep.pm != null) { prep.pm.canMove = true; prep.pm.enabled = prep.pmEnabled; }

        // Restaurare UI
        if (GameManager.instance != null)
        {
            GameManager.instance.RestoreUI();
        }

        Inventory inv = prep.localPlayer.GetComponent<Inventory>();
        if (inv != null)
        {
            inv.isInputBlocked = false;
            inv.ResetIKState();  // ← adăugat
        }
    }

    private IEnumerator LocalPlayerJumpscareRoutine(string animName, float duration)
    {
        // Pregătire
        JumpscarePrep prep = null;
        try
        {
            prep = PrepareJumpscare(animName);
            if (prep == null)
            {
                activeJumpscareCoroutine = null;
                yield break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AnimatronicAI] Exception in PrepareJumpscare: {e}");
            activeJumpscareCoroutine = null;
            yield break;
        }

        // Așteaptă durata
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (prep.localPlayer == null || NetworkManager.Singleton.LocalClient?.PlayerObject == null)
            {
                if (prep.usedTempCam && prep.tempCamGO != null) Destroy(prep.tempCamGO);
                activeJumpscareCoroutine = null;
                yield break;
            }
            yield return null;
        }

        // Restaurare
        try
        {
            RestoreJumpscare(prep);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AnimatronicAI] Exception in RestoreJumpscare: {e}");
        }
        finally
        {
            activeJumpscareCoroutine = null;
        }

        yield break;
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform result = FindChildRecursive(child, name);
            if (result != null) return result;
        }
        return null;
    }

    public void TriggerJumpscare()
    {
        if (!IsServer) return;

        float closestDist = float.MaxValue;
        ulong targetId = ulong.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                float dist = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    targetId = client.ClientId;
                }
            }
        }

        if (targetId != ulong.MaxValue)
        {
            StartJumpscareSingleServerRpc(targetId, generalJumpscareAnimName, playerJumpscareAnimName == "" ? generalJumpscareAnimName : playerJumpscareAnimName, generalJumpscareDuration);
        }
    }
}
