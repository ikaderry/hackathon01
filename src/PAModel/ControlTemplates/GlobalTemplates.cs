// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AppMagic.Authoring.Persistence;
using Microsoft.PowerPlatform.Formulas.Tools.EditorState;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerPlatform.Formulas.Tools.ControlTemplates
{
    internal class GlobalTemplates
    {
        public static void AddCodeOnlyTemplates(TemplateStore templateStore, Dictionary<string, ControlTemplate> templates, AppType type)
        {
            templates.Add("appinfo", CreateAppInfoTemplate(type));
            if (!templateStore.TryGetTemplate("appinfo", out _))
            {
                templateStore.AddTemplate("appinfo", new CombinedTemplateState()
                {
                    Id = "http://microsoft.com/appmagic/appinfo",
                    Name = "appinfo",
                    Version = "1.0",
                    LastModifiedTimestamp = "0",
                    IsComponentTemplate = false,
                    FirstParty = true,
                });
            }

            templates.Add("screen", CreateScreenTemplate(type));
            if (!templateStore.TryGetTemplate("screen", out _))
            {
                templateStore.AddTemplate("screen", new CombinedTemplateState()
                {
                    Id = "http://microsoft.com/appmagic/screen",
                    Name = "screen",
                    Version = "1.0",
                    LastModifiedTimestamp = "0",
                    IsComponentTemplate = false,
                    FirstParty = true,
                });
            }
        }

        private static ControlTemplate CreateAppInfoTemplate(AppType type)
        {
            var template = new ControlTemplate("appinfo", "1.0", "http://microsoft.com/appmagic/appinfo");
            template.InputDefaults.Add("MinScreenHeight", type == AppType.Phone ? "640" : "320");
            template.InputDefaults.Add("MinScreenWidth", type == AppType.Phone ? "640" : "320");
            template.InputDefaults.Add("ConfirmExit", "false");
            template.InputDefaults.Add("SizeBreakpoints", type == AppType.Phone ? "[1200, 1800, 2400]" : "[600, 900, 1200]");
            return template;
        }

        private static ControlTemplate CreateScreenTemplate(AppType type)
        {
            var template = new ControlTemplate("screen", "1.0", "http://microsoft.com/appmagic/screen");
            template.InputDefaults.Add("Fill", "RGBA(255, 255, 255, 1)");
            template.InputDefaults.Add("Height", "Max(App.Height, App.MinScreenHeight)");
            template.InputDefaults.Add("Width", "Max(App.Width, App.MinScreenWidth)");
            template.InputDefaults.Add("Size", "1 + CountRows(App.SizeBreakpoints) - CountIf(App.SizeBreakpoints, Value >= Self.Width)");
            template.InputDefaults.Add("Orientation", "If(Self.Width < Self.Height, Layout.Vertical, Layout.Horizontal)");
            template.InputDefaults.Add("LoadingSpinner", "LoadingSpinner.None");
            template.InputDefaults.Add("LoadingSpinnerColor", "RGBA(0, 51, 102, 1)");
            template.InputDefaults.Add("ImagePosition", "ImagePosition.Fit");
            return template;
        }

    }
}
