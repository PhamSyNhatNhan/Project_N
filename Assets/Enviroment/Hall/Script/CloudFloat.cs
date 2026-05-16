using UnityEngine;

public class CloudFloat : MonoBehaviour
{
    public float speedX = 2f;        // trôi sang phải (units/s)
    public float amplitudeY = 0.15f; // lắc lư nhẹ lên xuống (units)
    public float frequencyY = 0.8f;
    public float resetPadding = 2f;  // khoảng đệm ngoài màn hình (units)

    Vector3 startPos;
    float travelX;

    void Awake()
    {
        startPos = transform.position;
        travelX = 0f;
    }

    void Update()
    {
        travelX += speedX * Time.deltaTime;
        float y = Mathf.Sin(Time.time * frequencyY) * amplitudeY;
        transform.position = startPos + new Vector3(travelX, y, 0f);

        float rightEdge = GetCameraRightEdge() + resetPadding;
        float leftEdge  = GetCameraLeftEdge()  - resetPadding;

        if (speedX > 0 && transform.position.x > rightEdge)
        {
            // Bay sang phải → reset về bên trái
            travelX = leftEdge - startPos.x;
        }
        else if (speedX < 0 && transform.position.x < leftEdge)
        {
            // Bay sang trái → reset về bên phải
            travelX = rightEdge - startPos.x;
        }
    }

    float GetCameraRightEdge()
    {
        if (Camera.main == null) return 10f;
        return Camera.main.ViewportToWorldPoint(new Vector3(1f, 0f, 0f)).x;
    }

    float GetCameraLeftEdge()
    {
        if (Camera.main == null) return -10f;
        return Camera.main.ViewportToWorldPoint(new Vector3(0f, 0f, 0f)).x;
    }
}