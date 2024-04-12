// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.PowerPlatform.PowerApps.Persistence.Collections;
using Microsoft.PowerPlatform.PowerApps.Persistence.Extensions;
using Microsoft.PowerPlatform.PowerApps.Persistence.Models;

namespace Microsoft.PowerPlatform.PowerApps.Persistence.Templates;

public class ControlFactory : IControlFactory
{
    private readonly IControlTemplateStore _controlTemplateStore;

    public ControlFactory(IControlTemplateStore controlTemplateStore)
    {
        _controlTemplateStore = controlTemplateStore ?? throw new ArgumentNullException(nameof(controlTemplateStore));
    }

    public Control Create(string name, string template, string? componentDefinitionName = null, string? variant = null,
        ControlPropertiesCollection? properties = null, IList<Control>? children = null)
    {
        if (TryCreateComponent(name, template, componentDefinitionName, variant, properties, children, controlDefinition: null, out var control))
            return control;

        if (_controlTemplateStore.TryGetByIdOrName(template, out var controlTemplate))
        {
            if (TryCreateFirstClassControl(name, controlTemplate.Name, variant ?? string.Empty, properties, children, controlDefinition: null, out control))
                return control;

            return new BuiltInControl(name, variant ?? string.Empty, controlTemplate)
            {
                Properties = properties ?? new(),
                Children = children
            };
        }

        return new CustomControl(name, variant ?? string.Empty, new ControlTemplate(template))
        {
            Properties = properties ?? new(),
            Children = children
        };
    }

    public Control Create(string name, string template, string componentDefinitionName, Dictionary<string, object?>? controlDefinition)
    {
        string? variant = null;
        ControlPropertiesCollection? properties = null;
        IList<Control>? children = null;

        if (controlDefinition != null)
        {
            controlDefinition.TryGetValue(nameof(Control.Variant), out variant);
            controlDefinition.TryGetValue(nameof(Control.Properties), out properties);
            controlDefinition.TryGetValue(nameof(Control.Children), out children);
        }

        if (TryCreateComponent(name, template, componentDefinitionName, variant, properties, children, controlDefinition, out var control))
            return control;

        if (_controlTemplateStore.TryGetByIdOrName(template, out var controlTemplate))
        {
            if (TryCreateFirstClassControl(name, controlTemplate.Name, variant ?? string.Empty, properties, children, controlDefinition, out control))
            {
                return control;
            }

            return new BuiltInControl(name, variant ?? string.Empty, controlTemplate)
            {
                Properties = properties ?? new(),
                Children = children
            };
        }

        return new CustomControl(name, variant ?? string.Empty, new ControlTemplate(template))
        {
            Properties = properties ?? new(),
            Children = children
        };
    }

    public Control Create(string name, ControlTemplate template, string? componentDefinitionName = null, string? variant = null, ControlPropertiesCollection? properties = null, IList<Control>? children = null)
    {
        if (TryCreateComponent(name, template, componentDefinitionName, variant, properties, children, controlDefinition: null, out var control))
            return control;

        if (TryCreateFirstClassControl(name, template.Name, variant ?? string.Empty, properties, children, controlDefinition: null, out control))
            return control;

        return new BuiltInControl(name, variant ?? string.Empty, template)
        {
            Properties = properties ?? new(),
            Children = children
        };
    }

    public App CreateApp(string name, ControlPropertiesCollection? properties = null)
    {
        return new App(name, string.Empty, _controlTemplateStore)
        {
            Properties = properties ?? new(),
            Children = new Control[] { Create("Host", BuiltInTemplates.Host.Name) }
        };
    }

    public Screen CreateScreen(string name, ControlPropertiesCollection? properties = null, IList<Control>? children = null)
    {
        return new Screen(name, string.Empty, _controlTemplateStore)
        {
            Properties = properties ?? new(),
            Children = children
        };
    }

    private bool TryCreateComponent(string name, string template, string? componentDefinitionName, string? variant,
        ControlPropertiesCollection? properties, IList<Control>? children, Dictionary<string, object?>? controlDefinition,
        [MaybeNullWhen(false)] out Control control)
    {
        control = null;
        if (!string.IsNullOrWhiteSpace(componentDefinitionName))
        {
            control = new ComponentInstance(name, variant ?? string.Empty,
                new ControlTemplate(ComponentInstance.ComponentInstanceTemplateId) { Name = componentDefinitionName })
            {
                ComponentName = componentDefinitionName,
                Properties = properties ?? new(),
                Children = children
            };
        }
        else if (template.Equals(BuiltInTemplates.Component.Name, StringComparison.OrdinalIgnoreCase))
        {
            control = new ComponentDefinition(name, variant ?? string.Empty, CreateControlTemplate(name, controlDefinition))
            {
                Properties = properties ?? new(),
                Children = children
            };
        }

        if (control != null && controlDefinition != null)
            control.AfterCreate(controlDefinition);

        return control != null;
    }

    private static bool TryCreateComponent(string name, ControlTemplate template, string? componentDefinitionName, string? variant,
        ControlPropertiesCollection? properties, IList<Control>? children, Dictionary<string, object?>? controlDefinition,
        [MaybeNullWhen(false)] out Control control)
    {
        if (!string.IsNullOrWhiteSpace(componentDefinitionName))
        {
            control = new ComponentInstance(name, variant ?? string.Empty, template)
            {
                ComponentName = componentDefinitionName,
                Properties = properties ?? new(),
                Children = children
            };
        }
        else
        {
            control = new ComponentDefinition(name, variant ?? string.Empty, template)
            {
                Properties = properties ?? new(),
                Children = children
            };
        }

        if (controlDefinition != null)
            control.AfterCreate(controlDefinition);

        return true;
    }

    public ControlTemplate CreateControlTemplate(string name, Dictionary<string, object?>? controlDefinition)
    {
        if (controlDefinition == null || !controlDefinition.TryGetValue<string>(nameof(ComponentDefinition.Type), out var componentType))
            return new ControlTemplate(BuiltInTemplates.Component.Id) { Name = name };

        if (!Enum.TryParse<ComponentType>(componentType, out var componentTypeEnum))
            return new ControlTemplate(BuiltInTemplates.Component.Id) { Name = name };

        return CreateControlTemplate(name, componentTypeEnum);
    }

    public ControlTemplate CreateControlTemplate(string name, ComponentType componentType)
    {
        switch (componentType)
        {
            case ComponentType.Canvas:
                return new ControlTemplate(BuiltInTemplates.Component.Id) { Name = name };
            case ComponentType.Data:
                return new ControlTemplate(BuiltInTemplates.DataComponent.Id) { Name = name };
            case ComponentType.Function:
                return new ControlTemplate(BuiltInTemplates.FunctionComponent.Id) { Name = name };
            case ComponentType.Command:
                return new ControlTemplate(BuiltInTemplates.CommandComponent.Id) { Name = name };
            default:
                return new ControlTemplate(BuiltInTemplates.Component.Id) { Name = name };
        }
    }

    private bool TryCreateFirstClassControl(string name, string template, string variant,
        ControlPropertiesCollection? properties, IList<Control>? children, Dictionary<string, object?>? controlDefinition,
        [MaybeNullWhen(false)] out Control control)
    {
        control = null;
        if (!_controlTemplateStore.TryGetControlTypeByName(template, out var controlType))
            return false;

        var instance = Activator.CreateInstance(controlType, name, variant, _controlTemplateStore);
        if (instance is not Control controlInstance)
            throw new InvalidOperationException($"Failed to create control of type {controlType.Name}.");

        if (properties is not null)
            controlInstance.Properties = properties;

        controlInstance.Children = children;
        if (controlDefinition != null)
            controlInstance.AfterCreate(controlDefinition);

        control = controlInstance;

        return true;
    }
}
