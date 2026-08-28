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
    internal static void Run(
        UiNodeStore nodes,
        UiElement root,
        List<UiNodeId> traversal,
        List<UiNodeId> childOrder)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(childOrder);
        nodes.EnsureCurrent(root);
        traversal.Clear();
        traversal.Add(new(root.Id.Value, root.Generation));
        while (traversal.Count != 0)
        {
            var last = traversal.Count - 1;
            var id = traversal[last];
            traversal.RemoveAt(last);
            if (!nodes.TryGetNode(id, out var record) || record.Element is not { } element)
            {
                continue;
            }

            element.EnableBindingStage();
            element.ApplyBindingStage();
            element.CompleteBindingStage();
            nodes.CopyLogicalChildren(record.Id, childOrder);
            for (var i = childOrder.Count - 1; i >= 0; i--)
            {
                traversal.Add(childOrder[i]);
            }
        }
    }
}

internal static class UiScaleStage
{
    internal static void Run(
        UiNodeStore nodes,
        UiElement root,
        float scale,
        List<UiNodeId> traversal,
        List<UiNodeId> childOrder)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(childOrder);
        nodes.EnsureCurrent(root);
        traversal.Clear();
        traversal.Add(new(root.Id.Value, root.Generation));
        while (traversal.Count != 0)
        {
            var last = traversal.Count - 1;
            var id = traversal[last];
            traversal.RemoveAt(last);
            if (!nodes.TryGetNode(id, out var record) || record.Element is not { } element)
            {
                continue;
            }

            element.ApplyLayoutScale(scale);
            nodes.CopyLogicalChildren(record.Id, childOrder);
            for (var i = childOrder.Count - 1; i >= 0; i--)
            {
                traversal.Add(childOrder[i]);
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
        if (!root.NeedsMeasure(available))
        {
            return;
        }

        queue.Add(new(root, available));
        for (var i = 0; i < queue.Count; i++)
        {
            var request = queue[i];
            if (!request.Element.NeedsMeasure(request.Available))
            {
                continue;
            }

            if (!request.Element.ParticipatesIn(Delta.XAML.UiParticipation.Layout))
            {
                continue;
            }

            var childAvailable = UiDescriptorCatalog.ChildMeasureAvailable(request.Element, request.Available);
            for (var childIndex = 0; childIndex < request.Element.Children.Count; childIndex++)
            {
                if (request.Element.Children[childIndex] is UiElement child && child.NeedsMeasure(childAvailable))
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
        if (!root.NeedsArrange(bounds))
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
            if (element.NeedsArrange(bounds))
            {
                context.Requests.Add(new(element, bounds));
            }

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
