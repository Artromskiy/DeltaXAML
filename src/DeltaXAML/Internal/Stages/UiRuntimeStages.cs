using Delta.XAML.Contract;

namespace DeltaXAML.Internal;

internal static class UiInputStage
{
    internal static void Run(UiInputRouter router, List<Delta.XAML.UiInputSample> queue)
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
        List<UiControlMutation> controlQueue,
        List<UiMutation> propertyQueue,
        out int applied,
        out int rejected)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(controlQueue);
        ArgumentNullException.ThrowIfNull(propertyQueue);
        nodes.EnsureCurrent(root);
        applied = 0;
        rejected = 0;
        for (var i = 0; i < controlQueue.Count; i++)
        {
            var mutation = controlQueue[i];
            if (!nodes.TryGetNode(mutation.Target, out var node) || node.Element is not { } element)
            {
                continue;
            }

            switch (mutation.Kind)
            {
                case UiControlMutationKind.Input:
                    var input = mutation.Input;
                    UiDescriptorCatalog.ProcessInput(element, in input);
                    break;
                case UiControlMutationKind.RoutedEvent:
                    var routedEvent = mutation.RoutedEvent;
                    UiDescriptorCatalog.ProcessRoutedEvent(element, in routedEvent);
                    break;
            }
        }

        controlQueue.Clear();
        for (var i = 0; i < propertyQueue.Count; i++)
        {
            var mutation = propertyQueue[i];
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

        propertyQueue.Clear();
    }
}

internal static class UiMeasureQueue
{
    internal static void Add(in UiMeasureContext context, UiElement child, UiSize available)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (context.Requests is not { } requests || context.Nodes is not { } nodes)
        {
            return;
        }

        var contentAvailable = ElementPlacementMixin.MeasureAvailable(child, available);
        if (!child.NeedsMeasure(contentAvailable))
        {
            return;
        }

        var id = new UiNodeId(child.Id.Value, child.Generation);
        if (nodes.TryGetNode(id, out _))
        {
            requests.Add(new(id, contentAvailable));
        }
    }
}

internal static class UiBindingStage
{
    internal static void Run(
        UiNodeStore nodes,
        UiElement root,
        List<UiNodeId> traversal,
        List<UiNodeId> childOrder,
        bool force = false)
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

            if (!force && !element.NeedsBindingStage)
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
                if (force || (nodes.TryGetNode(childOrder[i], out var child) && child.Element is { } childElement && childElement.NeedsBindingStage))
                {
                    traversal.Add(childOrder[i]);
                }
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

internal static class UiImageMetadataStage
{
    internal static void Run(
        Delta.XAML.IUiImageMetadataResolver? resolver,
        UiNodeStore nodes,
        UiElement root,
        List<UiNodeId> traversal,
        List<UiNodeId> childOrder)
    {
        if (resolver is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
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

            if (element is Image image && image.Source != Guid.Empty)
            {
                var resource = new UiResourceId(image.Source);
                if (resolver.TryGetMetadata(resource, out var metadata))
                {
                    if (!float.IsFinite(metadata.Width) || !float.IsFinite(metadata.Height) ||
                        metadata.Width < 0 || metadata.Height < 0)
                    {
                        throw new InvalidOperationException("Image metadata dimensions must be finite and non-negative.");
                    }

                    var status = metadata.HasError ? (byte)Delta.XAML.UiImageStatus.Error :
                        metadata.IsReady ? (byte)Delta.XAML.UiImageStatus.Ready : (byte)Delta.XAML.UiImageStatus.Loading;
                    image.SetMetadata(metadata.Width, metadata.Height, status);
                }
                else
                {
                    image.SetMetadata(0, 0, (byte)Delta.XAML.UiImageStatus.Loading);
                }
            }

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
    internal static void Run(
        Delta.XAML.UiTheme? theme,
        UiNodeStore nodes,
        Delta.XAML.UiElement root,
        List<UiNodeId> traversal,
        List<UiNodeId> childOrder)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(childOrder);
        nodes.EnsureCurrent(root.RetainedElement);
        traversal.Clear();
        traversal.Add(new(root.RetainedElement.Id.Value, root.RetainedElement.Generation));
        while (traversal.Count != 0)
        {
            var last = traversal.Count - 1;
            var id = traversal[last];
            traversal.RemoveAt(last);
            if (!nodes.TryGetNode(id, out var record) || record.Element is not { } element)
            {
                continue;
            }

            if (element.NeedsResourceStage)
            {
                element.ApplyResourceStage();
            }

            if (theme is null)
            {
                element.CompleteStyleStage();
            }

            if (!nodes.TryCopyLogicalChildren(record.Id, childOrder))
            {
                continue;
            }

            for (var i = childOrder.Count - 1; i >= 0; i--)
            {
                if (nodes.TryGetNode(childOrder[i], out var child) &&
                    child.Element is { } childElement &&
                    (childElement.DirtyFlags & (UiDirtyMask.Resource | UiDirtyMask.Style)) != 0)
                {
                    traversal.Add(childOrder[i]);
                }
            }
        }

        theme?.RefreshStates(nodes, root, traversal, childOrder);
        traversal.Clear();
        traversal.Add(new(root.RetainedElement.Id.Value, root.RetainedElement.Generation));
        while (traversal.Count != 0)
        {
            var last = traversal.Count - 1;
            var id = traversal[last];
            traversal.RemoveAt(last);
            if (!nodes.TryGetNode(id, out var record) || record.Element is not { } element)
            {
                continue;
            }

            element.ApplyTemplateOwnerBindings();
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

internal static class UiMeasureStage
{
    internal static void Run(
        UiNodeStore nodes,
        UiElement root,
        UiSize available,
        UiMeasureQueueBuffer queue,
        List<UiNodeId> childOrder,
        Delta.XAML.UiTextLayoutCache? textLayout = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(childOrder);
        nodes.EnsureCurrent(root);
        queue.Clear();
        var rootAvailable = ElementPlacementMixin.MeasureAvailable(root, available);
        if (!root.NeedsMeasure(rootAvailable))
        {
            return;
        }

        queue.Add(new(
            new(root.Id.Value, root.Generation),
            rootAvailable));
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

            var childAvailable = UiDescriptorCatalog.ChildMeasureAvailable(node.RuntimeType, element, request.Available);
            if (!nodes.TryCopyLogicalChildren(request.Element, childOrder))
            {
                continue;
            }
            for (var childIndex = 0; childIndex < childOrder.Count; childIndex++)
            {
                if (nodes.TryGetNode(childOrder[childIndex], out var childNode) &&
                    childNode.Element is { } child)
                {
                    var availableForChild = ElementPlacementMixin.MeasureAvailable(child, childAvailable);
                    if (child.NeedsMeasure(availableForChild))
                    {
                        queue.Add(new(childNode.Id, availableForChild));
                    }
                }
            }
        }

        for (var i = queue.Count - 1; i >= 0; i--)
        {
            var request = queue[i];
            if (nodes.TryGetNode(request.Element, out var node) && node.Element is { } element)
            {
                ExecuteMeasure(nodes, element, node.RuntimeType, request.Available, queue, textLayout);
            }
        }
    }

    private static void ExecuteMeasure(
        UiNodeStore nodes,
        UiElement element,
        UiRuntimeTypeIndex runtimeType,
        UiSize available,
        UiMeasureQueueBuffer requests,
        Delta.XAML.UiTextLayoutCache? textLayout)
    {
        if (element.CanSkipMeasure(available))
        {
            return;
        }

        if (!element.ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            element.CompleteMeasure(available, default);
            return;
        }

        var children = nodes.GetLogicalChildren(new(element.Id.Value, element.Generation));
        var metrics = default(UiTextMeasureMetrics);
        if (textLayout is not null)
        {
            textLayout.TryMeasureText(runtimeType, element, out metrics);
        }

        var measured = UiDescriptorCatalog.Measure(
            runtimeType,
            element,
            new(available, element.LayoutScale, children, true, requests, nodes, metrics));
        UiSize desired = new(
            float.IsNaN(element.Width) ? measured.Width : element.Width,
            float.IsNaN(element.Height) ? measured.Height : element.Height);
        element.CompleteMeasure(available, ElementPlacementMixin.IncludeMargin(element, desired));
    }
}

internal static class UiArrangeStage
{
    internal static void Run(UiNodeStore nodes, UiElement root, UiRect bounds, UiArrangeQueueBuffer queue)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(queue);
        nodes.EnsureCurrent(root);
        queue.Clear();
        if (!root.NeedsArrange(bounds, bounds))
        {
            return;
        }

        queue.Add(new(new(root.Id.Value, root.Generation), bounds, bounds));
        for (var i = 0; i < queue.Count; i++)
        {
            var request = queue[i];
            if (nodes.TryGetNode(request.Element, out var node) && node.Element is { } element)
            {
                ExecuteArrange(nodes, element, node.RuntimeType, request.Bounds, request.Clip, queue);
            }
        }
    }

    private static void ExecuteArrange(
        UiNodeStore nodes,
        UiElement element,
        UiRuntimeTypeIndex runtimeType,
        UiRect bounds,
        UiRect clip,
        UiArrangeQueueBuffer requests)
    {
        if (element.CanSkipArrange(bounds, clip))
        {
            return;
        }

        if (!element.ParticipatesIn(Delta.XAML.UiParticipation.Layout))
        {
            element.SetNonParticipatingArrange(clip);
            element.CompleteArrange(bounds, clip);
            return;
        }

        element.SetArrangeFrame(bounds, clip);
        var children = nodes.GetLogicalChildren(new(element.Id.Value, element.Generation));
        UiDescriptorCatalog.Arrange(runtimeType, element, new(bounds, clip, children, requests, nodes));
        element.CompleteArrange(bounds, clip);
    }
}

internal static class UiArrangeQueue
{
    internal static void Add(in UiArrangeContext context, UiElement child, UiRect bounds)
        => Add(in context, child, bounds, UiRect.Intersect(context.Clip, bounds));

    internal static void Add(in UiArrangeContext context, UiElement child, UiRect bounds, UiRect clip)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (context.Requests is not { } requests || context.Nodes is not { } nodes)
        {
            return;
        }

        var arrangedBounds = ElementPlacementMixin.Arrange(child, bounds);
        var effectiveClip = UiRect.Intersect(context.Clip, clip);
        if (nodes.TryGetNode(new(child.Id.Value, child.Generation), out var node) && child.NeedsArrange(arrangedBounds, effectiveClip))
        {
            requests.Add(new(node.Id, arrangedBounds, effectiveClip));
        }
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
