// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Microsoft.PowerPlatform.Formulas.Tools;

namespace PASopa;

// Mode: Extract
internal class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine($"MsApp/Source converter. Version: {SourceSerializer.CurrentSourceVersion}");

        var warningText = "Warning: {0} is in preview, and functionality is not guaranteed. Use extreme caution if using in a production environment. For more information see aka.ms/paccanvas";

        var mode = args.Length > 0 ? args[0]?.ToLower() : null;
        if (mode == "-test")
        {
            if (args.Length < 2)
            {
                Usage();
                return;
            }

            var msAppPath = args[1];
            Console.WriteLine("Test roundtripping: " + msAppPath);

            // Test round-tripping
            MsAppTest.StressTest(msAppPath);
            return;
        }


        if (mode == "-testall2")
        {
            if (args.Length < 2)
            {
                Usage();
                return;
            }
            // Roundtrip all .msapps in a folder.
            var msAppPathDir = args[1];
            var countTotal = 0;
            var countPass = 0;
            Console.WriteLine("Test smart-merge all .msapps in : " + msAppPathDir);
            var msAppCommon = Path.Combine(msAppPathDir, "empty.msapp");
            foreach (var msAppPath in Directory.EnumerateFiles(msAppPathDir, "*.msapp", SearchOption.TopDirectoryOnly))
            {
                // Merge test requires a 2nd app. Could do a full NxN matrix. But here, just pick the first item.
                msAppCommon ??= msAppPath;

                var sw = Stopwatch.StartNew();
                var ok = MsAppTest.MergeStressTest(msAppCommon, msAppPath);

                var str = ok ? "Pass" : "FAIL";
                countTotal++;
                if (ok) { countPass++; }
                sw.Stop();
                if (!ok)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                Console.WriteLine($"Test: {Path.GetFileName(msAppPath)}: {str}  ({sw.ElapsedMilliseconds / 1000}s)");
                Console.ResetColor();
            }
            Console.WriteLine($"{countPass}/{countTotal}  ({countPass * 100 / countTotal}% passed)");
            return;
        }

        if (mode == "-testall")
        {
            if (args.Length < 2)
            {
                Usage();
                return;
            }
            // Roundtrip all .msapps in a folder.
            var msAppPathDir = args[1];
            var countTotal = 0;
            var countPass = 0;
            Console.WriteLine("Test roundtripping all .msapps in : " + msAppPathDir);
            foreach (var msAppPath in Directory.EnumerateFiles(msAppPathDir, "*.msapp", SearchOption.TopDirectoryOnly))
            {
                var sw = Stopwatch.StartNew();
                var ok = MsAppTest.StressTest(msAppPath);
                var str = ok ? "Pass" : "FAIL";
                countTotal++;
                if (ok) { countPass++; }
                sw.Stop();
                if (!ok)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                Console.WriteLine($"Test: {Path.GetFileName(msAppPath)}: {str}  ({sw.ElapsedMilliseconds / 1000}s)");
                Console.ResetColor();
            }
            Console.WriteLine($"{countPass}/{countTotal}  ({countPass * 100 / countTotal}% passed)");
        }
        else if (mode == "-unpack")
        {
            if (args.Length < 2)
            {
                Usage();
                return;
            }

            var msAppPath = args[1];
            msAppPath = Path.GetFullPath(msAppPath);

            Console.WriteLine(warningText, "unpack");

            if (!msAppPath.EndsWith(".msapp", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("must be path to .msapp file");
            }

            string outDir;
            if (args.Length == 2)
            {
                outDir = msAppPath.Substring(0, msAppPath.Length - 6) + "_src"; // chop off ".msapp";
            }
            else
            {
                outDir = args[2];
            }

            Console.WriteLine($"Unpack: {msAppPath} --> {outDir} ");

            (var msApp, var errors) = TryOperation(() => CanvasDocument.LoadFromMsapp(msAppPath));
            errors.Write(Console.Error);

            if (errors.HasErrors)
            {
                return;
            }

            errors = TryOperation(() => msApp.SaveToSources(outDir, verifyOriginalPath: msAppPath));
            errors.Write(Console.Error);
            if (errors.HasErrors)
            {
                return;
            }
        }
        else if (mode == "-pack")
        {
            if (args.Length < 3)
            {
                Usage();
                return;
            }

            var msAppPath = Path.GetFullPath(args[1]);
            var inputDir = Path.GetFullPath(args[2]);

            Console.WriteLine(warningText, "pack");

            Console.WriteLine($"Pack: {inputDir} --> {msAppPath} ");

            (var msApp, var errors) = TryOperation(() => CanvasDocument.LoadFromSources(inputDir));
            errors.Write(Console.Error);
            if (errors.HasErrors)
            {
                return;
            }
            errors = TryOperation(() => msApp.SaveToMsApp(msAppPath));
            errors.Write(Console.Error);
            if (errors.HasErrors)
            {
                return;
            }
        }
        else if (mode == "-make")
        {
            if (args.Length < 4)
            {
                Usage();
                return;
            }

            var msAppPath = Path.GetFullPath(args[1]);
            var pkgsPath = Path.GetFullPath(args[2]);
            var inputPA = Path.GetFullPath(args[3]);

            Console.WriteLine($"Make: {inputPA} --> {msAppPath} ");

            var appName = Path.GetFileName(msAppPath);

            (var app, var errors) = TryOperation(() => CanvasDocument.MakeFromSources(appName, pkgsPath, new List<string>() { inputPA }));
            errors.Write(Console.Error);
            if (errors.HasErrors)
            {
                return;
            }
            errors = TryOperation(() => app.SaveToMsApp(msAppPath));
            errors.Write(Console.Error);
            if (errors.HasErrors)
            {
                return;
            }
        }
        else if (mode == "-merge")
        {
            if (args.Length < 5)
            {
                Usage();
                return;
            }

            var path1 = args[1];
            var path2 = args[2];
            var parent = args[3];
            var pathresult = args[4];

            Console.WriteLine($"Merge is very experimental right now, do not rely on this behavior");
            Console.WriteLine($"Merge: {path1}, {path2} --> {pathresult} ");


            (var app1, var errors1) = TryOperation(() => CanvasDocument.LoadFromSources(path1));
            errors1.Write(Console.Error);
            if (errors1.HasErrors)
            {
                return;
            }

            (var app2, var errors2) = TryOperation(() => CanvasDocument.LoadFromSources(path2));
            errors2.Write(Console.Error);
            if (errors2.HasErrors)
            {
                return;
            }

            (var parentApp, var errors3) = TryOperation(() => CanvasDocument.LoadFromSources(parent));
            errors3.Write(Console.Error);
            if (errors3.HasErrors)
            {
                return;
            }

            var result = CanvasMerger.Merge(app1, app2, parentApp);

            var errors = result.SaveToSources(pathresult);
            errors.Write(Console.Error);
            if (errors.HasErrors)
            {
                return;
            }
        }
        else
        {
            Usage();
        }
    }

    private static void Usage()
    {
        Console.WriteLine(
            @"Usage

                -unpack PathToApp.msapp PathToNewSourceFolder
                -unpack PathToApp.msapp  // infers source folder
                -pack  NewPathToApp.msapp PathToSourceFolder
                -make PathToCreateApp.msapp PathToPkgFolder PathToPaFile
                -merge path1 path2 parentPath resultpath

                ");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "ByDesign - The exceptions are collected")]
    private static (CanvasDocument, ErrorContainer) TryOperation(Func<(CanvasDocument, ErrorContainer)> operation)
    {
        CanvasDocument app = null;
        var errors = new ErrorContainer();
        try
        {
            (app, errors) = operation();
        }
        catch (Exception e)
        {
            // Add unhandled exception to the error container.
            errors.InternalError(e);
        }
        return (app, errors);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "ByDesign - The exceptions are collected")]
    private static ErrorContainer TryOperation(Func<ErrorContainer> operation)
    {
        var errors = new ErrorContainer();
        try
        {
            errors = operation();
        }
        catch (Exception e)
        {
            // Add unhandled exception to the error container.
            errors.InternalError(e);
        }
        return errors;
    }
}
