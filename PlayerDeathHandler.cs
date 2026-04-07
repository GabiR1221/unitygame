using Unity.Netcode;
using UnityEngine;

public class PlayerDeathHandler : NetworkBehaviour
{
    [Header("Referințe")]
    public PlayerState playerState;
    public CharacterController characterController;
    public MonoBehaviour[] scriptsToDisableOnDeath;
    public GameObject playerModel;

    // Poți trage în Inspector obiectul camerei tale locale (de obicei child în prefab).
    // Poți trage spectatorCamera ca GameObject (trebuie să conțină Camera + AudioListener + SpectatorCamera script).
    public GameObject mainCamera;
    public GameObject spectatorCamera;

    private void Awake()
    {
        if (playerState == null)
            playerState = GetComponent<PlayerState>();
    }

    public override void OnNetworkSpawn()
    {
        if (playerState != null)
        {
            playerState.isAlive.OnValueChanged += OnAliveStateChanged;

            // Setăm doar corpul/vizualul la început, NU umblăm la camere în Lobby!
            if (playerModel != null) playerModel.SetActive(playerState.isAlive.Value);
            if (characterController != null) characterController.enabled = playerState.isAlive.Value;
        }

        // Dacă spectatorCamera nu e setat în inspector, încercăm să-l găsim local (child) sau în scenă
        if (spectatorCamera == null)
        {
            // cautăm un component SpectatorCamera la copii
            var specComp = GetComponentInChildren<SpectatorCamera>(true);
            if (specComp != null) spectatorCamera = specComp.gameObject;
            else
            {
                // fallback: căutăm în toată scena (doar pentru clientul owner)
                var specScene = FindObjectOfType<SpectatorCamera>(true);
                if (specScene != null) spectatorCamera = specScene.gameObject;
            }
        }

        // Detașăm spectatorCamera din ierarhia playerului dacă e child — astfel nu va fi dezactivată când dezactivăm modelul
        if (spectatorCamera != null && spectatorCamera.transform.IsChildOf(transform))
        {
            spectatorCamera.transform.SetParent(null, true);
            spectatorCamera.SetActive(false); // să fie oprită până la moarte
        }

        // Dacă mainCamera nu e setată, încercăm să o găsim în copii
        if (mainCamera == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) mainCamera = cam.gameObject;
        }
    }

    private void OnAliveStateChanged(bool previousValue, bool newValue)
    {
        // 1. ASCUNDEM/ARATĂ MODELUL PENTRU TOATĂ LUMEA (narativ și gameplay)
        if (newValue)
        {
            if (playerModel != null) playerModel.SetActive(true);
            if (characterController != null) characterController.enabled = true;
        }
        else
        {
            if (playerModel != null) playerModel.SetActive(false);
            if (characterController != null) characterController.enabled = false;
        }

        // 2. SCHIMBĂM CAMERELE DOAR PENTRU CEL CARE MOARE (owner)
        if (!IsOwner) return;

        // Daca nu e jocul început, nu ne atingem de camere (rămâne camera din meniu)
        bool isGameStarted = GameManager.instance != null && GameManager.instance.isGameStarted.Value;
        if (!isGameStarted) return;

        if (newValue) // e viu -> readucem controlul jucătorului
        {
            EnableLocalScripts(true);

            // Reactivăm camera principală (dacă există)
            if (mainCamera != null)
            {
                var camComp = mainCamera.GetComponent<Camera>();
                if (camComp != null) camComp.enabled = true;
                mainCamera.SetActive(true);

                var al = mainCamera.GetComponent<AudioListener>();
                if (al != null) al.enabled = true;
            }

            // Oprim spectatorul
            if (spectatorCamera != null)
            {
                var specCam = spectatorCamera.GetComponent<Camera>();
                if (specCam != null) specCam.enabled = false;
                var specAL = spectatorCamera.GetComponent<AudioListener>();
                if (specAL != null) specAL.enabled = false;
                var specScript = spectatorCamera.GetComponent<SpectatorCamera>();
                if (specScript != null) specScript.enabled = false;

                spectatorCamera.SetActive(false);
            }
        }
        else // ai murit -> activează spectatorul înainte să oprești camera principală
        {
            EnableLocalScripts(false);

            // 1) Ensure spectator camera exists and is detached (dacă nu există, încercăm fallback)
            if (spectatorCamera == null)
            {
                var specComp = FindObjectOfType<SpectatorCamera>(true);
                if (specComp != null) spectatorCamera = specComp.gameObject;
            }

            if (spectatorCamera != null)
            {
                // Dacă spectatorul e child al modelului, detașăm acum (safety)
                if (spectatorCamera.transform.IsChildOf(transform))
                    spectatorCamera.transform.SetParent(null, true);

                // poziționează spectatorul la poziția camerei principale (fallback) pentru a evita "no display"
                if (mainCamera != null)
                {
                    spectatorCamera.transform.position = mainCamera.transform.position;
                    spectatorCamera.transform.rotation = mainCamera.transform.rotation;
                }
                else
                {
                    // fallback: pune spectatorul lângă jucător
                    spectatorCamera.transform.position = transform.position + Vector3.up * 1.6f;
                    spectatorCamera.transform.rotation = Quaternion.identity;
                }

                spectatorCamera.SetActive(true);

                var specCam = spectatorCamera.GetComponent<Camera>();
                if (specCam != null) specCam.enabled = true;

                var audioListener = spectatorCamera.GetComponent<AudioListener>();
                if (audioListener != null) audioListener.enabled = true;

                var specScript = spectatorCamera.GetComponent<SpectatorCamera>();
                if (specScript != null) specScript.enabled = true;
            }

            // 2) Apoi oprește camera jucătorului (astfel Unity nu mai comută la camera din meniu)
            if (mainCamera != null)
            {
                var al = mainCamera.GetComponent<AudioListener>();
                if (al != null) al.enabled = false;

                var camComp = mainCamera.GetComponent<Camera>();
                if (camComp != null) camComp.enabled = false;

                mainCamera.SetActive(false);
            }
        }
    }

    private void EnableLocalScripts(bool enable)
    {
        foreach (var script in scriptsToDisableOnDeath)
        {
            if (script != null)
                script.enabled = enable;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (playerState != null)
            playerState.isAlive.OnValueChanged -= OnAliveStateChanged;
    }
}
