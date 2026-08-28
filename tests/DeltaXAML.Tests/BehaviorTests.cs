using Delta.Maths;
using Delta.XAML;

internal static class BehaviorTests
{
    internal static void Run()
    {
        StaticBehaviorKeepsInlineStateWithoutSubscriptions();
    }

    private static void StaticBehaviorKeepsInlineStateWithoutSubscriptions()
    {
        var target = new UiPanel();
        var program = new BehaviorProgram(target);
        using var text = new EmptyTextService();
        using var document = new UiDocument(target, text, null, null, null, program);
        document.Layout(new float2(20, 20), 1);
        document.Layout(new float2(20, 20), 1);
        Assert.Equal(1, program.AttachCount, "generated behavior attaches once through its static plan");
        Assert.Equal(2, program.UpdateCount, "generated behavior updates from the fixed document stage");
        program.Detach();
        Assert.Equal(1, program.DetachCount, "generated behavior detaches without an event subscription");
    }

    private struct BehaviorState
    {
        internal bool Attached;
        internal int AttachCount;
        internal int UpdateCount;
        internal int DetachCount;
    }

    private readonly struct CounterBehavior : IUiBehaviorPlan<CounterBehavior, BehaviorState>
    {
        public static void Attach(UiElement element, ref BehaviorState state)
        {
            state.Attached = true;
            state.AttachCount++;
        }

        public static void Detach(UiElement element, ref BehaviorState state)
        {
            state.Attached = false;
            state.DetachCount++;
        }

        public static void Update(UiDocument document, UiElement element, ref BehaviorState state) => state.UpdateCount++;
    }

    private sealed class BehaviorProgram : IUiGeneratedDocumentProgram
    {
        private readonly UiElement _target;
        private BehaviorState _state;

        internal BehaviorProgram(UiElement target)
        {
            _target = target;
            UiGeneratedBehaviors.Attach<CounterBehavior, BehaviorState>(_target, ref _state);
        }

        internal int AttachCount => _state.AttachCount;
        internal int UpdateCount => _state.UpdateCount;
        internal int DetachCount => _state.DetachCount;

        internal void Detach() => UiGeneratedBehaviors.Detach<CounterBehavior, BehaviorState>(_target, ref _state);

        public void RunStage(UiDocument document, UiGeneratedStage stage)
        {
            if (stage == UiGeneratedStage.AfterInput && _state.Attached)
            {
                UiGeneratedBehaviors.Update<CounterBehavior, BehaviorState>(document, _target, ref _state);
            }
        }
    }
}
