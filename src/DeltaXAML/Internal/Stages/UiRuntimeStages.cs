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
    internal static void Run(UiElement root, UiSize available, List<UiMeasureRequest> queue)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);
        queue.Clear();
        if ((root.DirtyFlags & UiDirtyMask.Measure) == 0)
        {
            return;
        }

        queue.Add(new(root, available));
        for (var i = 0; i < queue.Count; i++)
        {
            var request = queue[i];
            if (!request.Element.ParticipatesIn(Delta.XAML.UiParticipation.Layout))
            {
                continue;
            }

            var childAvailable = request.Element.MeasureChildAvailable(request.Available);
            for (var childIndex = 0; childIndex < request.Element.Children.Count; childIndex++)
            {
                if (request.Element.Children[childIndex] is UiElement child)
                {
                    queue.Add(new(child, childAvailable));
                }
            }
        }

        for (var i = queue.Count - 1; i >= 0; i--)
        {
            var request = queue[i];
            request.Element.MeasureStage(request.Available);
        }
    }
}

internal static class UiArrangeStage
{
    internal static void Run(UiElement root, UiRect bounds, List<UiArrangeRequest> queue)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);
        queue.Clear();
        if ((root.DirtyFlags & UiDirtyMask.Arrange) == 0)
        {
            return;
        }

        queue.Add(new(root, bounds));
        for (var i = 0; i < queue.Count; i++)
        {
            var request = queue[i];
            request.Element.ArrangeStage(request.Bounds, queue);
        }
    }
}

internal static class UiArrangeQueue
{
    internal static void Add(in UiArrangeContext context, IUiElement child, UiRect bounds)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (context.Requests is not null && child is UiElement element)
        {
            context.Requests.Add(new(element, bounds));
            return;
        }

        child.Arrange(bounds);
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
