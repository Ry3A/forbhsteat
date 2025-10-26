using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SpeedPickup : MonoBehaviour
{
    [Header("Boost effect")]
    [Tooltip("Во сколько раз увеличивается скорость. 1.3 = +30%.")]
    public float speedMultiplier = 1.3f;

    [Tooltip("Длительность ускорения (секунды).")]
    public float duration = 3f;

    [Header("Respawn")]
    [Tooltip("Через сколько секунд бонус снова появится.")]
    public float respawnTime = 8f;

    [Header("VFX / SFX (опционально)")]
    public ParticleSystem collectVFX; // частицы при подборе
    public AudioSource collectSFX;     // звук при подборе

    Collider col;
    Renderer[] rends;

    void Awake()
    {
        col = GetComponent<Collider>();
        col.isTrigger = true; // обязательно!
        rends = GetComponentsInChildren<Renderer>(true);
    }

    void OnTriggerEnter(Collider other)
    {
        // ищем контроллер машины
        var car = other.GetComponentInParent<PrometeoCarController>();
        if (!car) return;

        // применяем временный буст
        car.PickupBoost(speedMultiplier, duration);

        // эффект подбора
        if (collectVFX) collectVFX.Play();
        if (collectSFX) collectSFX.Play();

        // скрыть и запустить респаун
        StartCoroutine(DisappearAndRespawn());
    }

    IEnumerator DisappearAndRespawn()
    {
        col.enabled = false;
        SetVisible(false);
        yield return new WaitForSeconds(respawnTime);
        SetVisible(true);
        col.enabled = true;
    }

    void SetVisible(bool visible)
    {
        foreach (var r in rends)
            r.enabled = visible;
    }
}
