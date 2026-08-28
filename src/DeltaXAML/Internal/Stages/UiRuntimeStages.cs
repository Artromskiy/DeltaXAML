namespace DeltaXAML.Internal;

internal static class UiInputStage
{
    internal static void Run(UiInputRouter router, List<UiInputPacket> queue)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(queue);
        for (var i = 0; i < queue.Count; i++)
        {
            router.Dispatch(queue[i]);
        }

        queue.Clear();
    }
}

internal static class UiMutationStage
{
    internal static void Run(
        UiNodeStore nodes,
        UiElement root,
        List<UiMutation> queue,
        out int applied,
        out int rejected)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);
        nodes.EnsureCurrent(root);
        applied = 0;
        rejected = 0;
        for (var i = 0; i < queue.Count; i++)
        {
            var mutation = queue[i];
            if (nodes.TryResolve(mutation.Target, out var element) &&
                element.TrySet(mutation.Target, mutation.Value, mutation.Invalidation, out _))
            {
                applied++;
            }
            else
            {
                rejected++;
            }
        }

        queue.Clear();
    }
}

internal static class UiBindingStage
{
    internal static void Run(UiElement root, List<UiElement> traversal)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(traversal);
        traversal.Clear();
        traversal.Add(root);
        while (traversal.Count != 0)
        {
            var last = traversal.Count - 1;
            var element = traversal[last];
            traversal.RemoveAt(last);
            element.EnableBindingStage();
            element.ApplyBindingStage();
            element.CompleteBindingStage();
            for (var i = element.Children.Count - 1; i >= 0; i--)
            {
                if (element.Children[i] is UiElement child)
                {
                    traversal.Add(child);
                }
            }
        }
    }
}

internal static class UiScaleStage
{
    internal static void Run(UiElement root, float scale, List<UiElement> traversal)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(traversal);
        traversal.Clear();
        traversal.Add(root);
        while (traversal.Count != 0)
        {
            var last = traversal.Count - 1;
            var element = traversal[last];
            traversal.RemoveAt(last);
            element.ApplyLayoutScale(scale);
            for (var i = element.Children.Count - 1; i >= 0; i--)
            {
                if (element.Children[i] is UiElement child)
                {
                    traversal.Add(child);
                }
            }
        }
    }
}

internal static class UiStyleStage
{
    internal static void Run(Delta.XAML.UiTheme? theme, Delta.XAML.UiElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        theme?.RefreshStates(root);
    }
}

internal static class UiMeasureStage
{
    internal static void Run(UiElement root, UiSize available)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Measure(available);
    }
}

internal static class UiArrangeStage
{
    internal static void Run(UiElement root, UiRect bounds)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Arrange(bounds);
    }
}

internal static class UiFocusStage
{
    internal static void Run(UiInputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        router.RepairFocusAndCapture();
    }
}
