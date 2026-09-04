using Delta;
using Delta.XAML;

internal static class ConditionTests
{
    internal static void Run()
    {
        GeneratedConditionUsesCanonicalPrecedenceAndSemanticQueue();
    }

    private static void GeneratedConditionUsesCanonicalPrecedenceAndSemanticQueue()
    {
        var target = new UiTextBlock { Text = "condition", Foreground = new UiColor(1, 2, 3) };
        var context = new ConditionContext();
        var program = new ConditionProgram(context, target);
        using var text = new EmptyTextService();
        using var document = new UiDocument(target, text, null, null, null, program);
        document.Layout(new float2(100, 20), 1);
        Assert.Equal(new UiColor(1, 2, 3), target.Foreground, "inactive condition preserves the local source");

        target.RetainedElement.Clear("Foreground", DeltaXAML.Internal.UiValueSource.Local);
        context.Armed = true;
        document.Layout(new float2(100, 20), 1);
        Assert.Equal(new UiColor(9, 8, 7), target.Foreground, "active generated condition writes the trigger slot");
        Assert.Equal(1, document.SemanticCommands.Length, "condition transition publishes one deferred semantic action");

        document.Layout(new float2(100, 20), 1);
        Assert.Equal(0, document.SemanticCommands.Length, "unchanged condition does not republish an action");
        context.Armed = false;
        document.Layout(new float2(100, 20), 1);
        Assert.Equal(new UiColor(255, 255, 255), target.Foreground, "clearing the condition reveals the default source");
    }

    private sealed class ConditionContext
    {
        internal bool Armed;
    }

    private readonly struct ArmedPlan : IUiConditionPlan<ArmedPlan, ConditionContext>
    {
        public static bool Evaluate(in ConditionContext context) => context.Armed;
    }

    private sealed class ConditionProgram(ConditionContext context, UiTextBlock target) : IUiGeneratedDocumentProgram
    {
        private bool _active;

        public void RunStage(UiDocument document, UiGeneratedStage stage)
        {
            if (stage != UiGeneratedStage.AfterBindings)
            {
                return;
            }

            if (UiGeneratedConditions.Apply<ArmedPlan, ConditionContext, UiColor>(
                in context,
                target,
                TextBlockProperties.Foreground,
                new UiColor(9, 8, 7),
                ref _active) && _active)
            {
                UiGeneratedActions.Publish(document, target, UiSemanticActionKind.Command, "armed");
            }
        }
    }
}
