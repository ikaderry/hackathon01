
# Power Apps Source File Pack and Unpack Utility

**This project is still in Preview and under NDA.**
 
**We welcome feedback on the project, file format, and capabilities.** 

This tool enables Canvas apps to be edited outside of Power Apps Studio and managed in a source control tool such as GitHub.  The basic work flow is:
1. Download an existing Canvas app as a .msapp file, using **File** > **Save as** > **This computer** in Power Apps Studio.
1. Use this tool to extract the .msapp file into editable source files.
1. Edit these files with any text editor.
1. Check these files into any source control manager.
1. Use this tool to recreate a .msapp file from the editable source files.
1. Upload the .msapp file using **File** > **Open** > **Browse** in Power Apps Studio.

This is similar to the [Solution Packager](https://docs.microsoft.com/en-us/power-platform/alm/solution-packager-tool) for Microsoft Dataverse.

## Usage
Use the test console app to unpack/pack today.  In the future, this functionality will be included with the [Power Apps CLI](https://docs.microsoft.com/en-us/powerapps/developer/common-data-service/powerapps-cli).

You will need [.NET Core SDK v3.1.x (x64)](https://dotnet.microsoft.com/download/dotnet-core/3.1) in order to build. 
Build the test console app by running: `\build.cmd`  
This will create: `\bin\Debug\PASopa\PASopa.exe`

To unpack a .msapp file: `pasopa -unpack FromApp.msapp ToSourceFolder`
To pack a .msapp file: `pasopa -pack ToApp.msapp FromSourceFolder`

The tool aggressively ensures that it can faithfully round trip the conversion from .msapp to source files.  An unpack will immediately do a sanity test by performing a repack and compare.

## Folder structure
Unpack and pack use this folder structure:

- **\src** - the control and component files. This contains the sources.
   - CanvasManifest.json - a manifest file. This contains what is normally in the header, properties, and publishInfo.
   - \*.json - the raw control.json file.
   - \*.pa.yaml - the formulas extracted from the control.json file.  **This is the place to edit your formulas.**
- **\other** - all miscellaneous files needed to recreate the .msapp
   - entropy.json - volatile elements (like timestamps) are extracted to this file. This helps reduce noisy diffs in other files while ensuring that we can still round trip.
   - Holds other files from the msapp, such as what is in \references
- **\DataSources** - a file per datasource.

## File format
The .pa.yaml files use a subset of [YAML](https://yaml.org/spec/1.2/spec.html).  Most notably and similar to Excel, all expressions must begin with an `=` sign.  More details are available [here](PAFileFormat.md).

## Merging changes from Studio
When merging changes made in two different Studio sessions:
- Ensure that all control names are unique.  It is easy for them not to be, as inserting a button in two different sessions can easily result in two Button1 controls.  We recommend naming controls soon after creating them to avoid this problem.  The tool will not accept two controls with the same name.  
- If there are conflicts or errors, you can delete these files:
	- \src\editorstate\*.json  - These files contain optional information in studio. 
	- \other\entropy.json  
- For any conflict in these files, it’s ok to accept the latest version: 
	- \checksum.json 
- If there are any merge conflicts under these paths, it is not safe to merge.   Let us know if this happens often and we will work on restructuring the file format to avoid conflicts.
	- \connections\*
	- \datasources\*
	- \pkgs\*
	- CanvasManifest.json 

## Contributing

We welcome feedback on the design, file format, and capabilities. Comments and issues are very welcome.   

This project is still experimental and we routinely refactor the folder structure, file format, and implementation in big ways.  As such, we aren't yet accepting code contributions until we are more stable.

Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit [https://cla.opensource.microsoft.com](https://cla.opensource.microsoft.com).

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

### Setting up a dev box

For a developer machine (Windows 10, WSL, Linux, macOS), install:

- [git](https://git-scm.com/downloads)
- [.NET Core SDK v3.1.x (x64)](https://dotnet.microsoft.com/download/dotnet-core/3.1)
- [VS Code](https://code.visualstudio.com/Download)
- if on Windows: [VS2019 (Community edition will do)](https://visualstudio.microsoft.com/downloads/).  Select at least the following workload: .NET Core cross-plat
- recommended VSCode extensions:
  - [GitLens (eamodio.gitlens)](https://github.com/eamodio/vscode-gitlens)
  - [C# (ms-vscode.csharp)](https://github.com/OmniSharp/omnisharp-vscode)

### Building and running tests

After cloning this repo (https://github.com/microsoft/PowerApps-Language-Tooling), open a terminal/cmd/PS prompt with the dotnet executable on the path. Check with: ```dotnet --version ```

To build, run tests and produce nuget packages, run this command:

```bash
./build ci
```

To list all build targets, run: ```./build --list-tree```

To see other build help, run: ```./build --help```
