using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RepeatInteraction : IInputInteraction
{
    [Tooltip("Thời gian chờ trước lần performed đầu tiên")]
    public float delay = 0f;    

    [Tooltip("Khoảng cách giữa mỗi lần performed")]
    public float interval = 0f; 

    // Fallback về project settings nếu để 0
    private float DelayTime   => delay   > 0 ? delay   : InputSystem.settings.defaultHoldTime;
    private float IntervalTime => interval > 0 ? interval : 0.1f;

    private enum Phase { Waiting, Holding }
    private Phase _phase;

    public void Process(ref InputInteractionContext context)
    {
        if (context.timerHasExpired)
        {
            context.PerformedAndStayPerformed();
            context.SetTimeout(IntervalTime);
            return;
        }

        switch (_phase)
        {
            case Phase.Waiting:
                if (context.ControlIsActuated())
                {
                    _phase = Phase.Holding;
                    context.Started();
                    context.SetTimeout(DelayTime); // Bắt đầu đếm delay
                }
                break;

            case Phase.Holding:
                if (!context.ControlIsActuated())
                {
                    // Thả trước khi delay xong → để Tap/SlowTap xử lý
                    _phase = Phase.Waiting;
                    context.Canceled();
                }
                break;
        }
    }

    public void Reset()
    {
        _phase = Phase.Waiting;
    }


    // Runtime (build)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterRuntime() => Register();

    // Editor
#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void RegisterEditor() => Register();
#endif

    static void Register()
    {
        if (InputSystem.TryGetInteraction("Repeat") == null)
            InputSystem.RegisterInteraction<RepeatInteraction>("Repeat");
    }
}
