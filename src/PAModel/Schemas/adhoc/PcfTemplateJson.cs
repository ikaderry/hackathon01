using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerPlatform.Formulas.Tools.Schemas.adhoc
{
    // From PowerApps-Client\src\Cloud\DocumentServer.Core\Document\Document\Persistence\Serialization\Schemas\Control\Template\PcfTemplateJson.cs
    internal class PcfTemplateJson
    {
        /// <summary>
        /// The name of the pcf template
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The version of this pcf template
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The conversion information for aligning different version templates to this version
        /// </summary>      
        public ManifestControlConversion[] PcfConversions { get; set; }
    }

    // From PowerApps-Client\src\Cloud\PCFManifests\PowerAppsControlManifest.cs
    internal class ManifestControlConversion
    {
        public string From { get; set; }
        public string To { get; set; }
        public ManifestControlConversionAction[] Action { get; set; }
    }

    // From PowerApps-Client\src\Cloud\PCFManifests\PowerAppsControlManifest.cs
    internal class ManifestControlConversionAction
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string NewName { get; set; }
    }
}
