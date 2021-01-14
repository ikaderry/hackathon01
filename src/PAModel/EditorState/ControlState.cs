using Microsoft.PowerPlatform.Formulas.Tools.EditorState;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Microsoft.PowerPlatform.Formulas.Tools.EditorState
{
    /// <summary>
    /// Per control, this is the studio state content that doesn't wind up in the IR
    /// Similar to <seealso cref="ControlInfoJson.Item"/> without the info encoded by .pa
    /// </summary>
    internal class ControlState
    {
        public string Name { get; set; }
        public double PublishOrderIndex { get; set; }

        [JsonIgnore]
        public string TopParentName { get; set; }

        // These are properties with namemaps/info beyond the ones present in the control template
        // Key is property name
        public List<PropertyState> Properties { get; set; }

        // Doesn't get written to .msapp
        // Represents the index at which this property appears in it's parent's children list
        public int ParentIndex { get; set; } = -1;

        // For matching up within a Theme.
        public string StyleName { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object> ExtensionData { get; set; }

        // Not sure if there's a better way of representing this
        // For galleries, we need to persist the galleryTemplate control name as a child of this
        // to properly pair up the studio state for roundtripping
        // This isn't needed otherwise, if we weren't worried about exact round-tripping we could recreate the control with a different name
        public string GalleryTemplateChildName { get; set; } = null;

        public bool? IsComponentDefinition { get; set; }
    }
}
