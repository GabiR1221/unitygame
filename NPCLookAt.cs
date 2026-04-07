using UnityEngine;

public class NPCLookAt : MonoBehaviour
{
    public Transform target;
    public float rotationSpeed = 5f;
    public float detectionRange = 10f;

    private Quaternion initialRotation;

    void Start()
    {
        initialRotation = transform.rotation;
        
        // Dacă e Static, nu se va mișca niciodată!
        if (gameObject.isStatic)
        {
            Debug.LogError($"[NPC ERROR] Obiectul {gameObject.name} este marcat ca STATIC. Debifează 'Static' din colțul dreapta-sus al Inspectorului!");
        }
    }

    void Update()
    {
        // Încercăm să găsim jucătorul constant dacă referința s-a pierdut sau n-a fost găsită la Start
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
            else return; // Ieșim dacă tot nu e găsit
        }

        float distance = Vector3.Distance(transform.position, target.position);

        if (distance <= detectionRange)
        {
            LookAtTarget();
        }
        else
        {
            StopLooking();
        }
    }

    void LookAtTarget()
    {
        Vector3 direction = target.position - transform.position;
        direction.y = 0; 

        if (direction.sqrMagnitude > 0.01f)
        {
            // Debug pentru a vedea în Consolă dacă logica funcționează
            Debug.DrawRay(transform.position, direction.normalized * 2, Color.green);
            
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    void StopLooking()
    {
        transform.rotation = Quaternion.Slerp(transform.rotation, initialRotation, Time.deltaTime * rotationSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
