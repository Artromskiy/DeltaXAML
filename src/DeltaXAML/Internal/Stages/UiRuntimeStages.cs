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
            if (!nodes.TryCopyLogicalChildren(record.Id, childOrder))
            {
                continue;
            }
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
            if (!nodes.TryCopyLogicalChildren(record.Id, childOrder))
            {
                continue;
            }
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
    internal static void Run(
        UiNodeStore nodes,
        UiElement root,
        UiSize available,
        List<UiMeasureRequest> queue,
        List<UiNodeId> childOrder)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(childOrder);
        nodes.EnsureCurrent(root);
        queue.Clear();
        if (!root.NeedsMeasure(available))
        {
            return;
        }

        queue.Add(new(new(root.Id.Value, root.Generation), available));
        for (var i = 0; i < queue.Count; i++)
        {
            var request = queue[i];
            if (!nodes.TryGetNode(request.Element, out var node) || node.Element is not { } element)
            {
                continue;
            }

            if (!element.NeedsMeasure(request.Available))
            {
                continue;
            }

            if (!element.ParticipatesIn(Delta.XAML.UiParticipation.Layout))
            {
                continue;
            }

            var childAvailable = UiDescriptorCatalog.ChildMeasureAvailable(element, request.Available);
            if (!nodes.TryCopyLogicalChildren(request.Element, childOrder))
            {
                continue;
            }
            for (var childIndex = 0; childIndex < childOrder.Count; childIndex++)
            {
                if (nodes.TryGetNode(childOrder[childIndex], out var childNode) &&
                    childNode.Element is { } child &&
                    child.NeedsMeasure(childAvailable))
                {
                    queue.Add(new(childNode.Id, childAvailable));
                }
            }
        }

        for (var i = queue.Count - 1; i >= 0; i--)
        {
            var request = queue[i];
            if (nodes.TryGetNode(request.Element, out var node) && node.Element is { } element)
            {
                element.MeasureStage(request.Available, nodes);
            }
        }
    }
}

internal static class UiArrangeStage
{
    internal static void Run(UiNodeStore nodes, UiElement root, UiRect bounds, List<UiArrangeRequest> queue)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);
        nodes.EnsureCurrent(root);
        queue.Clear();
        if (!root.NeedsArrange(bounds))
        {
            return;
        }

        queue.Add(new(new(root.Id.Value, root.Generation), bounds));
        for (var i = 0; i < queue.Count; i++)
        {
            var request = queue[i];
            if (nodes.TryGetNode(request.Element, out var node) && node.Element is { } element)
            {
                element.ArrangeStage(request.Bounds, queue, nodes);
            }
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
            if (context.Nodes is null)
            {
                if (element.NeedsArrange(bounds))
                {
                    context.Requests.Add(new(new(element.Id.Value, element.Generation), bounds));
                }
            }
            else if (context.Nodes.TryGetNode(new(element.Id.Value, element.Generation), out var node) &&
                     element.NeedsArrange(bounds))
            {
                context.Requests.Add(new(node.Id, bounds));
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
