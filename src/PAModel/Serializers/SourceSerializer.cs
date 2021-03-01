// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AppMagic.Authoring.Persistence;
using Microsoft.PowerPlatform.Formulas.Tools.ControlTemplates;
using Microsoft.PowerPlatform.Formulas.Tools.EditorState;
using Microsoft.PowerPlatform.Formulas.Tools.IR;
using Microsoft.PowerPlatform.Formulas.Tools.Schemas;
using Microsoft.PowerPlatform.Formulas.Tools.SourceTransforms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Microsoft.PowerPlatform.Formulas.Tools
{
    // Read/Write to a source format. 
    internal static partial class SourceSerializer
    {
        // 1 - .pa1 format
        // 2 - intro to .pa.yaml format.
        // 3 - Moved .editorstate.json files under src\EditorState
        // 4 - Moved Assets out of /Other
        // 5 - AppCheckerResult is part of Entropy
        // 6 - ScreenIndex
        // 7 - PublishOrderIndex update
        // 8 - Volatile properties to Entropy
        // 9 - Split Up ControlTemplates, subdivide src/
        // 10 - Datasource, Service defs to /pkg
        // 11 - Split out ComponentReference into its own file
        // 12 - Moved Resources.json, move volatile rootpaths to entropy
        // 13 - Control UniqueIds to Entropy
        // 14 - Yaml DoubleQuote escape
        // 15 - Use dictionary for templates
        // 16 - Group Control transform
        // 17 - Moved PublishOrderIndex entirely to Entropy 
        // 18 - AppChecker result is not part of entropy (See change 0.5 in this list) 
        public static Version CurrentSourceVersion = new Version(0, 18);

        // Layout is:
        //  src\
        //  DataSources\
        //  Other\  (all unrecognized files)         
        public const string CodeDir = "Src";
        public const string AssetsDir = "Assets";
        public const string TestDir = "Src\\Tests";
        public const string EditorStateDir = "Src\\EditorState";
        public const string ComponentCodeDir = "Src\\Components";
        public const string PackagesDir = "pkgs";
        public const string DataSourcePackageDir = "pkgs\\TableDefinitions";
        public const string WadlPackageDir = "pkgs\\Wadl";
        public const string SwaggerPackageDir = "pkgs\\Swagger";
        public const string ComponentPackageDir = "pkgs\\Components";
        public const string OtherDir = "Other";
        public const string EntropyDir = "Entropy";  
        public const string ConnectionDir = "Connections";
        public const string DataSourcesDir = "DataSources";


        internal static readonly string AppTestControlName = "Test_7F478737223C4B69";
        private static readonly string _defaultThemefileName = "Microsoft.PowerPlatform.Formulas.Tools.Themes.DefaultTheme.json";
        private static readonly string _buildVerFileName = "Microsoft.PowerPlatform.Formulas.Tools.Build.BuildVer.json";
        private static BuildVerJson _buildVerJson = GetBuildDetails();

        // Full fidelity read-write

        public static CanvasDocument LoadFromSource(string directory2, ErrorContainer errors)
        {
            if (File.Exists(directory2))
            {
                if (directory2.EndsWith(".msapp", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Must point to a source directory, not an msapp file ({directory2}");
                }
            }

            if (!Directory.Exists(directory2))
            {
                throw new InvalidOperationException($"No directory {directory2}");
            }
            var dir = new DirectoryReader(directory2);
            var app = new CanvasDocument();

            // Do the manifest check (and version check) first. 
            // MAnifest lives in top-level directory. 
            foreach (var file in dir.EnumerateFiles("", "*.json"))
            {
                switch (file.Kind)
                {
                    case FileKind.CanvasManifest:
                        var manifest = file.ToObject<CanvasManifestJson>();

                        if (manifest.FormatVersion != CurrentSourceVersion)
                        {
                            errors.FormatNotSupported($"This tool only supports {CurrentSourceVersion}, the manifest version is {manifest.FormatVersion}");
                            throw new DocumentException();
                        }

                        app._properties = manifest.Properties;
                        app._header = manifest.Header;
                        app._publishInfo = manifest.PublishInfo;
                        app._screenOrder = manifest.ScreenOrder;
                        break;
                    case FileKind.Templates:
                        foreach (var kvp in file.ToObject<Dictionary<string, CombinedTemplateState>>())
                        {
                            app._templateStore.AddTemplate(kvp.Key, kvp.Value);
                        }
                        break;
                    case FileKind.ComponentReferences:
                        var refs = file.ToObject<ComponentDependencyInfo[]>();
                        app._libraryReferences = refs;
                        break;
                }
            }
            if (app._header == null)
            {
                // Manifest not found.
                errors.FormatNotSupported($"Can't find CanvasManifest.json file - is sources an old version?");
                throw new DocumentException();
            }

            // Load template files, recreate References/templates.json
            LoadTemplateFiles(errors, app, Path.Combine(directory2, PackagesDir), out var templateDefaults);

            foreach (var file in dir.EnumerateFiles(AssetsDir))
            {
                if (file._relativeName == "Resources.json")
                {
                    app._resourcesJson = file.ToObject<ResourcesJson>();
                    continue;
                }
                app.AddAssetFile(file.ToFileEntry());
            }

            foreach (var file in dir.EnumerateFiles(EntropyDir))
            {
                switch (file.Kind)
                {
                    case FileKind.Entropy:
                        app._entropy = file.ToObject<Entropy>();
                        break;
                    case FileKind.AppCheckerResult:
                        app._appCheckerResultJson = file.ToObject<AppCheckerResultJson>();
                        break;
                    case FileKind.Checksum:
                        app._checksum = file.ToObject<ChecksumJson>();
                        app._checksum.ClientBuildDetails = _buildVerJson;
                        break;
                    default:
                        errors.GenericWarning($"Unexpected file in Entropy, discarding");
                        break;

                }
            }


            foreach (var file in dir.EnumerateFiles(OtherDir))
            {
                // Special files like Header / Properties 
                switch (file.Kind)
                {
                    case FileKind.Unknown:
                        // Track any unrecognized files so we can save back.
                        app.AddFile(file.ToFileEntry());
                        break;

                    default:
                        // Shouldn't find anything else not unknown in here, but just ignore them for now
                        errors.GenericWarning($"Unexpected file in Other, discarding");
                        break;

                }
            } // each loose file in '\other' 


            app.GetLogoFile();

            LoadDataSources(app, dir, errors);
            LoadSourceFiles(app, dir, templateDefaults, errors);

            foreach (var file in dir.EnumerateFiles(ConnectionDir))
            {
                // Special files like Header / Properties 
                switch (file.Kind)
                {
                    case FileKind.Connections:
                        app._connections = file.ToObject<IDictionary<string, ConnectionJson>>();
                        break;
                }
            }


            // Defaults. 
            // - DynamicTypes.Json, Resources.Json , Templates.Json - could all be empty
            // - Themes.json- default to


            app.OnLoadComplete(errors);

            return app;
        }

        public static CanvasDocument Create(string appName, string packagesPath, IList<string> paFiles, ErrorContainer errors)
        {
            var app = new CanvasDocument();

            app._properties = DocumentPropertiesJson.CreateDefault(appName);
            app._header = HeaderJson.CreateDefault();

            LoadTemplateFiles(errors, app, packagesPath, out var loadedTemplates);
            app._entropy = new Entropy();
            app._checksum = new ChecksumJson() { ClientStampedChecksum = "Foo", ClientBuildDetails = _buildVerJson };

            AddDefaultTheme(app);

            CreateControls(app, paFiles, loadedTemplates, errors);

            return app;
        }


        private static void LoadTemplateFiles(ErrorContainer errors, CanvasDocument app, string packagesPath, out Dictionary<string, ControlTemplate> loadedTemplates)
        {
            loadedTemplates = new Dictionary<string, ControlTemplate>();
            var templateList = new List<TemplatesJson.TemplateJson>();
            foreach (var file in new DirectoryReader(packagesPath).EnumerateFiles(string.Empty, "*.xml", searchSubdirectories: false)) {
                var xmlContents = file.GetContents();
                if (!ControlTemplateParser.TryParseTemplate(new TemplateStore(), xmlContents, app._properties.DocumentAppType, loadedTemplates, out var parsedTemplate, out var templateName))
                {
                    errors.GenericError($"Unable to parse template file {file._relativeName}");
                    throw new DocumentException();
                }
                // Some control templates specify a name with an initial capital letter (e.g. rating control)
                // However, the server doesn't always use that. If the template name doesn't match the one we wrote
                // as the file name, adjust the template name to lowercase
                if (!file._relativeName.StartsWith(templateName))
                {
                    templateName = templateName.ToLower();
                }

                templateList.Add(new TemplatesJson.TemplateJson() { Name = templateName, Template = xmlContents, Version = parsedTemplate.Version });
            }

            // Also add Screen and App templates (not xml, constructed in code on the server)
            GlobalTemplates.AddCodeOnlyTemplates(new TemplateStore(), loadedTemplates, app._properties.DocumentAppType);

            app._templates = new TemplatesJson() { UsedTemplates = templateList.ToArray() };
        }

        // The publish info points to the logo file. Grab it from the unknowns. 
        private static void GetLogoFile(this CanvasDocument app)
        {
            // Logo file. 
            if (!string.IsNullOrEmpty(app._publishInfo?.LogoFileName))
            {
                string key = app._publishInfo.LogoFileName;
                FileEntry logoFile;
                if (app._assetFiles.TryGetValue(key, out logoFile))
                {
                    app._unknownFiles.Remove(key);
                    app._logoFile = logoFile;
                }
                else
                {
                    throw new InvalidOperationException($"Missing logo file {key}");
                }
            }
        }

        private static void LoadSourceFiles(CanvasDocument app, DirectoryReader directory, Dictionary<string, ControlTemplate> templateDefaults, ErrorContainer errors)
        {
            foreach (var file in directory.EnumerateFiles(EditorStateDir, "*.json"))
            {
                if (!file._relativeName.EndsWith(".editorstate.json"))
                {
                    errors.FormatNotSupported($"Unexpected file present in {EditorStateDir}");
                    throw new DocumentException();
                }

                // Json peer to a .pa file. 
                var controlExtraData = file.ToObject<Dictionary<string, ControlState>>();
                var topParentName = file._relativeName.Replace(".editorstate.json", "");
                foreach (var control in controlExtraData)
                {
                    control.Value.TopParentName = topParentName;
                    if (!app._editorStateStore.TryAddControl(control.Value))
                    {
                        // Can't have duplicate control names.
                        // This might happen due to a bad merge.
                        errors.EditorStateError(file.SourceSpan, $"Control '{control.Value.Name}' is already defined.");
                    }
                }                
            }

            // For now, the Themes file lives in CodeDir as a json file
            // We'd like to make this .pa.yaml as well eventually
            foreach (var file in directory.EnumerateFiles(CodeDir, "*.json", searchSubdirectories: false))
            {
                if (Path.GetFileName(file._relativeName) == "Themes.json")
                    app._themes = file.ToObject<ThemesJson>();
            }


            foreach (var file in directory.EnumerateFiles(CodeDir, "*.pa.yaml", searchSubdirectories: false))
            {
                AddControl(app, file._relativeName, false, file.GetContents(), errors);
            }

            foreach (var file in EnumerateComponentDirs(directory, "*.pa.yaml"))
            {
                AddControl(app, file._relativeName, true, file.GetContents(), errors);

            }

            foreach (var file in directory.EnumerateFiles(TestDir, "*.pa.yaml"))
            {
                AddControl(app, file._relativeName, false, file.GetContents(), errors);
            }

            foreach (var file in EnumerateComponentDirs(directory, "*.json"))
            {
                var componentTemplate = file.ToObject<CombinedTemplateState>();
                app._templateStore.AddTemplate(componentTemplate.ComponentManifest.Name, componentTemplate);
            }
        }

        private static IEnumerable<DirectoryReader.Entry> EnumerateComponentDirs(
            DirectoryReader directory, string pattern)
        {
            return directory.EnumerateFiles(ComponentCodeDir, pattern).Concat(
                directory.EnumerateFiles(ComponentPackageDir, pattern));
        }

        private static void CreateControls(CanvasDocument app, IList<string> paFiles, Dictionary<string, ControlTemplate> templateDefaults, ErrorContainer errors)
        {
            foreach (var file in paFiles)
            {
                var fileEntry = new DirectoryReader.Entry(file);

                AddControl(app, file, false, fileEntry.GetContents(), errors);
            }
        }

        private static void AddControl(CanvasDocument app, string filePath, bool isComponent, string fileContents, ErrorContainer errors)
        {
            var filename = Path.GetFileName(filePath);
            try
            {
                var parser = new Parser.Parser(filePath, fileContents, errors);
                var controlIR = parser.ParseControl();
                if (controlIR == null)
                {
                    return; // error condition
                }

                var collection = (isComponent) ? app._components : app._screens;
                collection.Add(controlIR.Name.Identifier, controlIR);
            }
            catch (DocumentException)
            {
                // On DocumentException, continue looking for errors in other files. 
            }
        }

        public static Dictionary<string, ControlTemplate> ReadTemplates(TemplatesJson templates)
        {
            throw new NotImplementedException();   
        }

        // Write out to a directory (this shards it) 
        public static void SaveAsSource(CanvasDocument app, string directory2, ErrorContainer errors)
        { 
            var dir = new DirectoryWriter(directory2);
            dir.DeleteAllSubdirs();

            // Shard templates, parse for default values
            var templateDefaults = new Dictionary<string, ControlTemplate>();
            foreach (var template in app._templates.UsedTemplates)
            {
                var filename = $"{template.Name}_{template.Version}.xml";
                dir.WriteAllXML(PackagesDir, filename, template.Template);
                if (!ControlTemplateParser.TryParseTemplate(app._templateStore, template.Template, app._properties.DocumentAppType, templateDefaults, out _, out _))
                    throw new NotSupportedException($"Unable to parse template file {template.Name}");
            }

            // Also add Screen and App templates (not xml, constructed in code on the server)
            GlobalTemplates.AddCodeOnlyTemplates(app._templateStore, templateDefaults, app._properties.DocumentAppType);

            var importedComponents = app.GetImportedComponents();

            foreach (var control in app._screens)
            {
                string controlName = control.Key;
                var isTest = controlName == AppTestControlName;
                var subDir = isTest ? TestDir : CodeDir;

                WriteTopParent(dir, app, control.Key, control.Value, subDir);
            }

            foreach (var control in app._components)
            {
                string controlName = control.Key;
                app._templateStore.TryGetTemplate(controlName, out var templateState);

                bool isImported = importedComponents.Contains(templateState.TemplateOriginalName);
                var subDir = (isImported) ? ComponentPackageDir : ComponentCodeDir;
                WriteTopParent(dir, app, control.Key, control.Value, subDir);
            }

            // Write out control templates at top level, skipping component templates which are written alongside components
            var nonComponentControlTemplates = app._templateStore.Contents.Where(kvp => !(kvp.Value.IsComponentTemplate ?? false)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            dir.WriteAllJson("", "ControlTemplates.json", nonComponentControlTemplates);

            if (app._checksum != null)
            {
                app._checksum.ClientBuildDetails = _buildVerJson;
                dir.WriteAllJson(EntropyDir, FileKind.Checksum, app._checksum);
            }

            if (app._appCheckerResultJson != null)
            {
                dir.WriteAllJson(EntropyDir, FileKind.AppCheckerResult, app._appCheckerResultJson);
            }

            foreach (var file in app._assetFiles.Values)
            {
                dir.WriteAllBytes(AssetsDir, file.Name, file.RawBytes);
            }

            if (app._logoFile != null)
            {
                dir.WriteAllBytes(AssetsDir, app._logoFile.Name, app._logoFile.RawBytes);
            }

            if (app._themes != null)
            {
                dir.WriteAllJson(CodeDir, "Themes.json", app._themes);
            }

            if (app._resourcesJson != null)
            {
                dir.WriteAllJson(AssetsDir, "Resources.json", app._resourcesJson);
            }

            WriteDataSources(dir, app, errors);

            // Loose files. 
            foreach (FileEntry file in app._unknownFiles.Values)
            {
                // Standardize the .json files so they're determinsitc and comparable
                if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlyMemory<byte> span = file.RawBytes;
                    var je = JsonDocument.Parse(span).RootElement;
                    var jsonStr = JsonNormalizer.Normalize(je);
                    dir.WriteAllText(OtherDir, file.Name, jsonStr); 
                }
                else
                {
                    dir.WriteAllBytes(OtherDir, file.Name, file.RawBytes);
                }
            }

            var manifest = new CanvasManifestJson
            {
                FormatVersion =  CurrentSourceVersion,
                Properties = app._properties,
                Header = app._header,
                PublishInfo = app._publishInfo,
                ScreenOrder = app._screenOrder
            };
            dir.WriteAllJson("", FileKind.CanvasManifest, manifest);

            if (app._connections != null)
            {
                dir.WriteAllJson(ConnectionDir, FileKind.Connections, app._connections);
            }

            if (app._libraryReferences != null)
            {
                dir.WriteAllJson("", FileKind.ComponentReferences, app._libraryReferences);
            }

            dir.WriteAllJson(EntropyDir, FileKind.Entropy, app._entropy);
        }

        private static void WriteDataSources(DirectoryWriter dir, CanvasDocument app, ErrorContainer errors)
        {
            // Data Sources  - write out each individual source. 
            HashSet<string> filenames = new HashSet<string>();
            foreach (var kvp in app.GetDataSources())
            {
                // Filename doesn't actually matter, but careful to avoid collisions and overwriting. 
                // Also be determinstic. 
                string filename = kvp.Key + ".json";

                if (!filenames.Add(filename.ToLower()))
                {
                    int index = 1;
                    var altFileName = kvp.Key + "_" + index + ".json";
                    while (!filenames.Add(altFileName.ToLower()))
                        ++index;

                    errors.GenericWarning("Data source name collision: " + filename + ", writing as " + altFileName + " to avoid.");
                    filename = altFileName;
                }
                var dataSourceStateToWrite = kvp.Value.JsonClone().OrderBy(ds => ds.Name, StringComparer.Ordinal);
                DataSourceDefinition dataSourceDef = null;

                // Split out the changeable parts of the data source.
                foreach (var ds in dataSourceStateToWrite.Where(ds => ds.Type != "ViewInfo"))
                {
                    // CDS DataSource
                    if (ds.TableDefinition != null)
                    {
                        dataSourceDef = new DataSourceDefinition();
                        dataSourceDef.TableDefinition = Utility.JsonParse<DataSourceTableDefinition>(ds.TableDefinition);
                        dataSourceDef.DatasetName = ds.DatasetName;
                        dataSourceDef.EntityName = ds.RelatedEntityName ?? ds.Name;
                        ds.DatasetName = null;
                        ds.TableDefinition = null;
                    }
                    // CDP DataSource
                    else if (ds.DataEntityMetadataJson != null)
                    {
                        if (ds.ApiId == "/providers/microsoft.powerapps/apis/shared_commondataservice")
                        {
                            // This is the old CDS connector, we can't support it since it's optionset format is incompatable with the newer one
                            errors.ValidationError($"Connection {ds.Name} is using the old CDS connector which is incompatable with this tool");
                            throw new DocumentException();
                        }
                        dataSourceDef = new DataSourceDefinition();
                        dataSourceDef.DataEntityMetadataJson = ds.DataEntityMetadataJson;
                        dataSourceDef.EntityName = ds.Name;
                        dataSourceDef.TableName = ds.TableName;
                        ds.TableName = null;
                        ds.DataEntityMetadataJson = null;
                    }
                    else if (ds.Type == "OptionSetInfo")
                    {
                        // This looks like a left over from previous versions of studio, account for it by
                        // tracking optionsets with empty dataset names
                        ds.DatasetName = ds.DatasetName == null ? string.Empty : null;
                    }
                    else if (ds.WadlMetadata != null)
                    {
                        // For some reason some connectors have both, investigate if one could be discarded by the server?
                        if (ds.WadlMetadata.WadlXml != null)
                        {
                            dir.WriteAllXML(WadlPackageDir, filename.Replace(".json", ".xml"), ds.WadlMetadata.WadlXml);
                        }
                        if (ds.WadlMetadata.SwaggerJson != null)
                        {
                            dir.WriteAllJson(SwaggerPackageDir, filename, JsonSerializer.Deserialize<SwaggerDefinition>(ds.WadlMetadata.SwaggerJson, Utility._jsonOpts));
                        }
                        ds.WadlMetadata = null;
                    }
                }

                if (dataSourceDef != null)
                {
                    TrimViewNames(dataSourceStateToWrite, dataSourceDef.DatasetName);
                }

                if (dataSourceDef?.DatasetName != null && app._dataSourceReferences.TryGetValue(dataSourceDef.DatasetName, out var referenceJson))
                {
                    // copy over the localconnectionreference
                    if (referenceJson.dataSources.TryGetValue(dataSourceDef.EntityName, out var dsRef))
                    {
                        dataSourceDef.LocalReferenceDSJson = dsRef;
                    }
                    dataSourceDef.InstanceUrl = referenceJson.instanceUrl;
                    dataSourceDef.ExtensionData = referenceJson.ExtensionData;
                }

                if (dataSourceDef != null)
                    dir.WriteAllJson(DataSourcePackageDir, filename, dataSourceDef);

                dir.WriteAllJson(DataSourcesDir, filename, dataSourceStateToWrite);
            }
        }

        private static void LoadDataSources(CanvasDocument app, DirectoryReader directory, ErrorContainer errors)
        {
            var tableDefs = new Dictionary<string, DataSourceDefinition>();
            app._dataSourceReferences = new Dictionary<string, LocalDatabaseReferenceJson>();

            foreach (var file in directory.EnumerateFiles(DataSourcePackageDir, "*.json"))
            {
                var tableDef = file.ToObject<DataSourceDefinition>();
                tableDefs.Add(tableDef.EntityName, tableDef);
                if (tableDef.DatasetName == null)
                    continue;

                if (!app._dataSourceReferences.TryGetValue(tableDef.DatasetName, out var localDatabaseReferenceJson))
                {
                    localDatabaseReferenceJson = new LocalDatabaseReferenceJson()
                    {
                        dataSources = new Dictionary<string, LocalDatabaseReferenceDataSource>(),
                        ExtensionData = tableDef.ExtensionData,
                        instanceUrl = tableDef.InstanceUrl
                    };
                    app._dataSourceReferences.Add(tableDef.DatasetName, localDatabaseReferenceJson);
                }
                if (localDatabaseReferenceJson.instanceUrl != tableDef.InstanceUrl)
                {
                    // Generate an error, dataset defs have diverged in a way that shouldn't be possible
                    // Each dataset has one instanceurl
                    errors.ValidationError($"For file {file._relativeName}, the dataset {tableDef.DatasetName} has multiple instanceurls");
                    throw new DocumentException();
                }

                localDatabaseReferenceJson.dataSources.Add(tableDef.EntityName, tableDef.LocalReferenceDSJson);
            }

            // key is filename, value is stringified xml
            var xmlDefs = new Dictionary<string, string>();
            foreach (var file in directory.EnumerateFiles(WadlPackageDir, "*.xml"))
            {
                xmlDefs.Add(Path.GetFileNameWithoutExtension(file._relativeName), file.GetContents());
            }

            // key is filename, value is stringified json
            var swaggerDefs = new Dictionary<string, string>();
            foreach (var file in directory.EnumerateFiles(SwaggerPackageDir, "*.json"))
            {
                swaggerDefs.Add(Path.GetFileNameWithoutExtension(file._relativeName), file.GetContents());
            }

            foreach (var file in directory.EnumerateFiles(DataSourcesDir, "*"))
            {
                var dataSources = file.ToObject<List<DataSourceEntry>>();
                foreach (var ds in dataSources)
                {
                    if (tableDefs.TryGetValue(ds.RelatedEntityName ?? ds.Name, out var definition))
                    {
                        switch (ds.Type)
                        {
                            case "NativeCDSDataSourceInfo":
                                ds.DatasetName = definition.DatasetName;
                                ds.TableDefinition = JsonSerializer.Serialize(definition.TableDefinition, Utility._jsonOpts);
                                break;
                            case "ConnectedDataSourceInfo":
                                ds.DataEntityMetadataJson = definition.DataEntityMetadataJson;
                                ds.TableName = definition.TableName;
                                break;
                            case "OptionSetInfo":
                                ds.DatasetName = ds.DatasetName != string.Empty? definition.DatasetName : null;
                                break;
                            case "ViewInfo":
                                if (definition != null)
                                {
                                    RestoreViewName(ds, definition.DatasetName);
                                }
                                break;
                            case "ServiceInfo":
                            default:
                                break;
                        }
                    }
                    else if (ds.Type == "ServiceInfo")
                    {
                        var foundXML = xmlDefs.TryGetValue(Path.GetFileNameWithoutExtension(file._relativeName), out string xmlDef);
                        var foundJson = swaggerDefs.TryGetValue(Path.GetFileNameWithoutExtension(file._relativeName), out string swaggerDef);
                        
                        if (foundXML || foundJson)
                        {
                            ds.WadlMetadata = new WadlDefinition() { WadlXml = xmlDef, SwaggerJson = swaggerDef };
                        }
                    }

                    app.AddDataSourceForLoad(ds);
                }
            }
        }

        // CDS View entities have Names that start with the environment guid (datasetname)
        // This trims that from the start of the name so that all the environment-specific info
        // can be moved to the /pkg directory 
        private static void TrimViewNames(IEnumerable<DataSourceEntry> dataSourceEntries, string dataSetName)
        {
            foreach (var ds in dataSourceEntries.Where(ds => ds.Type == "ViewInfo"))
            {
                if (ds.Name.StartsWith(dataSetName))
                {
                    ds.Name = ds.Name.Substring(dataSetName.Length);
                    ds.TrimmedViewName = true;
                }
            }
        }

        // Inverse of TrimViewNames() above
        // If the name was trimmed on unpack, this reconstructs it using the
        // dataset name corresponding to the base table for the view
        private static void RestoreViewName(DataSourceEntry ds, string dataSetName)
        {
            if (ds.TrimmedViewName ?? false)
            {
                ds.Name = dataSetName + ds.Name;
                ds.TrimmedViewName = null;
            }
        }

        /// This writes out the IR, editor state cache, and potentially component templates
        /// for a single top level control, such as the App object, a screen, or component
        /// Name refers to the control name
        private static void WriteTopParent(
            DirectoryWriter dir,
            CanvasDocument app,
            string name,
            BlockNode ir,
            string subDir)
        {
            var controlName = name;
            var text = PAWriterVisitor.PrettyPrint(ir);

            string filename = controlName + ".pa.yaml";

            
            dir.WriteAllText(subDir, filename, text);

            var extraData = new Dictionary<string, ControlState>();
            foreach (var item in app._editorStateStore.GetControlsWithTopParent(controlName))
            {
                extraData.Add(item.Name, item);
            }

            // Write out of all the other state for roundtripping 
            string extraContent = controlName + ".editorstate.json";
            dir.WriteAllJson(EditorStateDir, extraContent, extraData);

            // Write out component templates next to the component
            if (app._templateStore.TryGetTemplate(name, out var templateState))
            {
                dir.WriteAllJson(subDir, controlName + ".json", templateState);
            }
        }

        private static void AddDefaultTheme(CanvasDocument app)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(_defaultThemefileName);
            using var reader = new StreamReader(stream);

            var jsonString = reader.ReadToEnd();

            app._themes = JsonSerializer.Deserialize<ThemesJson>(jsonString, Utility._jsonOpts);
        }

        private static BuildVerJson GetBuildDetails()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(_buildVerFileName);
                if (stream == null)
                {
                    return null;
                }
                using var reader = new StreamReader(stream);
                var jsonString = reader.ReadToEnd();

                return JsonSerializer.Deserialize<BuildVerJson>(jsonString, Utility._jsonOpts);
            }
            catch (Exception)
            {
                return null;
            }
        }

    }
}
