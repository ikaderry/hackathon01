// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.PowerPlatform.PowerApps.Persistence.Attributes;
using Microsoft.PowerPlatform.PowerApps.Persistence.Extensions;
using Microsoft.PowerPlatform.PowerApps.Persistence.Models;

namespace Microsoft.PowerPlatform.PowerApps.Persistence.Templates;

/// <summary>
/// Control template store.
/// </summary>
public class ControlTemplateStore : IControlTemplateStore
{
    private readonly Dictionary<string, ControlTemplate> _classicTemplatesByName = new();
    private readonly Dictionary<string, ControlTemplate> _controlTemplatesByName = new();
    private readonly Dictionary<string, List<ControlTemplate>> _controlTemplatesById = new();
    private readonly Dictionary<string, Type> _nameToType = new();
    private readonly Dictionary<Type, string> _typeToName = new();
    private readonly Dictionary<Type, ControlTemplate> _typeToTemplate = new();

    public ControlTemplateStore()
    {
    }

    public void DiscoverBuiltInTemplateTypes()
    {
        var types = typeof(Control).Assembly.DefinedTypes;
        foreach (var type in types)
        {
            // Ignore anything that isn't a control.
            if (!type.IsAssignableTo(typeof(Control)))
                continue;

            if (type.GetCustomAttributes(true).FirstOrDefault(a => a is FirstClassAttribute) is FirstClassAttribute firstClassAttribute)
            {
                var controlTemplate = _controlTemplatesByName[firstClassAttribute.TemplateName];

                _nameToType.Add(controlTemplate.InvariantName, type);
                if (controlTemplate.HasDisplayName)
                    _nameToType.Add(controlTemplate.DisplayName, type);

                _typeToName.Add(type, controlTemplate.InvariantName);
                _typeToTemplate.Add(type, controlTemplate);
            }
        }
    }

    public void Add(ControlTemplate controlTemplate)
    {
        _ = controlTemplate ?? throw new ArgumentNullException(nameof(controlTemplate));

        if (controlTemplate.IsClassic)
            AddToTameDictionary(_classicTemplatesByName, controlTemplate);
        else
            AddToTameDictionary(_controlTemplatesByName, controlTemplate);

        // There can be multiple control templates with the same id, so we store them in a list.
        if (!_controlTemplatesById.TryGetValue(controlTemplate.Id, out var controlTemplates))
        {
            controlTemplates = new List<ControlTemplate>();
            _controlTemplatesById.Add(controlTemplate.Id, controlTemplates);
        }
        controlTemplates.Add(controlTemplate);

        // Add nested templates.
        if (controlTemplate.NestedTemplates != null)
        {
            foreach (var nestedTemplate in controlTemplate.NestedTemplates)
            {
                Add(nestedTemplate);
            }
        }
    }

    private static void AddToTameDictionary(Dictionary<string, ControlTemplate> nameDictionary, ControlTemplate controlTemplate)
    {
        if (nameDictionary.TryGetValue(controlTemplate.InvariantName, out var existingTemplate))
        {
            if (controlTemplate.Id != existingTemplate.Id || controlTemplate.IsClassic != controlTemplate.IsClassic)
                throw new InvalidOperationException($"Control template with name '{controlTemplate.InvariantName}' already exists.");
            return;
        }

        nameDictionary.Add(controlTemplate.InvariantName, controlTemplate);
        if (controlTemplate.HasDisplayName)
        {
            if (nameDictionary.ContainsKey(controlTemplate.DisplayName))
                return;

            nameDictionary.Add(controlTemplate.DisplayName, controlTemplate);
        }
    }

    /// <summary>
    /// Returns the control template with the given name.
    /// </summary>
    public bool TryGetTemplateByName(string name, [MaybeNullWhen(false)] out ControlTemplate controlTemplate, bool isClassic = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (isClassic)
            return _classicTemplatesByName.TryGetValue(name.FirstCharToUpper(), out controlTemplate);

        return _controlTemplatesByName.TryGetValue(name.FirstCharToUpper(), out controlTemplate);
    }

    public bool TryGetControlTypeByName(string name, [MaybeNullWhen(false)] out Type controlType)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        return _nameToType.TryGetValue(name, out controlType);
    }

    /// <summary>
    /// Returns the control template with the given name.
    /// </summary>
    public ControlTemplate GetByName(string name)
    {
        return _controlTemplatesByName[name];
    }

    /// <summary>
    /// Returns the control template with the given id.
    /// </summary>
    public bool TryGetById(string id, [MaybeNullWhen(false)] out ControlTemplate controlTemplate)
    {
        if (!_controlTemplatesById.TryGetValue(id, out var controlTemplates))
        {
            controlTemplate = null;
            return false;
        }

        // If there are multiple control templates with the same id, we return the first one.
        controlTemplate = controlTemplates.First();
        return true;
    }

    /// <summary>
    /// Returns the control template with the given id or name.
    /// </summary>
    public bool TryGetByIdOrName(string idOrName, [MaybeNullWhen(false)] out ControlTemplate controlTemplate, bool isClassic = false)
    {
        if (TryGetTemplateByName(idOrName, out controlTemplate, isClassic))
            return true;

        if (TryGetById(idOrName, out controlTemplate))
            return true;

        return false;
    }

    public bool TryGetByType(Type type, [MaybeNullWhen(false)] out ControlTemplate controlTemplate)
    {
        return _typeToTemplate.TryGetValue(type, out controlTemplate);
    }

    /// <summary>
    /// Returns the control template with the given id.
    /// </summary>
    public ControlTemplate GetById(string id)
    {
        if (!TryGetById(id, out var controlTemplate))
            throw new KeyNotFoundException($"Control template with id '{id}' not found.");

        return controlTemplate;
    }

    public bool Contains(Type type)
    {
        if (type == typeof(BuiltInControl))
            return true;

        return _typeToName.ContainsKey(type);
    }

    public bool Contains(string name)
    {
        return _controlTemplatesByName.ContainsKey(name);
    }

    public bool TryGetName(Type type, [MaybeNullWhen(false)] out string name)
    {
        return _typeToName.TryGetValue(type, out name);
    }

    public Type GetControlType(string name)
    {
        // First check if we have concrete type for the name.
        if (_nameToType.TryGetValue(name, out var type))
            return type;

        // If not, check if we have a control template for the name.
        if (_controlTemplatesByName.TryGetValue(name, out _))
            return typeof(BuiltInControl);

        // If not, return the custom control type.
        return typeof(CustomControl);
    }
}
