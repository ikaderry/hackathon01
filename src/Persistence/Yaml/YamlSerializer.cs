// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.PowerPlatform.PowerApps.Persistence.Models;
using YamlDotNet.Serialization;

namespace Microsoft.PowerPlatform.PowerApps.Persistence.Yaml;

public class YamlSerializer : IYamlSerializer
{
    private readonly ISerializer _serializer;

    internal YamlSerializer(ISerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public string Serialize(object graph)
    {
        _ = graph ?? throw new ArgumentNullException(nameof(graph));

        return _serializer.Serialize(graph);
    }

    public string SerializeControl<T>(T graph) where T : Control
    {
        _ = graph ?? throw new ArgumentNullException(nameof(graph));

        return _serializer.Serialize(graph);
    }

    public void SerializeControl<T>(TextWriter writer, T graph) where T : Control
    {
        _ = writer ?? throw new ArgumentNullException(nameof(writer));
        _ = graph ?? throw new ArgumentNullException(nameof(graph));

        _serializer.Serialize(writer, graph);
    }
}
