using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    public GameObject pauseMenuUI;              // Panelul care apare când e pauză
    public GameObject[] hudObjects;             // HUD elements to hide while paused

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.Escape;  // tasta de toggle (Esc)
    public bool freezeTime = false;             // dacă vrem Time.timeScale = 0 (atenție multiplayer)

    [Header("Scene rules")]
    public string menuSceneName = "MainMenu";   // numele scenei în care PAUSE nu e permis

    // intern
    bool isPaused = false;
    List<Behaviour> disabledDuringPause = new List<Behaviour>();
    Camera localPlayerCamera = null;

    void Start()
    {
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);

        // Dacă suntem în scena de menu, dezactivăm scriptul (sau îl lăsăm activ, dar oprește toggle-ul)
        // aici doar lăsăm scriptul activ, dar TogglePause va ignora în menuSceneName
    }

    void Update()
    {
        // Nu permite deschiderea pause menu din scena principală / menu
        if (SceneManager.GetActiveScene().name == menuSceneName) return;

        if (Input.GetKeyDown(toggleKey))
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        if (isPaused) Resume();
        else Pause();
    }

    void Pause()
    {
        if (isPaused) return;
        isPaused = true;

        // Show menu + hide HUD
        if (pauseMenuUI != null) pauseMenuUI.SetActive(true);
        SetHUDActive(false);

        // Cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Optionally freeze time (local only!)
        if (freezeTime) Time.timeScale = 0f;

        // Disable only MouseLook (nu mai dezactivăm PlayerMovement)
        DisableOnlyMouseLook();
    }

    void Resume()
    {
        if (!isPaused) return;
        isPaused = false;

        // Hide menu + show HUD
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
        SetHUDActive(true);

        // Cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Unfreeze time
        if (freezeTime) Time.timeScale = 1f;

        // Re-enable previously disabled scripts (în mod curent doar MouseLook)
        RestoreLocalInput();
    }

    void SetHUDActive(bool active)
    {
        if (hudObjects == null) return;
        foreach (var go in hudObjects)
        {
            if (go == null) continue;
            go.SetActive(active);
        }
    }

    // --- Modificare esențială: dezactivează doar componente de tip MouseLook ---
    void DisableOnlyMouseLook()
    {
        disabledDuringPause.Clear();

        // Găsim jucătorul local (dacă există Netcode)
        GameObject localPlayer = null;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject?.gameObject;
        }

        // Fallback — dacă nu e Netcode rulează local find (opțional)
        if (localPlayer == null)
        {
            var all = GameObject.FindGameObjectsWithTag("Player");
            foreach (var a in all)
            {
                var nb = a.GetComponent<NetworkObject>();
                if (nb == null || nb.IsOwner) { localPlayer = a; break; }
            }
        }

        if (localPlayer == null)
        {
            Debug.Log("[PauseMenu] Nu am găsit player local pentru a dezactiva MouseLook.");
            return;
        }

        // memorăm camera locală (folosită de UI/restore)
        localPlayerCamera = localPlayer.GetComponentInChildren<Camera>(true);

        // 1) MouseLook atașat pe jucător sau pe copii
        var ml = localPlayer.GetComponentInChildren<MouseLook>(true);
        if (ml != null && ml.enabled)
        {
            ml.enabled = false;
            disabledDuringPause.Add(ml);
        }

        // 2) MouseLook posibil atașat direct pe camera
        if (localPlayerCamera != null)
        {
            var mlCam = localPlayerCamera.GetComponent<MouseLook>();
            if (mlCam != null && mlCam.enabled && !disabledDuringPause.Contains(mlCam))
            {
                mlCam.enabled = false;
                disabledDuringPause.Add(mlCam);
            }
        }
    }

    void RestoreLocalInput()
    {
        // Reactivăm exact ce am dezactivat — în mod curent doar MouseLook-uri
        GameObject localPlayer = null;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject?.gameObject;
        }

        PlayerState pState = null;
        if (localPlayer != null) pState = localPlayer.GetComponent<PlayerState>();

        foreach (var b in disabledDuringPause)
        {
            if (b == null) continue;

            // Reactivăm MouseLook doar dacă jucătorul nu e mort (sau oricum dacă e safe)
            if (b is MouseLook ml)
            {
                // dacă jucătorul e dead, probabil nu vrei să reapari mouse look-ul local — dar aici îl reactivăm oricum
                ml.enabled = true;
                continue;
            }

            // fallback
            try { b.enabled = true; } catch { }
        }

        disabledDuringPause.Clear();
    }

    // pentru buton Resume din UI
    public void OnResumeButton()
    {
        if (isPaused) TogglePause();
    }

    // buton optional: leave to main menu (închide netcode, încarcă scena)
    public void OnLeaveToMenu(string sceneName)
    {
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
        {
            NetworkManager.Singleton.Shutdown();
        }
        if (freezeTime) Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

    // util: expune starea
    public bool IsPaused() => isPaused;
}
