#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// TitleStarsDemo floating buttons. Uses the Title's existing sparkle artwork lease.
internal static class PNTitleButtonFeedback
{
    // Keep keyboard/gamepad navigation in the Title's three existing choices. Tab
    // also works when the Input System module doesn't forward it as a UITK key event.
    internal sealed class Navigation
    {
        private readonly VisualElement scope;
        private readonly Button[] buttons;
        private int lastFrame=-1;
        internal Navigation(VisualElement scope,Button[] buttons)
        {
            this.scope=scope;this.buttons=buttons;
            scope.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode!=KeyCode.Tab)return;scope.focusController?.IgnoreEvent(e);Move(e.shiftKey?-1:1);e.StopImmediatePropagation();},TrickleDown.TrickleDown);
            scope.RegisterCallback<NavigationMoveEvent>(e=>{if(e.direction!=NavigationMoveEvent.Direction.Down&&e.direction!=NavigationMoveEvent.Direction.Up)return;scope.focusController?.IgnoreEvent(e);Move(e.direction==NavigationMoveEvent.Direction.Down?1:-1);e.StopImmediatePropagation();},TrickleDown.TrickleDown);
        }
        internal void Tick()
        {
            var keyboard=Keyboard.current;
            if(keyboard!=null&&keyboard.tabKey.wasPressedThisFrame)Move(keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed?-1:1);
        }
        private void Move(int delta)
        {
            if(lastFrame==Time.frameCount||!scope.enabledInHierarchy||scope.resolvedStyle.display==DisplayStyle.None)return;
            lastFrame=Time.frameCount;int index=System.Array.IndexOf(buttons,scope.focusController?.focusedElement as Button);
            index=index<0?(delta>0?0:buttons.Length-1):(index+delta+buttons.Length)%buttons.Length;
            var next=buttons[index];next.AddToClassList("pn-title-keyboard-focus");next.Focus();
        }
    }
    private sealed class Underline : VisualElement
    {
        internal Underline()
        {
            pickingMode=PickingMode.Ignore;usageHints=UsageHints.DynamicTransform;
            AddToClassList("pn-title-underline");generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var painter=context.painter2D;Vector2 center=contentRect.center;
            // Feathered ends and soft light, instead of the previous solid rectangular rule.
            for(int pass=0;pass<4;pass++)
            {
                painter.lineWidth=pass==3?1:9-pass*3;
                for(int i=0;i<24;i++)
                {
                    float alpha=Mathf.Sin(Mathf.PI*(i+.5f)/24)*(pass==3?.95f:.055f);
                    painter.strokeColor=new Color(1,1,1,alpha);painter.BeginPath();
                    painter.MoveTo(center+new Vector2(-18+i*1.5f,0));painter.LineTo(center+new Vector2(-18+(i+1)*1.5f,0));painter.Stroke();
                }
            }
        }
    }
    internal static void Attach(Button button)
    {
        button.usageHints=UsageHints.DynamicTransform;
        var star=new PNSparkleGlyph(false,false,new Color(1,245f/255,223f/255));star.SetGlow(new Color(1,244f/255,218f/255,.7f));
        star.AddToClassList("pn-title-hover-star");star.usageHints=UsageHints.DynamicTransform;
        button.Add(star);button.Add(new Underline());
        // Pointer focus and the initial programmatic Focus are not CSS :focus-visible.
        button.RegisterCallback<FocusInEvent>(e=>button.EnableInClassList("pn-title-keyboard-focus",button.ClassListContains("pn-title-keyboard-focus")||e.direction!=FocusChangeDirection.unspecified));
        button.RegisterCallback<FocusOutEvent>(_=>button.RemoveFromClassList("pn-title-keyboard-focus"));
        button.RegisterCallback<PointerDownEvent>(_=>button.RemoveFromClassList("pn-title-keyboard-focus"));
        button.RegisterCallback<NavigationMoveEvent>(_=>button.AddToClassList("pn-title-keyboard-focus"));
        button.RegisterCallback<NavigationSubmitEvent>(_=>button.AddToClassList("pn-title-keyboard-focus"));
        button.RegisterCallback<DetachFromPanelEvent>(_=>button.RemoveFromClassList("pn-title-keyboard-focus"));
    }
}
#endif
