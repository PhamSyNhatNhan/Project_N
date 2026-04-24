/// <summary>
/// Data class cho TriggerInteractiveUiEvent.
/// Thay thế 4 param rời rạc (bool, string, object) cũ.
/// </summary>
public class InteractiveUiData
{
    public bool   show;
    public string eventName;
    public object payload;

    public InteractiveUiData(bool show, string eventName, object payload = null)
    {
        this.show      = show;
        this.eventName = eventName;
        this.payload   = payload;
    }
}