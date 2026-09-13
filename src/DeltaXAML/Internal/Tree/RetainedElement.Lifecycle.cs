using UiDirtyFlags = DeltaXAML.Internal.UiDirtyMask;

namespace DeltaXAML.Internal;

internal partial class UiElement
{
    internal void DisposeRuntime()
    {
        if (_runtimeDisposed)
        {
            return;
        }

        _runtimeDisposed = true;
        var traversal = new List<UiElement> { this };
        for (var i = 0; i < traversal.Count; i++)
        {
            var element = traversal[i];
            element._properties.Dispose();
            foreach (var binding in element._bindingRuntimes.Values)
            {
                binding.Dispose();
            }

            foreach (var binding in element._externalBindingRuntimes.Values)
            {
                binding.Dispose();
            }

            foreach (var binding in element._compiledBindingRuntimes.Values)
            {
                binding.Dispose();
            }

            for (var bindingIndex = 0; bindingIndex < element._collectionBindingRuntimes.Count; bindingIndex++)
            {
                element._collectionBindingRuntimes[bindingIndex].Dispose();
            }

            element._bindingRuntimes.Clear();
            element._externalBindingRuntimes.Clear();
            element._compiledBindingRuntimes.Clear();
            element._collectionBindingRuntimes.Clear();
            for (var childIndex = 0; childIndex < element.Children.Count; childIndex++)
            {
                traversal.Add(element.Children[childIndex]);
            }
        }
    }
}
