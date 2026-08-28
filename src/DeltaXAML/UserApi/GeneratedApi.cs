namespace Delta.XAML;

/// <summary>Artifact-wide generated stages attached to one retained document.</summary>
/// <remarks>Implementations are generated companions, never per-element behavior objects.</remarks>
public interface IUiGeneratedDocumentProgram
{
    void RunStage(UiDocument document, UiGeneratedStage stage);
}

public enum UiGeneratedStage
{
    AfterInput,
    AfterBindings,
}

/// <summary>Static typed condition emitted from property, data or multi-condition markup.</summary>
public interface IUiConditionPlan<TPlan, TContext>
    where TPlan : IUiConditionPlan<TPlan, TContext>
{
    static abstract bool Evaluate(in TContext context);
}

/// <summary>Direct generated condition operations over the canonical property store.</summary>
public static class UiGeneratedConditions
{
    public static bool Apply<TValue>(
        bool condition,
        UiElement target,
        UiProperty<TValue> property,
        TValue value,
        ref bool active)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        if (active == condition)
        {
            return false;
        }

        active = condition;
        if (condition)
        {
            target.ApplyTriggerValue(property, value);
        }
        else
        {
            target.ClearTriggerValue(property);
        }

        return true;
    }

    public static bool Apply<TPlan, TContext, TValue>(
        in TContext context,
        UiElement target,
        UiProperty<TValue> property,
        TValue value,
        ref bool active)
        where TPlan : IUiConditionPlan<TPlan, TContext>
    {
        var next = TPlan.Evaluate(in context);
        return Apply(next, target, property, value, ref active);
    }
}

/// <summary>Semantic action sink used only by generated condition and behavior plans.</summary>
public static class UiGeneratedActions
{
    public static void Publish(UiDocument document, UiElement source, UiSemanticActionKind action, string? argument = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        document.PublishGenerated(source, action, argument);
    }
}

/// <summary>Static behavior capability with optional inline generated state.</summary>
public interface IUiBehaviorPlan<TPlan, TState>
    where TPlan : IUiBehaviorPlan<TPlan, TState>
    where TState : struct
{
    static abstract void Attach(UiElement element, ref TState state);

    static abstract void Detach(UiElement element, ref TState state);

    static abstract void Update(UiDocument document, UiElement element, ref TState state);
}

/// <summary>Direct calls emitted by generated companions; no behavior object or event subscription is retained.</summary>
public static class UiGeneratedBehaviors
{
    public static void Attach<TPlan, TState>(UiElement element, ref TState state)
        where TPlan : IUiBehaviorPlan<TPlan, TState>
        where TState : struct
    {
        ArgumentNullException.ThrowIfNull(element);
        TPlan.Attach(element, ref state);
    }

    public static void Detach<TPlan, TState>(UiElement element, ref TState state)
        where TPlan : IUiBehaviorPlan<TPlan, TState>
        where TState : struct
    {
        ArgumentNullException.ThrowIfNull(element);
        TPlan.Detach(element, ref state);
    }

    public static void Update<TPlan, TState>(UiDocument document, UiElement element, ref TState state)
        where TPlan : IUiBehaviorPlan<TPlan, TState>
        where TState : struct
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(element);
        TPlan.Update(document, element, ref state);
    }
}
