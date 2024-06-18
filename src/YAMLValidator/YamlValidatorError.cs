// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Json.Schema;

namespace Microsoft.PowerPlatform.PowerApps.Persistence;
internal class YamlValidatorError
{
    public string InstanceLocation { get; }
    public string SchemaPath { get; }
    public IReadOnlyDictionary<string, string>? Errors { get; }

    public YamlValidatorError(EvaluationResults results)
    {
        InstanceLocation = results.InstanceLocation.ToString();
        SchemaPath = results.EvaluationPath.ToString();
        Errors = results.Errors;
    }

    public override string ToString()
    {
        var errString = "";
        if (Errors != null)
        {
            foreach (var error in Errors)
            {
                var errType = string.IsNullOrEmpty(error.Key) ? "Error" : error.Key;
                errString += $"\t{errType}: {error.Value}\n";
            }
        }
        return $"InstanceLocation: {InstanceLocation}\nSchemaPath: {SchemaPath}\nErrors:\n{errString}";
    }
}
