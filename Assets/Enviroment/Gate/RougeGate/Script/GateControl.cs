using System.Collections;
using UnityEngine;

public class GateControl : InteractiveObject
{
    [SerializeField] private GameObject prefabGateDoorLeft;
    [SerializeField] private GameObject prefabGateDoorRight;
    [SerializeField] private float      openSpeed = 1f;

    [SerializeField] private Quaternion leftClosedRot, rightClosedRot;
    [SerializeField] private Quaternion leftOpenRot,   rightOpenRot;

    private bool isOpen = false;

    protected override void OnEnable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .AddListener((component, data) => ChangeGateStatus(data));
    }

    protected override void OnDisable()
    {
        EventManager.Enviroment.TriggerInteractiveEvent.Get(nameObject)
            .RemoveListener((component, data) => ChangeGateStatus(data));
    }

    protected override void Start() { }

    protected override void Update() { }

    private void ChangeGateStatus(object data)
    {
        if (isOpen) return;
        isOpen = true;
        StopAllCoroutines();
        float duration = 1f / openSpeed;
        StartCoroutine(RotateGate(true, duration));
        StartCoroutine(EndGateChange(duration));
    }

    private IEnumerator RotateGate(bool open, float duration)
    {
        Quaternion targetLeft  = open ? leftOpenRot  : leftClosedRot;
        Quaternion targetRight = open ? rightOpenRot : rightClosedRot;

        float      elapsedTime = 0f;
        Quaternion startLeft   = prefabGateDoorLeft.transform.localRotation;
        Quaternion startRight  = prefabGateDoorRight.transform.localRotation;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / duration);

            prefabGateDoorLeft.transform.localRotation  = Quaternion.Slerp(startLeft,  targetLeft,  t);
            prefabGateDoorRight.transform.localRotation = Quaternion.Slerp(startRight, targetRight, t);

            yield return null;
        }

        prefabGateDoorLeft.transform.localRotation  = targetLeft;
        prefabGateDoorRight.transform.localRotation = targetRight;
    }

    private IEnumerator EndGateChange(float time)
    {
        yield return new WaitForSeconds(time);
        //Debug.Log("[GateControl] Fire OnOpenCharacterSelect");
        EventManager.Gm.OnOpenCharacterSelect.Get().Invoke(this, null);
        isOpen = false;
    }

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(true, nameObject));
    }

    protected override void OnTriggerExit2D(Collider2D other)
    {
        EventManager.Ui.TriggerInteractiveUiEvent.Get()
            .Invoke(this, new InteractiveUiData(false, nameObject));
    }
}