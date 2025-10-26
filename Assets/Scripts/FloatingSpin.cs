using UnityEngine;

public class FloatingSpin : MonoBehaviour
{
    [Header("Rotation")]
    public float spinSpeed = 60f;

    [Header("Floating")]
    public float bobHeight = 0.25f;
    public float bobSpeed = 2f;

    Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        float offset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = startPos + new Vector3(0, offset, 0);
    }
}
