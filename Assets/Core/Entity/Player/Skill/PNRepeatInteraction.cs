using UnityEngine.InputSystem;

// EN: Repeats while actuated; zero selects defaultHoldTime and a 0.1 second interval.
// VI: Lặp khi còn giữ nút; 0 chọn defaultHoldTime và khoảng lặp 0.1 giây.
public class PNRepeatInteraction : IInputInteraction
{
    public float delay;
    public float interval;
    private bool holding;

    public void Process(ref InputInteractionContext context)
    {
        // Timeout contexts have no cached magnitude. Read the actual control on timer callbacks.
        bool actuated = context.timerHasExpired ? context.control.IsActuated() : context.ControlIsActuated();
        if (!actuated)
        {
            if (holding) context.Canceled();
            holding = false;
            return;
        }
        if (!holding)
        {
            holding = true;
            context.Started();
            context.SetTimeout(delay > 0f ? delay : InputSystem.settings.defaultHoldTime);
        }
        else if (context.timerHasExpired)
        {
            context.PerformedAndStayPerformed();
            context.SetTimeout(interval > 0f ? interval : 0.1f);
        }
    }

    public void Reset() => holding = false;
    public static void Register()
    {
        if (InputSystem.TryGetInteraction("PNRepeat") == null)
            InputSystem.RegisterInteraction<PNRepeatInteraction>("PNRepeat");
    }
}
