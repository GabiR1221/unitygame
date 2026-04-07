using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public enum PhaseType { Normal, ActionZone }
public enum CutsceneTarget { Start, End, Phase }
public enum TriggerCondition { TimeBased, AfterPreviousPhase }

[System.Serializable]
public class CutsceneConfig
{
    public bool hasCutscene;
    public float duration = 5f;
    public string animationStateName = "CutsceneAnim"; 
    
    [Header("Locatii Start Cutscene")]
    public List<Transform> cutscenePoints;
    
    [Header("Locatii DUPA Cutscene (Optional)")]
    public List<Transform> postCutscenePoints;
    
    public string headBoneName = "mixamorig12:Neck"; 
}

[System.Serializable]
public class NightPhase
{
    public string phaseName = "Eveniment Nou";
    
    [Header("Conditie de Activare")]
    public TriggerCondition triggerCondition = TriggerCondition.TimeBased;
    public float triggerTime = 15f; 
    public PhaseType type = PhaseType.Normal;
    
    [Header("Terminare Faza (Timer)")]
    public bool endsAfterTimer = false;
    public float phaseDuration = 10f;

    [Header("Setari ActionZone")]
    public Transform zoneCenter; 
    public float zoneRadius = 4f; 
    public bool revertToNightBaseAfterZone = true;

    [Header("Dificultate AI in aceasta faza")]
    public int normalAIDifficulty = 1;
    public int lightAIDifficulty = 1;
    public bool robotNormalActiv = true;
    public bool robotLuminaActiv = true;

    [Header("Cutscene pentru aceasta faza (Optional)")]
    public CutsceneConfig phaseCutscene;

    [Header("Setari Notificari UI")]
    public bool showStartMessage = true;
    public string startMessageText = "A inceput o faza noua!";
    public bool showEndMessage = true;
    public string endMessageText = "Faza s-a incheiat.";
    public float messageDisplayDuration = 3f;

    [Header("Notificari Telefon (Telefonul trebuie sa fie in inventar)")]
    public bool sendPhoneMessageOnStart = false;
    [Tooltip("Ce elemente din lista telefonului sa se activeze la START? (Ex: 0, 1)")]
    public int[] startMessageIndices;

    public bool sendPhoneMessageOnEnd = false;
    [Tooltip("Ce elemente din lista telefonului sa se activeze la FINAL?")]
    public int[] endMessageIndices;

    [Header("Consum Stamina Jucator")]
    [Tooltip("1 = normal, 2 = se consuma de doua ori mai repede, 0.5 = mai lent")]
    public float staminaMultiplier = 1f;

    [Header("Muzica de Fundal (Optional)")]
    public AudioClip phaseMusic;
    public bool loopMusic = true;
    [Range(0f, 1f)] public float musicVolume = 1f;

    // --- ADAUGAT OBJECTIVE ---
    [Header("Obiectiv fază (dacă e gol, rămâne cel al nopții)")]
    public string phaseObjective = "";
}

[System.Serializable]
public class NightConfig
{
    public string nightDisplayName = "Night 1"; 
    public Transform nightSpawnPoint;
    public float nightDuration = 60f; 
    
    [Header("Cutscene Inceput de Noapte")]
    public CutsceneConfig startCutscene;

    [Header("Cutscene Final de Noapte")]
    public CutsceneConfig endCutscene;

    [Header("Dificultate Animatronici INITIALA")]
    public int normalAIDifficulty = 1;
    public int lightAIDifficulty = 1;
    public bool robotNormalActiv = true;
    public bool robotLuminaActiv = true;

    [Header("Consum Stamina Initial")]
    public float baseStaminaMultiplier = 1f;

    [Header("Muzica de Baza a Noptii")]
    public AudioClip baseNightMusic;
    [Range(0f, 1f)] public float baseMusicVolume = 1f;

    [Header("Evenimente / Faze pe parcursul noptii")]
    public List<NightPhase> phases;

    // --- ADAUGAT OBJECTIVE ---
    [Header("Obiectiv Noapte")]
    public string nightObjective = "Supraviețuiește noaptea";
}

public class GameManager : NetworkBehaviour
{
    public static GameManager instance;

    [Header("Configuratie Nopti")]
    public List<NightConfig> nightConfigs; 

    public NetworkVariable<int> currentNight = new NetworkVariable<int>(1);
    public NetworkVariable<bool> isGameStarted = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> isNightActive = new NetworkVariable<bool>(false);
    
    public NetworkVariable<float> currentStaminaMultiplier = new NetworkVariable<float>(1f);

    private bool isLocalCutsceneActive = false;
    private bool introPlayedForCurrentNight = false;
    private bool[] uiPreviousStates;

    private Coroutine activePhaseTimer;
    private Coroutine currentUINotification; 

    [Header("UI & Setari")]
    public TextMeshProUGUI nightText;
    public float introDuration = 3f;

    // --- ADAUGAT OBJECTIVE ---
    [Header("Obiective UI")]
    public TextMeshProUGUI objectiveText;
    private ObjectiveUI objectiveUI;

    [Header("Audio")]
    public AudioSource bgmAudioSource;

    [Header("UI de ascuns în timpul evenimentelor")]
    public GameObject uiToHideDuringEvents;

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
        instance = this;
        Application.targetFrameRate = 60;

        if (bgmAudioSource == null)
        {
            bgmAudioSource = gameObject.AddComponent<AudioSource>();
            bgmAudioSource.loop = true;
            bgmAudioSource.playOnAwake = false;
        }

        // Găsește componenta ObjectiveUI pe același obiect ca objectiveText (sau pe părinte)
        if (objectiveText != null)
        {
            objectiveUI = objectiveText.GetComponentInParent<ObjectiveUI>();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        if (isGameStarted.Value) return;
        isGameStarted.Value = true;
        
        introPlayedForCurrentNight = false; 

        ResurrectAllPlayers(); 
        int index = Mathf.Clamp(currentNight.Value - 1, 0, nightConfigs.Count - 1);
        ResetPlayersClientRpc(index); 

        StartCoroutine(NightCycleRoutine());
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void SetNightServerRpc(int night) 
    {
        currentNight.Value = night;
        introPlayedForCurrentNight = false; 
    }

    IEnumerator NightCycleRoutine()
    {
        int index = Mathf.Clamp(currentNight.Value - 1, 0, nightConfigs.Count - 1);
        NightConfig config = nightConfigs[index];

        isNightActive.Value = false;
        ApplyAIState(false, 0, 0, false, false); 
        
        currentStaminaMultiplier.Value = config.baseStaminaMultiplier;

        StopMusicClientRpc();

        if (activePhaseTimer != null) 
        {
            StopCoroutine(activePhaseTimer);
            activePhaseTimer = null;
        }

        if (config.startCutscene.hasCutscene && !introPlayedForCurrentNight)
        {
            TriggerCutsceneClientRpc(index, CutsceneTarget.Start, -1);
            yield return new WaitForSeconds(config.startCutscene.duration);
            introPlayedForCurrentNight = true; 
            
            ShowUINotificationClientRpc(config.nightDisplayName, introDuration);
            // --- ADAUGAT OBJECTIVE ---
            UpdateObjectiveClientRpc(config.nightObjective);
        }
        else
        {
            ShowUINotificationClientRpc(config.nightDisplayName, introDuration);
            // --- ADAUGAT OBJECTIVE ---
            UpdateObjectiveClientRpc(config.nightObjective);
            yield return new WaitForSeconds(introDuration);
        }

        isNightActive.Value = true;
        ApplyAIState(true, config.normalAIDifficulty, config.lightAIDifficulty, config.robotNormalActiv, config.robotLuminaActiv);

        if (config.baseNightMusic != null)
        {
            PlayMusicClientRpc(index, -1);
        }

        float currentNightTime = 0f;
        int currentPhaseIndex = 0;

        while (currentNightTime < config.nightDuration)
        {
            if (currentPhaseIndex < config.phases.Count)
            {
                NightPhase nextPhase = config.phases[currentPhaseIndex];
                bool readyToTrigger = false;

                if (nextPhase.triggerCondition == TriggerCondition.TimeBased)
                {
                    if (currentNightTime >= nextPhase.triggerTime) readyToTrigger = true;
                }
                else if (nextPhase.triggerCondition == TriggerCondition.AfterPreviousPhase)
                {
                    readyToTrigger = true; 
                }

                if (readyToTrigger)
                {
                    if (activePhaseTimer != null) 
                    {
                        StopCoroutine(activePhaseTimer);
                        activePhaseTimer = null;
                    }
                    
                    if (nextPhase.showStartMessage)
                    {
                        ShowUINotificationClientRpc(nextPhase.startMessageText, nextPhase.messageDisplayDuration);
                    }

                    // --- ADAUGAT OBJECTIVE (actualizare înainte de orice) ---
                    string obj = string.IsNullOrEmpty(nextPhase.phaseObjective) ? config.nightObjective : nextPhase.phaseObjective;
                    UpdateObjectiveClientRpc(obj);

                    if (nextPhase.sendPhoneMessageOnStart)
                    {
                        TriggerSpecificPhoneMessagesClientRpc(nextPhase.startMessageIndices);
                    }
                    
                    ApplyAIState(true, nextPhase.normalAIDifficulty, nextPhase.lightAIDifficulty, nextPhase.robotNormalActiv, nextPhase.robotLuminaActiv);
                    currentStaminaMultiplier.Value = nextPhase.staminaMultiplier;

                    if (nextPhase.phaseMusic != null)
                    {
                        PlayMusicClientRpc(index, currentPhaseIndex);
                    }

                    if (nextPhase.type == PhaseType.ActionZone)
                    {
                        while (!AreAllPlayersInZone(nextPhase.zoneCenter, nextPhase.zoneRadius))
                        {
                            yield return new WaitForSeconds(0.5f); 
                        }

                        if (nextPhase.phaseCutscene.hasCutscene)
                        {
                            TriggerCutsceneClientRpc(index, CutsceneTarget.Phase, currentPhaseIndex);
                            yield return new WaitForSeconds(nextPhase.phaseCutscene.duration);
                        }

                        if (nextPhase.revertToNightBaseAfterZone)
                        {
                            ApplyAIState(true, config.normalAIDifficulty, config.lightAIDifficulty, config.robotNormalActiv, config.robotLuminaActiv);
                            currentStaminaMultiplier.Value = config.baseStaminaMultiplier;
                            
                            // --- ADAUGAT OBJECTIVE (revenim la obiectivul nopții) ---
                            UpdateObjectiveClientRpc(config.nightObjective);

                            if (config.baseNightMusic != null)
                            {
                                PlayMusicClientRpc(index, -1);
                            }
                            else
                            {
                                StopMusicClientRpc();
                            }
                        }
                    }
                    else 
                    {
                        if (nextPhase.phaseCutscene.hasCutscene)
                        {
                            TriggerCutsceneClientRpc(index, CutsceneTarget.Phase, currentPhaseIndex);
                            yield return new WaitForSeconds(nextPhase.phaseCutscene.duration);
                        }
                    }

                    if (nextPhase.endsAfterTimer)
                    {
                        activePhaseTimer = StartCoroutine(PhaseTimerExpiration(nextPhase, config, index));
                    }

                    currentPhaseIndex++;
                }
            }

            yield return null;
            currentNightTime += Time.deltaTime;
        }

        isNightActive.Value = false;
        ApplyAIState(false, 0, 0, false, false); 
        
        if (activePhaseTimer != null) 
        {
            StopCoroutine(activePhaseTimer);
            activePhaseTimer = null;
        }

        StopMusicClientRpc();

        if (config.endCutscene.hasCutscene)
        {
            TriggerCutsceneClientRpc(index, CutsceneTarget.End, -1);
            yield return new WaitForSeconds(config.endCutscene.duration);
        }

        // --- ADAUGAT OBJECTIVE (golim sau setăm un mesaj de final) ---
        UpdateObjectiveClientRpc("Noapte încheiată");

        EndNightServerRpc();
    }

    IEnumerator PhaseTimerExpiration(NightPhase phase, NightConfig baseConfig, int nightIndex)
    {
        yield return new WaitForSeconds(phase.phaseDuration);
        
        if (phase.showEndMessage)
        {
            ShowUINotificationClientRpc(phase.endMessageText, phase.messageDisplayDuration);
        }
        
        if (phase.sendPhoneMessageOnEnd)
        {
            TriggerSpecificPhoneMessagesClientRpc(phase.endMessageIndices);
        }
        
        ApplyAIState(true, baseConfig.normalAIDifficulty, baseConfig.lightAIDifficulty, baseConfig.robotNormalActiv, baseConfig.robotLuminaActiv);
        currentStaminaMultiplier.Value = baseConfig.baseStaminaMultiplier;

        // --- ADAUGAT OBJECTIVE (revenim la obiectivul nopții) ---
        UpdateObjectiveClientRpc(baseConfig.nightObjective);

        if (baseConfig.baseNightMusic != null)
        {
            PlayMusicClientRpc(nightIndex, -1);
        }
        else
        {
            StopMusicClientRpc();
        }
    }

    // Verifică dacă toți jucătorii sunt morți
    private bool AreAllPlayersDead()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var state = client.PlayerObject.GetComponent<PlayerState>();
                if (state != null && state.isAlive.Value)
                    return false;
            }
        }
        return true;
    }

    // Înviază toți jucătorii morți
    private void ResurrectAllPlayers()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var state = client.PlayerObject.GetComponent<PlayerState>();
                if (state != null && !state.isAlive.Value)
                    state.ResurrectServerRpc();
            }
        }
    }

    // Adaugă aceste două metode în clasa GameManager (unde sunt celelalte metode private)
    private bool AnyAlivePlayers()
    {
        // returnează true dacă există măcar un player alive în ConnectedClientsList
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var state = client.PlayerObject.GetComponent<PlayerState>();
                if (state != null && state.isAlive.Value)
                    return true;
            }
        }
        return false;
    }

    private void DoJumpscareReset()
    {
        // Această metodă conține aceeași logică ca înainte dar rulează direct pe server
        if (!IsServer) return;

        StopAllCoroutines();
        isNightActive.Value = false;

        // rulează client RPC-urile necesare
        ForceHideUIClientRpc();
        StopMusicClientRpc();

        if (activePhaseTimer != null)
        {
            StopCoroutine(activePhaseTimer);
            activePhaseTimer = null;
        }

        AnimatronicAI[] ais = Object.FindObjectsByType<AnimatronicAI>(FindObjectsSortMode.None);
        foreach (var ai in ais) ai.ResetAI();

        LightSensitiveAI[] lights = Object.FindObjectsByType<LightSensitiveAI>(FindObjectsSortMode.None);
        foreach (var l in lights) l.ResetAI();

        ResurrectAllPlayers();

        int index = Mathf.Clamp(currentNight.Value - 1, 0, nightConfigs.Count - 1);
        ResetPlayersClientRpc(index);

        StartCoroutine(NightCycleRoutine());
        ResetAllDoorsServerSide();
    }

    // Omorâre specifică unui jucător
    [ServerRpc(RequireOwnership = false)]
    public void KillPlayerServerRpc(ulong targetClientId)
    {
        if (!IsServer) return;

        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(targetClientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                var state = client.PlayerObject.GetComponent<PlayerState>();
                if (state != null && state.isAlive.Value)
                {
                    state.DieServerRpc();

                    // Opțional: rulează efecte locale pe acel client
                    TriggerLocalJumpscareClientRpc(targetClientId);
                }
            }
        }

        // Dacă toți sunt morți, resetează noaptea
        if (AreAllPlayersDead())
        {
            JumpscareResetServerRpc();
        }
    }

    [ClientRpc]
    private void TriggerLocalJumpscareClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == targetClientId)
        {
            // Aici rulezi efectele de jumpscare pentru jucătorul local (sunet, UI etc.)
            Debug.Log("Jumpscare pentru tine!");
        }
    }

    [ClientRpc]
    void PlayMusicClientRpc(int nightIndex, int phaseIndex)
    {
        if (bgmAudioSource == null) return;

        NightConfig nConfig = nightConfigs[nightIndex];
        AudioClip clipToPlay = null;
        float volume = 1f;
        bool loop = true;

        if (phaseIndex == -1) 
        {
            clipToPlay = nConfig.baseNightMusic;
            volume = nConfig.baseMusicVolume;
        }
        else if (phaseIndex >= 0 && phaseIndex < nConfig.phases.Count) 
        {
            clipToPlay = nConfig.phases[phaseIndex].phaseMusic;
            volume = nConfig.phases[phaseIndex].musicVolume;
            loop = nConfig.phases[phaseIndex].loopMusic;
        }

        if (clipToPlay != null)
        {
            bgmAudioSource.clip = clipToPlay;
            bgmAudioSource.volume = volume;
            bgmAudioSource.loop = loop;
            bgmAudioSource.Play();
        }
    }

    [ClientRpc]
    void StopMusicClientRpc()
    {
        if (bgmAudioSource != null && bgmAudioSource.isPlaying)
        {
            bgmAudioSource.Stop();
        }
    }

    // --- ClientRpc pentru actualizare text ---
    [ClientRpc]
    void UpdateObjectiveClientRpc(string newObjective)
    {
        if (objectiveText != null)
            objectiveText.text = newObjective;

        // Notifică ObjectiveUI să afișeze noul text
        if (objectiveUI != null)
            objectiveUI.ShowObjective(newObjective);
    }

    void ApplyAIState(bool active, int nDiff, int lDiff, bool nOn, bool lOn)
    {
        if (!IsServer) return;

        AnimatronicAI[] normalAIs = Object.FindObjectsByType<AnimatronicAI>(FindObjectsSortMode.None);
        foreach(var ai in normalAIs) {
            ai.disableAI = !active || !nOn;
            ai.aiLevel.Value = nDiff; 
            ai.ResetAI(); 
        }

        LightSensitiveAI[] lightAIs = Object.FindObjectsByType<LightSensitiveAI>(FindObjectsSortMode.None);
        foreach(var lai in lightAIs) {
            lai.disableAI = !active || !lOn;
            lai.aiLevel.Value = lDiff; 
            lai.ResetAI();
        }
    }

    [ClientRpc]
    void TriggerSpecificPhoneMessagesClientRpc(int[] messageIndices)
    {
        if (NetworkManager.Singleton.LocalClient?.PlayerObject == null) return;
        
        PhoneController phone = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponentInChildren<PhoneController>(true);
        
        if (phone != null && messageIndices != null && messageIndices.Length > 0)
        {
            StartCoroutine(SendSpecificMessages(phone, messageIndices));
        }
    }


    IEnumerator SendSpecificMessages(PhoneController phone, int[] indices)
    {
        foreach (int index in indices)
        {
            phone.ReceiveMessageByIndex(index);
            yield return new WaitForSeconds(0.5f); // Pauză între mesaje
        }
    }

    private bool AreAllPlayersInZone(Transform zone, float radius)
    {
        if (zone == null) return true; 

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                float distance = Vector3.Distance(client.PlayerObject.transform.position, zone.position);
                if (distance > radius) return false; 
            }
        }
        return true; 
    }

    [ClientRpc]
    void ShowUINotificationClientRpc(string text, float duration)
    {
        if (currentUINotification != null) StopCoroutine(currentUINotification);
        currentUINotification = StartCoroutine(UINotificationRoutine(text, duration));
    }

    [ClientRpc]
    void ForceHideUIClientRpc()
    {
        if (currentUINotification != null) StopCoroutine(currentUINotification);
        if (nightText != null) nightText.gameObject.SetActive(false);
    }

    IEnumerator UINotificationRoutine(string text, float duration)
    {
        if (nightText != null)
        {
            nightText.text = text;
            nightText.gameObject.SetActive(true);
            yield return new WaitForSeconds(duration);
            nightText.gameObject.SetActive(false);
        }
    }

    [ClientRpc]
    void TriggerCutsceneClientRpc(int nightIndex, CutsceneTarget target, int phaseIndex)
    {
        StartCoroutine(LocalCutsceneRoutine(nightIndex, target, phaseIndex));
    }

    IEnumerator LocalCutsceneRoutine(int nightIndex, CutsceneTarget target, int phaseIndex)
    {
        if (isLocalCutsceneActive) yield break;
        isLocalCutsceneActive = true;

        NightConfig nConfig = nightConfigs[nightIndex];
        CutsceneConfig cConfig = null;

        if (target == CutsceneTarget.Start) cConfig = nConfig.startCutscene;
        else if (target == CutsceneTarget.End) cConfig = nConfig.endCutscene;
        else if (target == CutsceneTarget.Phase && phaseIndex >= 0) cConfig = nConfig.phases[phaseIndex].phaseCutscene;

        if (cConfig == null || !cConfig.hasCutscene)
        {
            isLocalCutsceneActive = false;
            yield break;
        }

        // Ascunde UI-ul pe durata cutscene-ului
        HideAllUI();

        GameObject localPlayer = null; // declarat aici pentru a fi accesibil în finally

        try
        {
            // wait for local player
            float timeout = 0f;
            while (timeout < 3f)
            {
                var localClient = NetworkManager.Singleton.LocalClient;
                if (localClient?.PlayerObject != null && localClient.PlayerObject.IsSpawned) break;
                timeout += Time.deltaTime;
                yield return null;
            }
            if (NetworkManager.Singleton.LocalClient?.PlayerObject == null)
            {
                yield break;
            }

            localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;

            // Blochează inventarul
            Inventory inv = localPlayer.GetComponent<Inventory>();
            if (inv != null) inv.isInputBlocked = true;

            // --- SALVĂ pozițiile modelelor ---
            PlayerMovement[] allPlayers = Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
            Dictionary<PlayerMovement, Vector3> oldModelPositions = new Dictionary<PlayerMovement, Vector3>();
            Dictionary<PlayerMovement, Quaternion> oldModelRotations = new Dictionary<PlayerMovement, Quaternion>();
            foreach (var p in allPlayers)
            {
                Animator pAnim = p.GetComponentInChildren<Animator>(true);
                if (pAnim != null)
                {
                    oldModelPositions[p] = pAnim.transform.localPosition;
                    oldModelRotations[p] = pAnim.transform.localRotation;
                }
            }

            // --- PENTRU JUCĂTORUL LOCAL ---
            CharacterController cc = localPlayer.GetComponent<CharacterController>();
            Animator anim = localPlayer.GetComponentInChildren<Animator>(true);
            Camera playerCam = localPlayer.GetComponentInChildren<Camera>(true);

            List<Behaviour> disabledDuringCutscene = new List<Behaviour>();

            var pm = localPlayer.GetComponent<PlayerMovement>();
            if (pm != null)
            {
                if (pm.enabled)
                {
                    pm.enabled = false;
                    disabledDuringCutscene.Add(pm);
                }
                pm.canMove = false;
            }

            var ml = localPlayer.GetComponentInChildren<MouseLook>(true);
            if (ml != null && ml.enabled)
            {
                ml.enabled = false;
                disabledDuringCutscene.Add(ml);
            }

            if (playerCam != null)
            {
                var mlCam = playerCam.GetComponent<MouseLook>();
                if (mlCam != null && mlCam.enabled && !disabledDuringCutscene.Contains(mlCam))
                {
                    mlCam.enabled = false;
                    disabledDuringCutscene.Add(mlCam);
                }
            }

            var allScripts = localPlayer.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var s in allScripts)
            {
                if (s == null) continue;
                string tname = s.GetType().Name.ToLower();
                if (tname == "playerstate" || tname == "playerdeathhandler" || tname == "gamemanager") continue;
                if (disabledDuringCutscene.Contains(s as Behaviour)) continue;

                bool looksLikeMovement = tname.Contains("move") || tname.Contains("movement") ||
                                        tname.Contains("input") || tname.Contains("controller") ||
                                        tname.Contains("look") || tname.Contains("mouse") ||
                                        tname.Contains("aim") || tname.Contains("fps") ||
                                        (tname.Contains("player") && !tname.Equals("playerstate"));

                if (looksLikeMovement)
                {
                    var b = s as Behaviour;
                    if (b != null && b.enabled)
                    {
                        b.enabled = false;
                        disabledDuringCutscene.Add(b);
                    }
                }
            }

            Rigidbody rb = localPlayer.GetComponent<Rigidbody>();
            bool rbWasKinematic = false;
            if (rb != null)
            {
                rbWasKinematic = rb.isKinematic;
                rb.isKinematic = true;
            }
            if (cc != null) cc.enabled = false;

            if (cConfig.cutscenePoints != null && cConfig.cutscenePoints.Count > 0)
            {
                int spawnIndex = (int)NetworkManager.Singleton.LocalClientId % cConfig.cutscenePoints.Count;
                localPlayer.transform.SetPositionAndRotation(cConfig.cutscenePoints[spawnIndex].position, cConfig.cutscenePoints[spawnIndex].rotation);
            }

            Transform headBone = null;
            Transform originalParent = null;
            Vector3 oldLocalPos = Vector3.zero;
            Quaternion oldLocalRot = Quaternion.identity;
            bool usedTempCam = false;
            GameObject cutsceneTempCamGO = null;
            Camera cutsceneTempCam = null;
            Behaviour[] camBehaviours = null;

            if (playerCam != null)
            {
                originalParent = playerCam.transform.parent;
                oldLocalPos = playerCam.transform.localPosition;
                oldLocalRot = playerCam.transform.localRotation;

                headBone = FindChildRecursive(localPlayer.transform, cConfig.headBoneName);
                if (headBone != null)
                {
                    playerCam.transform.SetParent(headBone, false);
                    playerCam.transform.localPosition = Vector3.zero;
                    playerCam.transform.localRotation = Quaternion.identity;
                    playerCam.gameObject.SetActive(true);
                    var camComp = playerCam.GetComponent<Camera>();
                    if (camComp != null) camComp.enabled = true;
                }
                else
                {
                    usedTempCam = true;
                }

                camBehaviours = playerCam.GetComponents<Behaviour>();
            }
            else
            {
                usedTempCam = true;
            }

            if (usedTempCam)
            {
                cutsceneTempCamGO = new GameObject($"CutsceneCam_{NetworkManager.Singleton.LocalClientId}");
                cutsceneTempCam = cutsceneTempCamGO.AddComponent<Camera>();
                if (playerCam != null)
                {
                    cutsceneTempCam.fieldOfView = playerCam.fieldOfView;
                    cutsceneTempCam.nearClipPlane = playerCam.nearClipPlane;
                    cutsceneTempCam.farClipPlane = playerCam.farClipPlane;
                    cutsceneTempCam.clearFlags = playerCam.clearFlags;
                    cutsceneTempCam.cullingMask = playerCam.cullingMask;
                }
                if (cConfig.cutscenePoints != null && cConfig.cutscenePoints.Count > 0)
                {
                    int spawnIndex = (int)NetworkManager.Singleton.LocalClientId % cConfig.cutscenePoints.Count;
                    cutsceneTempCam.transform.position = cConfig.cutscenePoints[spawnIndex].position;
                    cutsceneTempCam.transform.rotation = cConfig.cutscenePoints[spawnIndex].rotation;
                }
                else
                {
                    cutsceneTempCam.transform.position = localPlayer.transform.position + Vector3.up * 1.6f;
                    cutsceneTempCam.transform.rotation = Quaternion.LookRotation(localPlayer.transform.forward);
                }
                cutsceneTempCam.gameObject.SetActive(true);
            }

            if (anim != null && !string.IsNullOrEmpty(cConfig.animationStateName))
            {
                anim.enabled = true;
                try { anim.Play(cConfig.animationStateName, 0, 0f); } catch { }
            }

            yield return new WaitForSeconds(cConfig.duration);

            // --- RESTAURARE ---
            foreach (var p in allPlayers)
            {
                if (p != null)
                {
                    Animator pAnim = p.GetComponentInChildren<Animator>(true);
                    if (pAnim != null && oldModelPositions.ContainsKey(p) && oldModelRotations.ContainsKey(p))
                    {
                        pAnim.Rebind();
                        pAnim.Update(0f);
                        pAnim.transform.localPosition = oldModelPositions[p];
                        pAnim.transform.localRotation = oldModelRotations[p];
                        pAnim.Play("Movement", 0, 0f);
                    }
                }
            }

            if (!usedTempCam && playerCam != null)
            {
                if (originalParent != null)
                {
                    playerCam.transform.SetParent(originalParent, false);
                    playerCam.transform.localPosition = oldLocalPos;
                    playerCam.transform.localRotation = oldLocalRot;
                }
                else
                {
                    playerCam.transform.SetParent(localPlayer.transform, true);
                    playerCam.transform.localPosition = oldLocalPos;
                    playerCam.transform.localRotation = oldLocalRot;
                }

                foreach (var b in camBehaviours ?? new Behaviour[0])
                {
                    if (b == null) continue;
                    if (disabledDuringCutscene.Contains(b))
                        b.enabled = true;
                }
            }
            else
            {
                if (cutsceneTempCamGO != null) Destroy(cutsceneTempCamGO);
                if (playerCam != null)
                {
                    playerCam.gameObject.SetActive(true);
                    var camComp = playerCam.GetComponent<Camera>();
                    if (camComp != null) camComp.enabled = true;
                }
            }

            if (cc != null) cc.enabled = true;
            if (rb != null) rb.isKinematic = rbWasKinematic;

            foreach (var b in disabledDuringCutscene)
            {
                if (b != null) b.enabled = true;
            }

            if (pm != null)
            {
                pm.enabled = true;
                pm.canMove = true;
            }
            if (ml != null) ml.enabled = true;
            if (playerCam != null)
            {
                var mlCam = playerCam.GetComponent<MouseLook>();
                if (mlCam != null) mlCam.enabled = true;
            }

            if (cConfig.postCutscenePoints != null && cConfig.postCutscenePoints.Count > 0)
            {
                int postSpawnIndex = (int)NetworkManager.Singleton.LocalClientId % cConfig.postCutscenePoints.Count;
                localPlayer.transform.SetPositionAndRotation(
                    cConfig.postCutscenePoints[postSpawnIndex].position,
                    cConfig.postCutscenePoints[postSpawnIndex].rotation
                );
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        finally
        {
            // Deblochează inventarul
            if (localPlayer != null)
            {
                Inventory inv = localPlayer.GetComponent<Inventory>();
                if (inv != null)
                {
                    inv.isInputBlocked = false;
                    inv.ResetIKState();  // ← adăugat
                }
            }
            // Reactivare UI
            RestoreUI();
            isLocalCutsceneActive = false;
        }
    }

    [ServerRpc]
    void EndNightServerRpc()
    {
        isNightActive.Value = false;
        currentNight.Value++; 
        
        introPlayedForCurrentNight = false; 
        
        if (activePhaseTimer != null) 
        {
            StopCoroutine(activePhaseTimer);
            activePhaseTimer = null;
        }
        
        ResurrectAllPlayers(); 
        int index = Mathf.Clamp(currentNight.Value - 1, 0, nightConfigs.Count - 1);
        ResetPlayersClientRpc(index); 

        StartCoroutine(NightCycleRoutine());
        ResetAllDoorsServerSide();
    }

    [ServerRpc(RequireOwnership = false)]
    public void JumpscareResetServerRpc()
    {
        DoJumpscareReset();
        StopAllCoroutines(); 
        isNightActive.Value = false;
        
        ForceHideUIClientRpc(); 
        StopMusicClientRpc(); 
        
        if (activePhaseTimer != null) 
        {
            StopCoroutine(activePhaseTimer);
            activePhaseTimer = null;
        }
        
        AnimatronicAI[] ais = Object.FindObjectsByType<AnimatronicAI>(FindObjectsSortMode.None);
        foreach(var ai in ais) ai.ResetAI();
        
        LightSensitiveAI[] lights = Object.FindObjectsByType<LightSensitiveAI>(FindObjectsSortMode.None);
        foreach(var l in lights) l.ResetAI();

        ResurrectAllPlayers(); // <-- ADĂUGAT

        int index = Mathf.Clamp(currentNight.Value - 1, 0, nightConfigs.Count - 1);
        ResetPlayersClientRpc(index); 

        StartCoroutine(NightCycleRoutine());
        ResetAllDoorsServerSide(); 
    }

    [ClientRpc]
    void ResetPlayersClientRpc(int nightIndex) 
    {
        if (NetworkManager.Singleton.LocalClient?.PlayerObject == null) return;
        GameObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;

        DoorSystem[] doors = Object.FindObjectsByType<DoorSystem>(FindObjectsSortMode.None);
        foreach (var door in doors) if (door.isAtDoor) door.ForceExitThreshold();

        CharacterController cc = localPlayer.GetComponent<CharacterController>();
        
        var allScripts = localPlayer.GetComponentsInChildren<MonoBehaviour>();
        foreach (var s in allScripts) 
        {
            if (s is NetworkBehaviour || s == this) continue;
            if (s.enabled) s.enabled = false;
        }
        
        if (cc != null) cc.enabled = false; 

        if(nightIndex >= 0 && nightIndex < nightConfigs.Count)
        {
            Transform targetSpawn = nightConfigs[nightIndex].nightSpawnPoint;

            if(targetSpawn != null)
            {
                localPlayer.transform.position = targetSpawn.position;
                localPlayer.transform.rotation = targetSpawn.rotation;
            }
        }

        if (cc != null) cc.enabled = true;
        
        foreach (var s in allScripts) 
        {
            if(s != null && !(s is NetworkBehaviour)) s.enabled = true;
        }
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnect;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }
        NetworkManager.Singleton.OnClientDisconnectCallback += OnLocalDisconnect;
    }

    private void HandleClientConnect(ulong clientId)
    {
        if (isGameStarted.Value) NetworkManager.Singleton.DisconnectClient(clientId);
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        DoorSystem[] doors = Object.FindObjectsByType<DoorSystem>(FindObjectsSortMode.None);
        foreach (var door in doors)
        {
            if (door.isOccupied.Value && door.occupierId.Value == clientId)
            {
                door.SetOccupiedServerRpc(false, 0);
                door.SetDoorProgressServerRpc(0f);
            }
        }

        // După ce am curățat ușile, verificăm dacă a rămas vreun jucător VIU
        // Daca nu există niciun jucător alive dar mai sunt clienți conectați (dead spectatori),
        // atunci forțăm resetul pe server (echivalent JumpscareReset).
        if (IsServer)
        {
            int clientCount = NetworkManager.Singleton.ConnectedClientsList.Count;
            if (clientCount > 0 && !AnyAlivePlayers())
            {
                Debug.Log("[GameManager] Ultimul jucător viu a părăsit meciul — rulăm reset server-side.");
                DoJumpscareReset();
            }
        }
    }

    private void OnLocalDisconnect(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void ResetAllDoorsServerSide()
    {
        if (!IsServer) return;
        DoorSystem[] doors = Object.FindObjectsByType<DoorSystem>(FindObjectsSortMode.None);
        foreach (var door in doors)
        {
            door.isOccupied.Value = false;
            door.occupierId.Value = 0;
            door.currentProgress.Value = 0f; 
            door.isDoorFullyClosed.Value = false;
        }
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

    public void LeaveAndReset()
    {
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
