using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SpeedPickup : MonoBehaviour
{
    [Header("Boost effect")]
    [Tooltip("�� ������� ��� ������������� ��������. 1.3 = +30%.")]
    public float speedMultiplier = 1.3f;

    [Tooltip("������������ ��������� (�������).")]
    public float duration = 3f;

    [Header("Respawn")]
    [Tooltip("����� ������� ������ ����� ����� ��������.")]
    public float respawnTime = 8f;

    [Header("VFX / SFX (�����������)")]
    public ParticleSystem collectVFX; // ������� ��� �������
    public AudioSource collectSFX;     // ���� ��� �������

    Collider col;
    Renderer[] rends;

    void Awake()
    {
        col = GetComponent<Collider>();
        col.isTrigger = true; // �����������!
        rends = GetComponentsInChildren<Renderer>(true);
    }

    void OnTriggerEnter(Collider other)
    {
        // ���� ���������� ������
        var car = other.GetComponentInParent<PrometeoCarController>();
        if (!car) return;

        // ��������� ��������� ����
        car.PickupBoost(speedMultiplier, duration);

        // ������ �������
        if (collectVFX) collectVFX.Play();
        if (collectSFX) collectSFX.Play();

        // ������ � ��������� �������
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
