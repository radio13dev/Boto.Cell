using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

#if UNITY_EDITOR
using UnityEditor;
[InitializeOnLoad]
#endif
[DisplayStringFormat("{firstTouch}+{secondTouch}")]
public class PanningComposite : InputBindingComposite<Vector2>
{
    [InputControl(layout = "Value")]
    public int firstTouch;
    [InputControl(layout = "Value")]
    public int secondTouch;

    private struct PanningStateComparer : IComparer<TouchState>
    {
        public int Compare(TouchState x, TouchState y) => 1;
    }

    // This method computes the resulting input value of the composite based
    // on the input from its part bindings.
    public override Vector2 ReadValue(ref InputBindingCompositeContext context)
    {
        var touch_0 = context.ReadValue<TouchState, PanningStateComparer>(firstTouch);
        var touch_1 = context.ReadValue<TouchState, PanningStateComparer>(secondTouch);

        if (touch_0.phase != TouchPhase.Moved || touch_1.phase != TouchPhase.Moved)
            return Vector2.zero;
        
        return touch_0.delta + touch_1.delta;
    }

    // This method computes the current actuation of the binding as a whole.
    public override float EvaluateMagnitude(ref InputBindingCompositeContext context) => 1f;

    static PanningComposite()
    {
        InputSystem.RegisterBindingComposite<PanningComposite>();
        Debug.Log($"Registered PanningComposite");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init() { } // Trigger static constructor.
}