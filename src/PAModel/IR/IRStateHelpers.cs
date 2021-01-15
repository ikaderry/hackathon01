// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.PowerPlatform.Formulas.Tools.IR;
using Microsoft.PowerPlatform.Formulas.Tools.ControlTemplates;
using Microsoft.PowerPlatform.Formulas.Tools.EditorState;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.AppMagic.Authoring.Persistence;
using System;
using Microsoft.PowerPlatform.Formulas.Tools.Schemas;

namespace Microsoft.PowerPlatform.Formulas.Tools
{
    internal static class IRStateHelpers
    {
        internal static void SplitIRAndState(SourceFile file, EditorStateStore stateStore, TemplateStore templateStore, Entropy entropy, out BlockNode topParentIR)
        {
            var topParentJson = file.Value.TopParent;
            SplitIRAndState(topParentJson, topParentJson.Name, 0, stateStore, templateStore, entropy, out topParentIR);
        }

        private static void SplitIRAndState(ControlInfoJson.Item control, string topParentName, int index, EditorStateStore stateStore, TemplateStore templateStore, Entropy entropy, out BlockNode controlIR)
        {
            // Bottom up, recursively process children
            var childrenWithZIndex = new List<KeyValuePair<BlockNode, double>>();
            var childIndex = 0;
            foreach (var child in control.Children)
            {
                SplitIRAndState(child, topParentName, childIndex, stateStore, templateStore, entropy, out var childBlock);
                childrenWithZIndex.Add(new KeyValuePair<BlockNode, double>(childBlock, GetControlZIndex(child)));
                ++childIndex;
            }
            var children = childrenWithZIndex.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key);
            var isComponentDef = control.Template.IsComponentDefinition ?? false;

            var customPropsToHide = new HashSet<string>();
            var functions = new List<FunctionNode>();
            if (control.Template.CustomProperties?.Any() ?? false)
            {
                if (!isComponentDef)
                {
                    // Skip component property params on instances
                    customPropsToHide = new HashSet<string>(control.Template.CustomProperties
                        .Where(customProp => customProp.IsFunctionProperty)
                        .SelectMany(customProp =>
                            customProp.PropertyScopeKey.PropertyScopeRulesKey
                                .Select(propertyScopeRule => propertyScopeRule.Name)
                        ));
                }
                else
                {
                    // Create FunctionNodes on def
                    foreach (var customProp in control.Template.CustomProperties.Where(prop => prop.IsFunctionProperty))
                    {
                        var name = customProp.Name;
                        customPropsToHide.Add(name);
                        var expression = control.Rules.First(rule => rule.Property == name).InvariantScript;
                        var expressionNode = new ExpressionNode() { Expression = expression };

                        var resultType = new TypeNode() { TypeName = customProp.PropertyDataTypeKey };

                        var args = new List<TypedNameNode>();
                        var argMetadata = new List<ArgMetadataBlockNode>();
                        foreach (var arg in customProp.PropertyScopeKey.PropertyScopeRulesKey)
                        {
                            customPropsToHide.Add(arg.Name);
                            args.Add(new TypedNameNode()
                            {
                                Identifier = arg.ScopeVariableInfo.ScopeVariableName,
                                Kind = new TypeNode()
                                {
                                    TypeName = ((PropertyDataType)arg.ScopeVariableInfo.ScopePropertyDataType).ToString()
                                }
                            });

                            argMetadata.Add(new ArgMetadataBlockNode()
                            {
                                Identifier = arg.ScopeVariableInfo.ScopeVariableName,
                                Default = new ExpressionNode()
                                {
                                    Expression = arg.ScopeVariableInfo.DefaultRule.Replace("\r\n", "\n").Replace("\r", "\n").TrimStart()
                                },
                            });

                            arg.ScopeVariableInfo.DefaultRule = null;
                            arg.ScopeVariableInfo.ScopePropertyDataType = null;
                            arg.ScopeVariableInfo.ParameterIndex = null;
                            arg.ScopeVariableInfo.ParentPropertyName = null;
                        }

                        argMetadata.Add(new ArgMetadataBlockNode()
                        {
                            Identifier = PAConstants.ThisPropertyIdentifier,
                            Default = new ExpressionNode()
                            {
                                Expression = expression.Replace("\r\n", "\n").Replace("\r", "\n").TrimStart(),
                            },
                        });

                        functions.Add(new FunctionNode()
                        {
                            Args = args,
                            Metadata = argMetadata,
                            Identifier = name
                        });
                    }
                }
            }

            var properties = new List<PropertyNode>();
            var propStates = new List<PropertyState>();
            foreach (var property in control.Rules)
            {
                var (prop, state) = SplitProperty(property);
                propStates.Add(state);

                if (customPropsToHide.Contains(property.Property))
                    continue;

                properties.Add(prop);
            }

            controlIR = new BlockNode()
            {
                Name = new TypedNameNode()
                {
                    Identifier = control.Name,
                    Kind = new TypeNode()
                    {
                        TypeName = control.Template.TemplateDisplayName ?? control.Template.Name,
                        OptionalVariant = string.IsNullOrEmpty(control.VariantName) ? null : control.VariantName
                    }
                },
                Children = children.ToList(),
                Properties = properties,
                Functions = functions,
            };


            if (templateStore.TryGetTemplate(control.Template.Name, out var templateState))
            {
                if (isComponentDef)
                {
                    templateState.IsComponentTemplate = true;
                    templateState.CustomProperties = control.Template.CustomProperties;
                }
            }
            else
            {
                templateState = new CombinedTemplateState(control.Template);
                templateState.ComponentDefinitionInfo = null;
                var templateName = templateState.TemplateDisplayName ?? templateState.Name;
                templateStore.AddTemplate(templateName, templateState);
            }

            entropy.ControlUniqueIds.Add(control.Name, int.Parse(control.ControlUniqueId));
            var controlState = new ControlState()
            {
                Name = control.Name,
                PublishOrderIndex = control.PublishOrderIndex,
                TopParentName = topParentName,
                Properties = propStates,
                StyleName = control.StyleName,
                ExtensionData = control.ExtensionData,
                ParentIndex = index,
                IsComponentDefinition = control.Template.IsComponentDefinition,
            };

            stateStore.TryAddControl(controlState);
        }

        private static (PropertyNode prop, PropertyState state) SplitProperty(ControlInfoJson.RuleEntry rule)
        {
            var script = rule.InvariantScript.Replace("\r\n", "\n").Replace("\r", "\n").TrimStart();
            var prop = new PropertyNode() { Expression = new ExpressionNode() { Expression = script }, Identifier = rule.Property };
            var state = new PropertyState() { PropertyName = rule.Property, ExtensionData = rule.ExtensionData, NameMap = rule.NameMap, RuleProviderType = rule.RuleProviderType };
            return (prop, state);
        }

        internal static double GetControlZIndex(ControlInfoJson.Item control)
        {
            if (control.Rules == null)
                return -1;

            var zindexRule = control.Rules.FirstOrDefault(rule => rule.Property == "ZIndex");

            if (zindexRule == default)
                return -1;

            if (!double.TryParse(zindexRule.InvariantScript, out var zindexResult) || double.IsNaN(zindexResult) || double.IsInfinity(zindexResult))
                return -1;

            return zindexResult;
        }

        internal static SourceFile CombineIRAndState(BlockNode blockNode, ErrorContainer errors, EditorStateStore stateStore, TemplateStore templateStore, UniqueIdRestorer uniqueIdRestorer)
        {
            var topParentJson = CombineIRAndState(blockNode, errors, string.Empty, stateStore, templateStore, uniqueIdRestorer);
            return SourceFile.New(new ControlInfoJson() { TopParent = topParentJson.item });
        }

        // Returns pair of item and index (with respect to parent order)
        private static (ControlInfoJson.Item item, int index) CombineIRAndState(BlockNode blockNode, ErrorContainer errors, string parent, EditorStateStore stateStore, TemplateStore templateStore, UniqueIdRestorer uniqueIdRestorer)
        {
            var controlName = blockNode.Name.Identifier;

            // Bottom up, merge children first
            var children = new List<(ControlInfoJson.Item item, int index)>();
            foreach (var childBlock in blockNode.Children)
            {
                children.Add(CombineIRAndState(childBlock, errors, controlName, stateStore, templateStore, uniqueIdRestorer));
            }

            var orderedChildren = children.OrderBy(childPair => childPair.index).Select(pair => pair.item).ToArray();

            var templateIR = blockNode.Name.Kind;
            var templateName = templateIR.TypeName;
            var variantName = templateIR.OptionalVariant;
            ControlInfoJson.Template template;

            if (!templateStore.TryGetTemplate(templateName, out var templateState))
            {
                template = ControlInfoJson.Template.CreateDefaultTemplate(templateName, null);
            }
            else
            {
                template = templateState.ToControlInfoTemplate();
            }

            var uniqueId = uniqueIdRestorer.GetControlId(controlName);
            ControlInfoJson.Item resultControlInfo;
            if (stateStore.TryGetControlState(controlName, out var state))
            {
                var properties = new List<ControlInfoJson.RuleEntry>();
                foreach (var propIR in blockNode.Properties)
                {
                    properties.Add(CombinePropertyIRAndState(propIR, errors, state));
                }

                if (blockNode.Functions.Any())
                {
                    foreach (var func in blockNode.Functions)
                    {
                        var funcName = func.Identifier;
                        var thisPropertyBlock = func.Metadata.FirstOrDefault(metadata => metadata.Identifier == PAConstants.ThisPropertyIdentifier);
                        if (thisPropertyBlock == default)
                        {
                            errors.ParseError(func.SourceSpan.GetValueOrDefault(), "Function definition missing ThisProperty block");
                            throw new DocumentException();
                        }
                        properties.Add(GetPropertyEntry(state, errors, funcName, thisPropertyBlock.Default.Expression));

                        foreach (var arg in func.Metadata)
                        {
                            if (arg.Identifier == PAConstants.ThisPropertyIdentifier)
                                continue;

                            properties.Add(GetPropertyEntry(state, errors, funcName + "_" + arg.Identifier, arg.Default.Expression));
                        }

                        RepopulateTemplateCustomProperties(func, templateState, errors);
                    }
                }
                else if (template.CustomProperties?.Any(prop => prop.IsFunctionProperty) ?? false)
                {
                    // For component uses, recreate the dummy props for function parameters
                    foreach (var hiddenScopeRule in template.CustomProperties.Where(prop => prop.IsFunctionProperty).SelectMany(prop => prop.PropertyScopeKey.PropertyScopeRulesKey))
                    {
                        properties.Add(GetPropertyEntry(state, errors, hiddenScopeRule.Name, hiddenScopeRule.ScopeVariableInfo.DefaultRule));
                    }
                }                

                // Preserve ordering from serialized IR
                // Required for roundtrip checks
                properties = properties.OrderBy(prop => state.Properties.Select(propState => propState.PropertyName).ToList().IndexOf(prop.Property)).ToList();
                resultControlInfo = new ControlInfoJson.Item()
                {
                    Parent = parent,
                    Name = controlName,
                    ControlUniqueId = uniqueId.ToString(),
                    PublishOrderIndex = state.PublishOrderIndex,
                    VariantName = variantName ?? string.Empty,
                    Rules = properties.ToArray(),
                    StyleName = state.StyleName,
                    ExtensionData = state.ExtensionData,
                };

                if (state.IsComponentDefinition ?? false)
                {
                    templateState.ComponentDefinitionInfo = new ComponentDefinitionInfoJson(resultControlInfo, template.LastModifiedTimestamp, orderedChildren);
                    template = templateState.ToControlInfoTemplate();
                    template.IsComponentDefinition = true;
                    template.ComponentDefinitionInfo = null;
                }
                else
                {
                    template.IsComponentDefinition = state.IsComponentDefinition;
                }
            }
            else
            {
                state = null;
                resultControlInfo = ControlInfoJson.Item.CreateDefaultControl();
                resultControlInfo.Name = controlName;
                resultControlInfo.ControlUniqueId = uniqueId.ToString();
                resultControlInfo.Parent = parent;
                resultControlInfo.VariantName = variantName ?? string.Empty;
                resultControlInfo.StyleName = $"default{templateName.FirstCharToUpper()}Style";
                var properties = new List<ControlInfoJson.RuleEntry>();
                foreach (var propIR in blockNode.Properties)
                {
                    properties.Add(CombinePropertyIRAndState(propIR, errors));
                }
                resultControlInfo.Rules = properties.ToArray();
            }
            resultControlInfo.Template = template;
            resultControlInfo.Children = orderedChildren;

            return (resultControlInfo, state?.ParentIndex ?? -1);
        }

        private static void RepopulateTemplateCustomProperties(FunctionNode func, CombinedTemplateState templateState, ErrorContainer errors)
        {
            var funcName = func.Identifier;
            var customProp = templateState.CustomProperties.FirstOrDefault(prop => prop.Name == funcName);
            if (customProp == default)
            {
                errors.ParseError(func.SourceSpan.GetValueOrDefault(), "Functions are not yet supported without corresponding custom properties in ControlTemplates.json");
                throw new DocumentException();
            }

            var scopeArgs = customProp.PropertyScopeKey.PropertyScopeRulesKey.ToDictionary(scope => scope.Name);
            var argTypes = func.Args.ToDictionary(arg => arg.Identifier, arg => arg.Kind.TypeName);

            int i = 1;
            foreach (var arg in func.Metadata)
            {
                if (arg.Identifier == PAConstants.ThisPropertyIdentifier)
                    continue;

                var defaultRule = arg.Default.Expression;
                var propertyName = funcName + "_" + arg.Identifier;

                if (!scopeArgs.TryGetValue(propertyName, out var propScopeRule))
                {
                    errors.ParseError(func.SourceSpan.GetValueOrDefault(), "Functions are not yet supported without corresponding custom properties in ControlTemplates.json");
                    throw new DocumentException();
                }
                if (!argTypes.TryGetValue(arg.Identifier, out var propType) || !Enum.TryParse<PropertyDataType>(propType, out var propTypeEnum))
                {
                    errors.ParseError(func.SourceSpan.GetValueOrDefault(), "Function metadata blocks must correspond to a function parameter with a valid type");
                    throw new DocumentException();
                }

                propScopeRule.ScopeVariableInfo.DefaultRule = defaultRule;
                propScopeRule.ScopeVariableInfo.ParameterIndex = i;
                propScopeRule.ScopeVariableInfo.ParentPropertyName = funcName;
                propScopeRule.ScopeVariableInfo.ScopePropertyDataType = (int)propTypeEnum;

                ++i;
            }
        }

        private static ControlInfoJson.RuleEntry CombinePropertyIRAndState(PropertyNode node, ErrorContainer errors, ControlState state = null)
        {
            var propName = node.Identifier;
            var expression = node.Expression.Expression;

            if (state == null)
            {
                var property = new ControlInfoJson.RuleEntry();
                property.Property = propName;
                property.InvariantScript = expression;
                property.RuleProviderType = "Unknown";
                return property;
            }
            else
            {
                var property = GetPropertyEntry(state, errors, propName, expression);
                return property;
            }
        }

        private static ControlInfoJson.RuleEntry GetPropertyEntry(ControlState state, ErrorContainer errors, string propName, string expression)
        {
            var property = new ControlInfoJson.RuleEntry();
            property.Property = propName;
            property.InvariantScript = expression;

            PropertyState propState = null;
            if (state.Properties.ToDictionary(prop => prop.PropertyName).TryGetValue(propName, out propState))
            {
                property.ExtensionData = propState.ExtensionData;
                property.NameMap = propState.NameMap;
                property.RuleProviderType = propState.RuleProviderType;
            }
            else
            {
                if (state.IsComponentDefinition ?? false)
                {
                    errors.UnsupportedError("This tool currently does not support adding new custom properties to components. Please use Power Apps Studio to edit component definitions");
                    throw new DocumentException();
                }

                property.RuleProviderType = "Unknown";
            }

            return property;
        }
    }
}
