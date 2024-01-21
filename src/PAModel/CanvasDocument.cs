// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AppMagic.Authoring.Persistence;
using Microsoft.PowerPlatform.Formulas.Tools.ControlTemplates;
using Microsoft.PowerPlatform.Formulas.Tools.EditorState;
using Microsoft.PowerPlatform.Formulas.Tools.Extensions;
using Microsoft.PowerPlatform.Formulas.Tools.IO;
using Microsoft.PowerPlatform.Formulas.Tools.IR;
using Microsoft.PowerPlatform.Formulas.Tools.Schemas;
using Microsoft.PowerPlatform.Formulas.Tools.Schemas.PcfControl;
using Microsoft.PowerPlatform.Formulas.Tools.SourceTransforms;
using System.IO;
using System.Linq;

namespace Microsoft.PowerPlatform.Formulas.Tools;

/// <summary>
/// Represents a PowerApps document.  This can be save/loaded from a MsApp or Source representation. 
/// This is a full in-memory representation of the msapp file. 
/// </summary>
public class CanvasDocument
{
    /// <summary>
    /// Current source format version. 
    /// </summary>
    public static Version CurrentSourceVersion => SourceSerializer.CurrentSourceVersion;

    // Rules for CanvasDocument
    // - Save/Load must faithfully roundtrip an msapp exactly. 
    // - this is an in-memory representation - so it must parse/shard everything on load. 
    // - Save should not mutate any state. 

    // Track all unknown "files". Ensures round-tripping isn't lossy.         
    // Only contains files of FileKind.Unknown
    internal Dictionary<FilePath, FileEntry> _unknownFiles = new();

    // Key is Top Parent Control Name for both _screens and _components
    internal Dictionary<string, BlockNode> _screens = new(StringComparer.Ordinal);
    internal Dictionary<string, BlockNode> _components = new(StringComparer.Ordinal);

    internal EditorStateStore _editorStateStore;
    internal TemplateStore _templateStore;

    // Various data sources        
    // This is references\dataSources.json
    // Also includes entries for DataSources made from a DataComponent
    // Key is parent entity name (datasource name for non cds data sources)
    internal Dictionary<string, List<DataSourceEntry>> _dataSources = new(StringComparer.Ordinal);
    internal List<string> _screenOrder = new();

    internal HeaderJson _header;
    internal DocumentPropertiesJson _properties;
    internal PublishInfoJson _publishInfo;
    internal TemplatesJson _templates;
    internal ThemesJson _themes;
    internal ResourcesJson _resourcesJson;

    // Yaml strings, handled by Canvas without interaction in PALT
    internal string _parameterSchema;
    internal string _customPageInputsMetadata;

    internal AppCheckerResultJson _appCheckerResultJson;
    internal Dictionary<string, PcfControl> _pcfControls = new(StringComparer.OrdinalIgnoreCase);

    // Environment-specific information
    // Extracted from _properties.LocalConnectionReferences
    // Key is a Connection.Id
    internal IDictionary<string, ConnectionJson> _connections;

    // Extracted from _properties.InstrumentationKey
    internal AppInsightsKeyJson _appInsights;

    // Extracted from _properties.LocalDatasourceReferences
    // Key is a dataset name
    internal IDictionary<string, LocalDatabaseReferenceJson> _dataSourceReferences;

    // Extracted from _properties.LibraryDependencies
    // Must preserve server ordering. 
    internal ComponentDependencyInfo[] _libraryReferences;

    internal FileEntry _logoFile;

    // Save for roundtripping.
    internal Entropy _entropy = new();

    // Checksum from existing msapp. 
    internal ChecksumJson _checksum;

    // Track all asset files, key is file name
    internal Dictionary<FilePath, FileEntry> _assetFiles = new();

    internal UniqueIdRestorer _idRestorer;

    // Tracks duplicate asset file information. When a name collision happens we generate a new name for the duplicate asset file.
    // This dictionary stores the metadata information for that file - like OriginalName, NewFileName, Path...
    // Key is a (case-insensitive) new fileName of the resource.
    // Reason for using FileName of the resource as the key is to avoid name collision across different types eg. Images/close.png, Videos/close.mp4.
    internal Dictionary<string, LocalAssetInfoJson> _localAssetInfoJson = new();
    internal static string AssetFilePathPrefix = @"Assets\";

    #region Save/Load

    /// <summary>
    /// Load an .msapp file for a Canvas Document. 
    /// </summary>
    /// <param name="fullPathToMsApp">path to an .msapp file</param>
    /// <returns>A tuple of the document and errors and warnings. If there are errors, the document is null.  </returns>
    public static (CanvasDocument, ErrorContainer) LoadFromMsapp(string fullPathToMsApp)
    {
        var errors = new ErrorContainer();

        FilePath.EnsurePathRooted(fullPathToMsApp);

        if (!fullPathToMsApp.EndsWith(".msapp", StringComparison.OrdinalIgnoreCase))
        {
            errors.BadParameter("Only works for .msapp files");
        }

        FilePath.VerifyFileExists(errors, fullPathToMsApp);
        if (errors.HasErrors)
        {
            return (null, errors);
        }

        using (var stream = new FileStream(fullPathToMsApp, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var doc = Wrapper(() => MsAppSerializer.Load(stream, errors), errors);
            return (doc, errors);
        }
    }

    public static (CanvasDocument, ErrorContainer) LoadFromMsapp(Stream streamToMsapp)
    {
        var errors = new ErrorContainer();
        var doc = Wrapper(() => MsAppSerializer.Load(streamToMsapp, errors), errors);
        return (doc, errors);
    }

    public static (CanvasDocument, ErrorContainer) LoadFromSources(string pathToSourceDirectory)
    {
        FilePath.EnsurePathRooted(pathToSourceDirectory);

        var errors = new ErrorContainer();
        var doc = Wrapper(() => SourceSerializer.LoadFromSource(pathToSourceDirectory, errors), errors);
        return (doc, errors);
    }

    public ErrorContainer SaveToMsApp(string fullPathToMsApp)
    {
        FilePath.EnsurePathRooted(fullPathToMsApp);

        var errors = new ErrorContainer();
        Wrapper(() => MsAppSerializer.SaveAsMsApp(this, fullPathToMsApp, errors), errors);
        return errors;
    }

    // Used to validate roundtrip after unpack
    internal ErrorContainer SaveToMsAppValidation(string fullPathToMsApp)
    {
        FilePath.EnsurePathRooted(fullPathToMsApp);

        var errors = new ErrorContainer();
        Wrapper(() => MsAppSerializer.SaveAsMsApp(this, fullPathToMsApp, errors, isValidation: true), errors);
        return errors;
    }

    /// <summary>
    /// Save the document in a textual source format that can be checked into source control
    /// </summary>
    /// <param name="pathToSourceDirectory"></param>
    /// <param name="verifyOriginalPath">true if we should immediately repack the sources to verify they successfully roundtrip. </param>
    /// <returns></returns>
    public ErrorContainer SaveToSources(string pathToSourceDirectory, string verifyOriginalPath = null)
    {
        FilePath.EnsurePathRooted(pathToSourceDirectory);

        var errors = new ErrorContainer();
        Wrapper(() => SourceSerializer.SaveAsSource(this, pathToSourceDirectory, errors), errors);


        // Test that we can repack
        if (!errors.HasErrors && verifyOriginalPath != null)
        {
            (var msApp2, var errors2) = LoadFromSources(pathToSourceDirectory);
            if (errors2.HasErrors)
            {
                errors2.PostUnpackValidationFailed();
                return errors2;
            }

            using (var temp = new TempFile())
            {
                errors2 = msApp2.SaveToMsAppValidation(temp.FullPath);
                if (errors2.HasErrors)
                {
                    errors2.PostUnpackValidationFailed();
                    return errors2;
                }

                var ok = MsAppTest.Compare(verifyOriginalPath, temp.FullPath, TextWriter.Null, errors2);
                if (!ok)
                {
                    errors2.PostUnpackValidationFailed();
                    return errors2;
                }
            }
        }

        return errors;
    }
    public static (CanvasDocument, ErrorContainer) MakeFromSources(string appName, string packagesPath, IList<string> paFiles)
    {
        var errors = new ErrorContainer();
        var doc = Wrapper(() => SourceSerializer.Create(appName, packagesPath, paFiles, errors), errors);
        return (doc, errors);
    }

    #endregion

    // Wrapper to ensure consistent invariants between loading a document, exception handling, and returning errors. 
    private static CanvasDocument Wrapper(Func<CanvasDocument> worker, ErrorContainer errors)
    {
        try
        {
            var document = worker();
            if (errors.HasErrors)
            {
                return null;
            }
            return document;
        }
        catch (DocumentException e)
        {
            if (!errors.HasErrors)
            {
                // Internal error - something was thrown without adding to the error container.
                // Add at least one error
                errors.InternalError(e);
            }
            return null;
        }
    }

    private static void Wrapper(Action worker, ErrorContainer errors)
    {
        try
        {
            worker();
        }
        catch (DocumentException e)
        {
            if (!errors.HasErrors)
            {
                // Internal error - something was thrown without adding to the error container.
                // Add at least one error
                errors.InternalError(e);
            }
        }
    }

    internal CanvasDocument()
    {
        _editorStateStore = new EditorStateStore();
        _templateStore = new TemplateStore();
    }

    internal CanvasDocument(CanvasDocument other)
    {
        foreach (var kvp in other._unknownFiles)
        {
            _unknownFiles.Add(kvp.Key, new FileEntry(kvp.Value));
        }

        foreach (var kvp in other._assetFiles)
        {
            _assetFiles.Add(kvp.Key, new FileEntry(kvp.Value));
        }

        foreach (var kvp in other._screens)
        {
            _screens.Add(kvp.Key, kvp.Value.Clone());
        }

        foreach (var kvp in other._components)
        {
            _components.Add(kvp.Key, kvp.Value.Clone());
        }

        _editorStateStore = new EditorStateStore(other._editorStateStore);
        _templateStore = new TemplateStore(other._templateStore);

        _dataSources = other._dataSources.JsonClone();
        _screenOrder = new List<string>(other._screenOrder);

        _header = other._header.JsonClone();
        _properties = other._properties.JsonClone();
        _parameterSchema = other._parameterSchema.JsonClone();
        _customPageInputsMetadata = other._customPageInputsMetadata;
        _publishInfo = other._publishInfo.JsonClone();
        _templates = other._templates.JsonClone();
        _themes = other._themes.JsonClone();
        _resourcesJson = other._resourcesJson.JsonClone();
        _appCheckerResultJson = other._appCheckerResultJson.JsonClone();
        _pcfControls = other._pcfControls.JsonClone();

        _appInsights = other._appInsights.JsonClone();

        _connections = other._connections.JsonClone();

        _dataSourceReferences = other._dataSourceReferences.JsonClone();
        _libraryReferences = other._libraryReferences.JsonClone();

        _logoFile = other._logoFile != null ? new FileEntry(other._logoFile) : null;
        _entropy = other._entropy.JsonClone();
        _checksum = other._checksum.JsonClone();

        _idRestorer = new UniqueIdRestorer(_entropy);

        _localAssetInfoJson = other._localAssetInfoJson.JsonClone();
    }

    // iOrder is used to preserve ordering value for round-tripping. 
    internal void AddDataSourceForLoad(DataSourceEntry ds, int? order = null)
    {
        // Key is parent entity name
        var key = ds.RelatedEntityName ?? ds.Name;
        if (!_dataSources.TryGetValue(key, out var list))
        {
            list = new List<DataSourceEntry>();
            _dataSources.Add(key, list);
        }

        list.Add(ds);
        _entropy.Add(ds, order);
    }

    // Key is parent entity name
    internal IEnumerable<KeyValuePair<string, List<DataSourceEntry>>> GetDataSources()
    {
        return _dataSources;
    }

    internal void ApplyAfterMsAppLoadTransforms(ErrorContainer errors)
    {
        // Update volatile document properties
        _entropy.SetProperties(_properties);

        // Shard templates, parse for default values
        var templateDefaults = new Dictionary<string, ControlTemplate>();
        foreach (var template in _templates.UsedTemplates)
        {
            if (!ControlTemplateParser.TryParseTemplate(_templateStore, template.Template, _properties.DocumentAppType, templateDefaults, out _, out _))
            {
                errors.GenericError($"Unable to parse template file {template.Name}");
                throw new DocumentException();
            }
        }

        // Also add Screen and App templates (not xml, constructed in code on the server)
        GlobalTemplates.AddCodeOnlyTemplates(_templateStore, templateDefaults, _properties.DocumentAppType);

        // PCF templates
        if (_pcfControls.Count == 0)
        {
            foreach (var kvp in _templateStore.Contents)
            {
                if (kvp.Value.IsPcfControl && kvp.Value.DynamicControlDefinitionJson != null)
                {
                    _pcfControls.Add(kvp.Key, PcfControl.GetPowerAppsControlFromJson(kvp.Value));
                    kvp.Value.DynamicControlDefinitionJson = null;
                }
            }
        }

        var componentInstanceTransform = new ComponentInstanceTransform(errors);
        var componentDefTransform = new ComponentDefinitionTransform(errors, _templateStore, componentInstanceTransform);

        // Transform component definitions and populate template set of component instances that need updates 
        foreach (var ctrl in _components)
        {
            AddComponentDefaults(ctrl.Value, templateDefaults);
            componentDefTransform.AfterRead(ctrl.Value);
        }

        var transformer = new SourceTransformer(this, errors, templateDefaults, new Theme(_themes), componentInstanceTransform, _editorStateStore, _templateStore, _entropy);

        foreach (var ctrl in _screens.Concat(_components))
        {
            transformer.ApplyAfterRead(ctrl.Value);
        }

        StabilizeAssetFilePaths(errors);

        // Persist the original order of resource entries in Resources.json in the entropy.
        this.PersistOrderingOfResourcesJsonEntries();
    }

    internal void ApplyBeforeMsAppWriteTransforms(ErrorContainer errors)
    {
        // Update volatile document properties
        _entropy.GetProperties(_properties);

        // Shard templates, parse for default values
        var templateDefaults = new Dictionary<string, ControlTemplate>();
        foreach (var template in _templates.UsedTemplates)
        {
            if (!ControlTemplateParser.TryParseTemplate(_templateStore, template.Template, _properties.DocumentAppType, templateDefaults, out _, out _))
            {
                errors.GenericError($"Unable to parse template file {template.Name}");
                throw new DocumentException();
            }
        }

        // Also add Screen and App templates (not xml, constructed in code on the server)
        GlobalTemplates.AddCodeOnlyTemplates(_templateStore, templateDefaults, _properties.DocumentAppType);

        // Generate DynamicControlDefinitionJson for power apps controls
        foreach (var kvp in _pcfControls)
        {
            if (_templateStore.TryGetTemplate(kvp.Key, out var template))
            {
                template.DynamicControlDefinitionJson = PcfControl.GenerateDynamicControlDefinition(kvp.Value);
            }
            else
            {
                // Validation for accidental deletion of ocf control templates.
                errors.ValidationWarning($"Could not find Pcf Control Template with name: {kvp.Key} in pkgs/PcfControlTemplates directory. " +
                    $"If it was intentionally deleted, please delete the entry from ControlTemplates.json along with its references from source files.");
            }
        }

        var componentInstanceTransform = new ComponentInstanceTransform(errors);
        var componentDefTransform = new ComponentDefinitionTransform(errors, _templateStore, componentInstanceTransform);

        // Transform component definitions and populate template set of component instances that need updates 
        foreach (var ctrl in _components)
        {
            componentDefTransform.BeforeWrite(ctrl.Value);
            AddComponentDefaults(ctrl.Value, templateDefaults);
        }

        var transformer = new SourceTransformer(this, errors, templateDefaults, new Theme(_themes), componentInstanceTransform, _editorStateStore, _templateStore, _entropy);

        foreach (var ctrl in _screens.Concat(_components))
        {
            transformer.ApplyBeforeWrite(ctrl.Value);
        }

        RestoreAssetFilePaths(errors);
    }

    private void AddComponentDefaults(BlockNode topParent, Dictionary<string, ControlTemplate> templateDefaults)
    {
        var type = topParent.Name.Kind.TypeName;
        if (!_templateStore.TryGetTemplate(type, out var template) || !(template.IsComponentTemplate ?? false))
            return;

        var componentTemplate = new ControlTemplate(type, "", "");

        foreach (var prop in topParent.Properties)
        {
            componentTemplate.InputDefaults.Add(prop.Identifier, prop.Expression.Expression);
        }
        templateDefaults.Add(type, componentTemplate);
    }


    // Called after loading. This will check internal fields and fill in consistency data. 
    internal void OnLoadComplete(ErrorContainer errors)
    {
        // Do integrity checks. 
        if (_header == null)
        {
            errors.FormatNotSupported("Missing header file");
            throw new DocumentException();
        }
        if (_properties == null)
        {
            errors.FormatNotSupported("Missing properties file");
            throw new DocumentException();
        }

        // Will visit all controls and add errors
        var uniqueVisitor = new UniqueControlNameVisitor(errors);
        foreach (var control in _screens.Values.Concat(_components.Values))
        {
            uniqueVisitor.Visit(control);
        }


        // Integrity checks.
        foreach (var kv in _connections.NullOk())
        {
            var connection = kv.Value;

            if (kv.Key != connection.id)
            {
                errors.FormatNotSupported($"Document consistency error. Connection id mismatch");
                throw new DocumentException();
            }
        }
    }

    // Get ComponentIds for components we've imported. 
    internal HashSet<string> GetImportedComponents()
    {
        var set = new HashSet<string>();
        if (_libraryReferences != null)
        {
            foreach (var item in _libraryReferences)
            {
                set.Add(item.OriginalComponentDefinitionTemplateId);
            }
        }
        return set;
    }

    private static FilePath GetAssetFilePathWithoutPrefix(string path)
    {
        return FilePath.FromMsAppPath(path.Substring(AssetFilePathPrefix.Length));
    }

    internal void StabilizeAssetFilePaths(ErrorContainer errors)
    {
        _entropy.LocalResourceFileNames.Clear();

        // If a name matches case insensitive but not case sensitive, it is a candidate for rename
        var caseInsensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var caseSensitiveNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in _resourcesJson.Resources.Where(resource => resource?.Name != null && resource.ResourceKind == ResourceKind.LocalFile)
                                                         .OrderBy(resource => resource.Name, StringComparer.Ordinal))
        {
            if (caseInsensitiveNames.Add(resource.Name))
            {
                caseSensitiveNames.Add(resource.Name);
            }
        }

        // Keep the newly renamed asset files separate so they do not overwrite any existing asset files
        // due to collisions between the resource and file names.
        var newAssetFiles = new Dictionary<FilePath, FileEntry>();

        // Update AssetFile paths
        foreach (var resource in _resourcesJson.Resources.Where(resource => resource?.Name != null && resource.ResourceKind == ResourceKind.LocalFile)
                                                         .OrderBy(resource => resource.Name, StringComparer.Ordinal))
        {
            var originalName = resource.Name;
            var assetFilePath = GetAssetFilePathWithoutPrefix(resource.Path);
            var pathToStabilize = resource.Path;

            if (!_assetFiles.TryGetValue(assetFilePath, out var fileEntry))
                continue;
            if (!caseSensitiveNames.Contains(resource.Name) && caseInsensitiveNames.Contains(resource.Name))
            {
                var i = 1;
                var newResourceName = resource.Name + '_' + i;
                while (caseInsensitiveNames.Contains(newResourceName))
                {
                    ++i;
                    newResourceName = resource.Name + '_' + i;
                }

                resource.Name = newResourceName;

                caseInsensitiveNames.Add(resource.Name);
                caseSensitiveNames.Add(resource.Name);

                var colliding = _entropy.LocalResourceFileNames.Keys.First(key => string.Equals(key, originalName, StringComparison.OrdinalIgnoreCase));
                errors.GenericWarning($"Asset named {originalName} collides with {colliding}, unpacking as {resource.Name}");
            }

            var extension = assetFilePath.GetExtension();
            var newFileName = resource.Name + extension;

            _entropy.LocalResourceFileNames.Add(resource.Name, resource.FileName);

            var updatedPath = FilePath.FromMsAppPath(FilePath.GetResourceRelativePath(resource.Content)).Append(newFileName);
            resource.Path = updatedPath.ToMsAppPath();
            resource.FileName = newFileName;

            var withoutPrefix = GetAssetFilePathWithoutPrefix(resource.Path);
            var updatedPathToStabilize = resource.Path;

            fileEntry.Name = withoutPrefix;
            _assetFiles.Remove(assetFilePath);
            newAssetFiles[withoutPrefix] = fileEntry;

            // For every duplicate asset file an additional <filename>.json file is created which contains information like - originalName, newFileName.
            if (resource.Name != originalName && !_localAssetInfoJson.ContainsKey(newFileName))
            {
                var assetFileInfoPath = GetAssetFilePathWithoutPrefix(FilePath.GetResourceRelativePath(resource.Content)).Append(resource.FileName + ".json");
                _localAssetInfoJson.Add(resource.FileName, new LocalAssetInfoJson() { OriginalName = originalName, NewFileName = resource.FileName, Path = assetFileInfoPath.ToPlatformPath() });
            }
        }

        // Once we have all the new asset files, add them to the existing
        foreach (var newAssetKey in newAssetFiles.Keys)
        {
            _assetFiles[newAssetKey] = newAssetFiles[newAssetKey];
        }
    }

    private int FindMaxEntropyFileName()
    {
        var max = 0;
        foreach (var filename in _entropy.LocalResourceFileNames.Values)
        {
            var oldName = Path.GetFileNameWithoutExtension(filename);
            if (int.TryParse(oldName, out var number) && number > max)
            {
                max = number;
            }
        }
        return max;
    }

    private void RestoreAssetFilePaths(ErrorContainer errors)
    {
        // For apps unpacked before this asset rewrite was added, skip the restore step
        try
        {
            if (_entropy?.LocalResourceFileNames == null)
                return;

            if (_resourcesJson?.Resources == null || _assetFiles == null)
                return;

            var maxFileNumber = FindMaxEntropyFileName();

            // Keep the newly renamed asset files separate so they do not overwrite any existing asset files
            // due to collisions between the resource and file names.
            var newAssetFiles = new Dictionary<FilePath, FileEntry>();

            foreach (var resource in _resourcesJson.Resources)
            {
                if (resource == null)
                    continue;

                if (resource.ResourceKind != ResourceKind.LocalFile)
                    continue;

                var assetFilePath = GetAssetFilePathWithoutPrefix(resource.Path);
                if (!_assetFiles.TryGetValue(assetFilePath, out var fileEntry))
                    continue;

                if (!_entropy.LocalResourceFileNames.TryGetValue(resource.Name, out var msappFileName))
                {
                    maxFileNumber++;
                    msappFileName = maxFileNumber.ToString("D4") + assetFilePath.GetExtension();
                }

                // Restore the original names of the duplicate asset files.
                if (_localAssetInfoJson?.TryGetValue(resource.FileName, out var localAssetInfoJson) == true)
                {
                    resource.Name = localAssetInfoJson.OriginalName;
                }

                var updatedPath = FilePath.FromMsAppPath(FilePath.GetResourceRelativePath(resource.Content)).Append(msappFileName);
                resource.Path = updatedPath.ToMsAppPath();
                resource.FileName = msappFileName;

                var withoutPrefix = GetAssetFilePathWithoutPrefix(resource.Path);
                fileEntry.Name = withoutPrefix;
                _assetFiles.Remove(assetFilePath);
                newAssetFiles[withoutPrefix] = fileEntry;
            }

            // Once we have all the new asset files, add them to the existing
            foreach (var newAssetKey in newAssetFiles.Keys)
            {
                _assetFiles[newAssetKey] = newAssetFiles[newAssetKey];
            }
        }
        catch (NullReferenceException nullReferenceException)
        {
            errors.InternalError(nullReferenceException);
            return;
        }
    }

    // Helper for traversing and ensuring unique control names. 
    internal class UniqueControlNameVisitor
    {
        // Control names are case sensitive. 
        private readonly Dictionary<string, SourceLocation?> _names = new(StringComparer.Ordinal);
        private readonly ErrorContainer _errors;

        public UniqueControlNameVisitor(ErrorContainer errors)
        {
            _errors = errors;
        }

        public void Visit(BlockNode node)
        {
            // Ignore test templates here. 
            // Test templates have control-like syntax, but allowed to repeat names:
            //    Step4 As TestStep:
            if (AppTestTransform.IsTestSuite(node.Name.Kind.TypeName))
            {
                return;
            }

            Visit(node.Name);
            foreach (var child in node.Children)
            {
                Visit(child);
            }
        }

        public void Visit(TypedNameNode node)
        {
            if (_names.TryGetValue(node.Identifier, out var existing))
            {
                _errors.DuplicateSymbolError(node.SourceSpan.GetValueOrDefault(), node.Identifier, existing.GetValueOrDefault());
            }
            else
            {
                _names.Add(node.Identifier, node.SourceSpan);
            }
        }
    }
}
