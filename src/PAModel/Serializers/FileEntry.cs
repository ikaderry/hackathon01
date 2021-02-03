// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;


namespace Microsoft.PowerPlatform.Formulas.Tools
{
    // Raw kinds of files we recognize in the .msapp 
    enum FileKind
    {
        Unknown,

        Properties,
        Header,
        AppCheckerResult,
        Checksum,

        // Used for dataComponents
        ComponentsMetadata,
        DataComponentSources,
        DataComponentTemplates,


        // If this file is present, it's an older format. 
        OldEntityJSon,

        // Resourcs 
        PublishInfo,

        // References 
        DataSources,
        Themes,
        Templates,
        Resources,
        Asset,

        // Category so 
        ControlSrc,
        ComponentSrc,
        TestSrc,

        // Unique to source format. 
        Entropy,
        CanvasManifest,
        Connections,
        ComponentReferences, // ComponentReferences.json
    }

    // Represent a file from disk or a Zip archive. 
    [DebuggerDisplay("{Name}")]
    class FileEntry
    {
        // Name relative to root. Can be triaged to a FileKind
        public string Name;
        
        public byte[] RawBytes;

        public static FileEntry FromFile(string fullPath, string root)
        {
            var relativePath = Utility.GetRelativePath(fullPath, root);
            var bytes = File.ReadAllBytes(fullPath);
            var entry = new FileEntry
            {
                Name = relativePath.Replace('/', '\\'),
                RawBytes = bytes
            };
            return entry;
        }

        public static FileEntry FromZip(ZipArchiveEntry z, string name = null)
        {
            if (name == null)
            {
                name = z.FullName;
                // Some paths mistakenly start with DirectorySepChar in the msapp,
                // We add _ to it when writing so that windows can handle it correctly. 
                if (z.FullName.StartsWith(Path.DirectorySeparatorChar.ToString()))
                    name = FilenameLeadingUnderscore + z.FullName;
            }
            return new FileEntry
            {
                Name = name,
                RawBytes = z.ToBytes()
            };
        }

        public const char FilenameLeadingUnderscore = '_';

        // Map from path in .msapp to type. 
        internal static Dictionary<string, FileKind> _fileKinds = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase)
        {
            {"Entities.json", FileKind.OldEntityJSon },
            {"Properties.json", FileKind.Properties },
            {"Header.json", FileKind.Header},
            {ChecksumMaker.ChecksumName, FileKind.Checksum },
            {"AppCheckerResult.sarif", FileKind.AppCheckerResult },
            {"ComponentsMetadata.json", FileKind.ComponentsMetadata },
            {@"Resources\PublishInfo.json", FileKind.PublishInfo },
            {@"References\DataComponentSources.json", FileKind.DataComponentSources },
            {@"References\DataComponentTemplates.json", FileKind.DataComponentTemplates },
            {@"References\DataSources.json", FileKind.DataSources },
            {@"References\Themes.json", FileKind.Themes },
            {@"References\Templates.json", FileKind.Templates },
            {@"References\Resources.json", FileKind.Resources },

            // Files that only appear in Source
            {"Entropy.json", FileKind.Entropy },
            {"CanvasManifest.json", FileKind.CanvasManifest },
            {"ControlTemplates.json", FileKind.Templates },
            {"Connections.json", FileKind.Connections },
            {"ComponentReferences.json", FileKind.ComponentReferences }
        };


        internal static string GetFilenameForKind(FileKind kind)
        {
            string filename =
                (from kv in _fileKinds
                 where kv.Value == kind
                 select kv.Key).FirstOrDefault();

            return filename;
        }

        internal static FileKind TriageKind(string fullname)
        {
            FileKind kind;
            if (_fileKinds.TryGetValue(fullname, out kind))
            {
                return kind;
            }

            // Source? 
            if (fullname.StartsWith(@"Controls\", StringComparison.OrdinalIgnoreCase))
            {
                return FileKind.ControlSrc;
            }
            if (fullname.StartsWith(@"Components\", StringComparison.OrdinalIgnoreCase))
            {
                return FileKind.ComponentSrc;
            }

            if (fullname.StartsWith(@"AppTests\", StringComparison.OrdinalIgnoreCase))
            {
                return FileKind.TestSrc;
            }

            // Resource 
            if (fullname.StartsWith(@"Assets\", StringComparison.OrdinalIgnoreCase))
            {
                return FileKind.Asset;
            }

            return FileKind.Unknown;
        }
    }
}
