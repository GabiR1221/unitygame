using UnityEngine;
using TMPro;
using System.Collections;

public class ObjectiveUI : MonoBehaviour
{
    [Header("Setări mișcare")]
    public float animationDuration = 0.5f;      // cât durează slide-ul
    public float displayDuration = 3f;           // cât stă vizibil după ce apare
    public Vector2 hiddenPosition;                // poziția în afara ecranului (ex: dreapta)
    public Vector2 visiblePosition;               // poziția când este vizibil (ex: 0,0)

    private RectTransform rectTransform;
    private TextMeshProUGUI textComponent;
    private Coroutine currentAnimation;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        textComponent = GetComponentInChildren<TextMeshProUGUI>();

        // Setează poziția inițială ca fiind ascunsă
        rectTransform.anchoredPosition = hiddenPosition;
    }

    void Update()
    {
        // La apăsarea tastei Tab
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            // Afișează textul curent (fără să îl schimbi)
            ShowCurrentObjective();
        }
    }

    // Metodă apelată din GameManager când se primește un obiectiv nou
    public void ShowObjective(string newText)
    {
        if (textComponent != null)
            textComponent.text = newText;

        // Pornește animația (resetează timerul)
        TriggerAnimation();
    }

    // Afișează textul curent (fără a-l modifica)
    private void ShowCurrentObjective()
    {
        TriggerAnimation();
    }

    private void TriggerAnimation()
    {
        if (currentAnimation != null)
            StopCoroutine(currentAnimation);

        currentAnimation = StartCoroutine(AnimateObjective());
    }

    IEnumerator AnimateObjective()
    {
        // 1. Slide-in (de la hidden la visible)
        float elapsed = 0f;
        Vector2 startPos = rectTransform.anchoredPosition;

        while (elapsed < animationDuration)
        {
            float t = elapsed / animationDuration;
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, visiblePosition, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rectTransform.anchoredPosition = visiblePosition;

        // 2. Așteaptă displayDuration secunde
        yield return new WaitForSeconds(displayDuration);

        // 3. Slide-out (înapoi la hidden)
        elapsed = 0f;
        startPos = rectTransform.anchoredPosition;

        while (elapsed < animationDuration)
        {
            float t = elapsed / animationDuration;
            rectTransform.anchoredPosition = Vector2.Lerp(startPos, hiddenPosition, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        rectTransform.anchoredPosition = hiddenPosition;

        currentAnimation = null;
    }
}
