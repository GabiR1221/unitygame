using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;
using System.Threading.Tasks;

public class NetworkUI : MonoBehaviour
{
    [Header("Referinte Panouri")]
    public GameObject mainMenuPanel;
    public GameObject lobbyPanel;
    public GameObject inGamePanel;

    [Header("Panouri Meniu Principal (sub-panouri)")]
    public GameObject[] mainMenuSubPanels;
    public int defaultSubPanelIndex = 0;

    [Header("Elemente Meniu Principal")]
    public Button createRoomButton;
    public TMP_InputField joinCodeInput;
    public Button joinRoomButton;

    [Header("Elemente Lobby")]
    public TextMeshProUGUI roomCodeText;
    public TextMeshProUGUI playersCountText;
    public GameObject hostOnlyControls;
    public Button[] nightButtons;
    public Button startGameButton;

    [Header("Elemente InGame")]
    public Button leaveButton;

    [Header("Audio Setari")]
    public AudioSource menuMusicSource;

    private string currentRoomCode = "";

    void Start()
    {
        createRoomButton.onClick.AddListener(() => _ = CreateRoom());
        joinRoomButton.onClick.AddListener(JoinRoom);
        leaveButton.onClick.AddListener(LeaveGame);

        if (nightButtons.Length >= 4)
        {
            nightButtons[0].onClick.AddListener(() => GameManager.instance.SetNightServerRpc(1));
            nightButtons[1].onClick.AddListener(() => GameManager.instance.SetNightServerRpc(2));
            nightButtons[2].onClick.AddListener(() => GameManager.instance.SetNightServerRpc(3));
            nightButtons[3].onClick.AddListener(() => GameManager.instance.SetNightServerRpc(4));
        }

        startGameButton.onClick.AddListener(() => GameManager.instance.StartGameServerRpc());

        // Seteaza panoul implicit O SINGURA DATA la pornire
        ShowMainMenuSubPanel(defaultSubPanelIndex); 
        UpdateUI();
    }

    void Update()
    {
        HandleMenuMusic();
        UpdateUI();
    }

    void HandleMenuMusic()
    {
        if (menuMusicSource == null || GameManager.instance == null) return;

        bool shouldPlay = !GameManager.instance.isGameStarted.Value;
        if (shouldPlay && !menuMusicSource.isPlaying)
            menuMusicSource.Play();
        else if (!shouldPlay && menuMusicSource.isPlaying)
            menuMusicSource.Stop();
    }

    void UpdateUI()
    {
        bool isConnected = NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer;
        bool isGameStarted = GameManager.instance != null && GameManager.instance.isGameStarted.Value;

        if (!isConnected)
        {
            mainMenuPanel.SetActive(true);
            lobbyPanel.SetActive(false);
            inGamePanel.SetActive(false);

        }
        else if (!isGameStarted)
        {
            mainMenuPanel.SetActive(false);
            lobbyPanel.SetActive(true);
            inGamePanel.SetActive(false);

            if (!string.IsNullOrEmpty(currentRoomCode))
                roomCodeText.text = "Cod Cameră: " + currentRoomCode;
            else
                roomCodeText.text = "Cod Cameră: (se generează...)";

            if (NetworkManager.Singleton != null)
                playersCountText.text = "Jucători conectați: " + NetworkManager.Singleton.ConnectedClients.Count;

            if (hostOnlyControls != null)
                hostOnlyControls.SetActive(NetworkManager.Singleton.IsHost);
        }
        else
        {
            mainMenuPanel.SetActive(false);
            lobbyPanel.SetActive(false);
            inGamePanel.SetActive(true);
        }

        if (!isConnected || (GameManager.instance != null && !GameManager.instance.isGameStarted.Value))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // Metodă publică pentru butoane
    public void ShowMainMenuSubPanel(int index)
    {
        Debug.Log($"ShowMainMenuSubPanel called with index {index}");
        Debug.Log($"mainMenuSubPanels.Length = {mainMenuSubPanels.Length}");
        if (index < 0 || index >= mainMenuSubPanels.Length)
        {
            Debug.LogError($"Index {index} out of range!");
            return;
        }

        foreach (GameObject panel in mainMenuSubPanels)
        {
            if (panel != null) panel.SetActive(false);
            else Debug.LogWarning("Un panel din array este null!");
        }

        if (mainMenuSubPanels[index] != null)
            mainMenuSubPanels[index].SetActive(true);
        else
            Debug.LogError($"Panelul la index {index} este null!");
    }

    async Task CreateRoom()
    {
        string code = await RelayManager.Instance.CreateRelay();
        if (code != null)
            currentRoomCode = code;
    }

    void JoinRoom()
    {
        string code = joinCodeInput.text;
        if (!string.IsNullOrEmpty(code))
        {
            RelayManager.Instance.JoinRelay(code);
            currentRoomCode = code;
        }
    }

    void LeaveGame()
    {
        currentRoomCode = "";
        GameManager.instance.LeaveAndReset();
    }
}
