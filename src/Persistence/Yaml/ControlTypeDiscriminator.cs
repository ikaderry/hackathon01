// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.PowerPlatform.PowerApps.Persistence.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization.BufferedDeserialization.TypeDiscriminators;

namespace Microsoft.PowerPlatform.PowerApps.Persistence.Yaml;

internal class ControlTypeDiscriminator : ITypeDiscriminator
{
    public Type BaseType => typeof(object);

    public bool TryDiscriminate(IParser buffer, out Type? suggestedType)
    {
        suggestedType = null;

        if (!buffer.TryFindMappingEntry(s => BuiltInTemplates.ShortNameToType.ContainsKey(s.Value) || s.Value == Control.YamlName, out var scalar, out var value))
            return false;

        // Control is abstract, so we need to return a concrete type.
        if (scalar!.Value == Control.YamlName)
        {
            var templateUri = ((Scalar)value!).Value.Trim();
            if (BuiltInTemplates.TemplateToType.TryGetValue(templateUri, out suggestedType))
                return true;

            // If we don't have a type for this template, we'll use the custom control type.
            suggestedType = typeof(CustomControl);

            return true;
        }

        suggestedType = BuiltInTemplates.ShortNameToType[scalar!.Value];
        return true;
    }
}
