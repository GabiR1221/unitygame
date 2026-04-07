using UnityEngine;
using TMPro;
using UnityEngine.UI;

// Structurile care definesc cum arată dialogul
[System.Serializable]
public class DialogChoice
{
    public string playerText;     // Ce zice jucătorul
    public int nextNodeIndex;     // La ce index din listă te duce (-1 pentru a închide dialogul)
}

[System.Serializable]
public class DialogNode
{
    [TextArea(3, 5)]
    public string npcText;        // Ce zice NPC-ul
    public DialogChoice[] choices; // Variantele de răspuns ale jucătorului
}

public class DialogManager : MonoBehaviour
{
    public static DialogManager instance;

    [Header("UI Elemente")]
    public GameObject dialogPanel;
    public TMP_Text npcTextUI;
    public Button[] choiceButtons;    // Trage aici cele 3 butoane din UI
    public TMP_Text[] choiceTextsUI;  // Trage aici textele de pe cele 3 butoane

    private DialogNode[] currentDialog;
    private bool isDialogActive = false;

    void Awake()
    {
        if (instance == null) instance = this;
        dialogPanel.SetActive(false);
    }

    public void StartDialog(DialogNode[] dialogNodes)
    {
        if (isDialogActive) return;

        currentDialog = dialogNodes;
        isDialogActive = true;
        dialogPanel.SetActive(true);

        // Deblocăm cursorul pentru a putea da click pe opțiuni
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Începem mereu cu primul nod (Index 0)
        ShowNode(0);
    }

    public void ShowNode(int nodeIndex)
    {
        // Dacă indexul este -1, înseamnă că dialogul s-a terminat
        if (nodeIndex < 0)
        {
            EndDialog();
            return;
        }

        DialogNode node = currentDialog[nodeIndex];
        npcTextUI.text = node.npcText;

        // Ascundem toate butoanele mai întâi
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            choiceButtons[i].gameObject.SetActive(false);
            choiceButtons[i].onClick.RemoveAllListeners(); // Curățăm acțiunile vechi
        }

        // Activăm doar butoanele pentru câte opțiuni avem în acest nod
        for (int i = 0; i < node.choices.Length; i++)
        {
            if (i >= choiceButtons.Length) break; // Să nu depășim numărul de butoane din UI

            choiceButtons[i].gameObject.SetActive(true);
            choiceTextsUI[i].text = node.choices[i].playerText;

            // Salvăm indexul următor pentru a-l folosi când jucătorul dă click
            int nextIndex = node.choices[i].nextNodeIndex;
            choiceButtons[i].onClick.AddListener(() => ShowNode(nextIndex));
        }
    }

    public void EndDialog()
    {
        isDialogActive = false;
        dialogPanel.SetActive(false);

        // Blocăm cursorul la loc (dacă ești într-un joc First/Third Person)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public bool IsDialogActive()
    {
        return isDialogActive;
    }
}
