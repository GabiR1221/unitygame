using UnityEngine;
using UnityEngine.EventSystems; // Avem nevoie de asta pentru mouse

public class ExitZoneHelper : MonoBehaviour, IPointerEnterHandler
{
    public DoorSystem doorSystem; // Tragem ușa aici

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Când mouse-ul intră în această zonă
        if (doorSystem != null && doorSystem.isAtDoor)
        {
            doorSystem.ExitDoor();
        }
    }
}
