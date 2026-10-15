using System;
using UnityEngine.InputSystem;

// EN: Owns five skill actions using the project's bindings, without inherited action-level interactions.
// VI: Sở hữu năm input skill dùng binding của project, không mang interaction cấp action cũ.
public class PNPlayerSkillControls : IDisposable
{
    public class ControllerActions
    {
        public InputAction Attack { get; internal set; }
        public InputAction Skill { get; internal set; }
        public InputAction Ulti { get; internal set; }
        public InputAction Dash { get; internal set; }
        public InputAction Burst { get; internal set; }
    }

    public ControllerActions Controller { get; } = new ControllerActions();

    public PNPlayerSkillControls()
    {
        var template = new PlayerControls();
        try
        {
            Controller.Attack = CopyBindings("Attack", template.Controller.Attack);
            Controller.Skill = CopyBindings("Skill", template.Controller.Skill);
            Controller.Ulti = CopyBindings("Ulti", template.Controller.Ulti);
            Controller.Dash = CopyBindings("Dash", template.Controller.Dash);
            Controller.Burst = CopyBindings("Burst", template.Controller.Burst);
        }
        finally
        {
#if UNITY_EDITOR
            if(!UnityEngine.Application.isPlaying)UnityEngine.Object.DestroyImmediate(template.asset);
            else
#endif
                template.Dispose();
        }
    }

    private static InputAction CopyBindings(string name, InputAction source)
    {
        var action = new InputAction(name, InputActionType.Button);
        foreach (var sourceBinding in source.bindings)
        {
            var binding = sourceBinding;
            binding.action = null;
            binding.interactions = "";
            binding.overrideInteractions = "";
            action.AddBinding(binding);
        }
        return action;
    }

    public void Dispose()
    {
        Controller.Attack.Dispose(); Controller.Skill.Dispose(); Controller.Ulti.Dispose();
        Controller.Dash.Dispose(); Controller.Burst.Dispose();
    }
}
