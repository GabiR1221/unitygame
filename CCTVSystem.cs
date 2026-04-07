using UnityEngine;

public class CCTVSystem : MonoBehaviour
{
    public Camera[] cameras; // O listă cu toate camerele noastre
    private int currentCameraIndex = 0;

    void Start()
    {
        // La început, oprim toate camerele, în afară de prima
        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i].enabled = (i == 0);
        }
    }

    // Această funcție va fi chemată când apeși pe un buton
    public void NextCamera()
    {
        cameras[currentCameraIndex].enabled = false; // Oprim camera actuală
        currentCameraIndex++; // Trecem la următoarea

        if (currentCameraIndex >= cameras.Length)
        {
            currentCameraIndex = 0; // Resetăm la prima dacă am ajuns la final
        }

        cameras[currentCameraIndex].enabled = true; // Pornim noua cameră
    }
}
