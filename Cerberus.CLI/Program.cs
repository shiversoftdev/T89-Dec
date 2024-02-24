using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Cerberus.Logic;
using CommandLine;
using CommandLine.Text;

namespace Cerberus.CLI
{
    class Program
    {
        /// <summary>
        /// File Extensions we accept
        /// </summary>
        static readonly string[] AcceptedExtensions =
        {
            ".ff",
            ".gscc",
            ".cscc",
        };

        /// <summary>
        /// Directory of the processed scripts
        /// </summary>
        static readonly string ProcessDirectory = "ProcessedScripts";

        /// <summary>
        /// Command Line Options
        /// </summary>
        static CliOptions Options { get; set; }


        /// <summary>
        /// Class to hold CLI options
        /// </summary>
        class CliOptions
        {
            [Option('v', "verbose", Required = false, HelpText = "Outputs more information to the console.")]
            public bool Verbose { get; set; }
            [Option('d', "disassemble", Required = false, HelpText = "Disassembles the script/s.")]
            public bool Disassemble { get; set; }
            [Option('n', "close", Required = false, HelpText = "Closes the program once execution has finished.")]
            public bool Close { get; set; }
            [Option('h', "help", Required = false, HelpText = "Prints this message.")]
            public bool Help { get; set; }
        }

        /// <summary>
        /// Prints a message in verbose mode
        /// </summary>
        static void PrintVerbose(object value)
        {
            if(Options?.Verbose == true)
            {
                Console.WriteLine(value);
            }
        }

        /// <summary>
        /// Prints help output
        /// </summary>
        static void PrintHelp(ParserResult<CliOptions> outputOptions)
        {
            var helpText = new HelpText
            {
                AdditionalNewLineAfterOption = false,
                AddDashesToOption = true,
            };

            helpText.AddOptions(outputOptions);

            var stuff = helpText.ToString().Split('\n').Where(x => !string.IsNullOrWhiteSpace(x));

            Console.WriteLine(": Example: Cerberus.CLI [options] <files (.gsc|.csc|.gscc|.cscc|.ff)>");
            Console.WriteLine(": Options: ");

            foreach (var item in stuff)
            {
                Console.WriteLine(":\t{0}", item.Trim());
            }
        }

        private class ExportCollection
        {
            public Dictionary<string, HashSet<string>> Functions;
            public string ScriptPath;
        }

        static Dictionary<string, ExportCollection> ExportMappings = new Dictionary<string, ExportCollection>();
        /// <summary>
        /// Processes a script file
        /// </summary>
        /// <param name="filePath"></param>
        static void ProcessScript(string filePath)
        {
            using(var reader = new BinaryReader(File.OpenRead(filePath)))
            {
                using (var script = ScriptBase.LoadScript(reader))
                {
                    PrintVerbose(string.Format(": Processing {0} script.", script.Game));
                    var outputPath = Path.Combine(ProcessDirectory, script.Game, script.FilePath);
                    if (Path.GetExtension(outputPath) == "") outputPath += ".gsc";
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                    PrintVerbose(string.Format(": Outputting to {0}", outputPath));

                    if (Options.Disassemble)
                    {
                        PrintVerbose(": Disassembling script..");
                        File.WriteAllText(outputPath + ".gasm", script.Disassemble());
                    }

                    PrintVerbose(": Decompiling script..");
                    File.WriteAllText(outputPath, script.Decompile());
                    ExportMappings[script.FilePath] = new ExportCollection();
                    ExportMappings[script.FilePath].Functions = script.ExportCollection;
                    ExportMappings[script.FilePath].ScriptPath = script.FilePath;
                }
            }
        }

        static bool EmitExportDef = true;
        static void EmitExportsCollection()
        {
            if(!EmitExportDef)
            {
                return;
            }
            StringBuilder result = new StringBuilder();
            foreach(var kvp in ExportMappings)
            {
                foreach(var namespaceCollection in kvp.Value.Functions)
                {
                    foreach(var export in namespaceCollection.Value)
                    {
                        if(export.Contains("autoexec ") || export.Contains("private ") || export.Contains("event "))
                        {
                            continue;
                        }

                        string fullHeader = export;
                        string _export = export.Replace("function ", "");
                        string funcName = _export.Substring(0, _export.IndexOf("("));

                        if(funcName.StartsWith("__"))
                        {
                            continue;
                        }

                        string insertDef = funcName + "(";
                        List<string> parameters = new List<string>();

                        for(int i = export.IndexOf("(") + 1; i < fullHeader.Length && export[i] != ')'; i++)
                        {
                            int endIndex = export.IndexOf(",", i);
                            if(endIndex < 0)
                            {
                                endIndex = export.LastIndexOf(")");
                            }
                            int paramLength = endIndex - i;

                            string paramValue = export.Substring(i, paramLength).Trim();
                            i += paramLength + 1; // skip for spaces
                            parameters.Add(paramValue);
                        }

                        for(int i = 0; i < parameters.Count; i++)
                        {
                            if (parameters[i] == "...")
                            {
                                insertDef += $"${{0:params}}";
                            }
                            else
                            {
                                insertDef += $"${{{i + 1}:{parameters[i].Replace("&", "")}}}";
                            }
                            
                            if (i + 1 < parameters.Count)
                            {
                                insertDef += ", ";
                            }
                        }

                        insertDef += ")";

                        result.AppendLine("importDef = new DefImport;");
                        result.AppendLine($"importDef._namespace = `{namespaceCollection.Key}`;");
                        result.AppendLine($"importDef.header = `{export}`;");
                        result.AppendLine($"importDef.script = `{kvp.Key}`;");
                        result.AppendLine($"importDef.name = `{funcName}`;");
                        result.AppendLine($"importDef.insert = \"{insertDef}\";");
                        result.AppendLine("defImports.push(importDef);");
                        result.AppendLine();
                    }
                }
            }

            File.WriteAllText("export_def.txt", result.ToString());
        }

        /// <summary>
        /// Main Entry Point
        /// </summary>
        static void Main(string[] args)
        {
            Console.WriteLine(": ----------------------------------------------------------");
            Console.WriteLine(": Cerberus Command Line - Black Ops II/III Script Decompiler");
            Console.WriteLine(": Developed by Scobalula");
            Console.WriteLine(": Version: {0}", Assembly.GetExecutingAssembly().GetName().Version);
            Console.WriteLine(": ----------------------------------------------------------");

            var parser = new Parser(config => config.HelpWriter = null);
            var cliOptions = parser.ParseArguments<CliOptions>(args).WithParsed(x => Options = x).WithNotParsed(_ => Options = new CliOptions());

            var filesProcessed = 0;

            // Force working directory back to exe
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            Console.WriteLine(": Exporting to: {0}", Directory.GetCurrentDirectory());

            //LoadHashTables();

            var files = Directory.GetFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "*.*", SearchOption.AllDirectories);

            Console.WriteLine(files.Length);

            //foreach (var arg in files)
            //{
            //    try
            //    {
            //        if (AcceptedExtensions.Contains(Path.GetExtension(arg).ToLower()))
            //        {
            //            if (File.Exists(arg))
            //            {
            //                filesProcessed++;
            //                Console.WriteLine(": Processing {0}...", Path.GetFileName(arg));

            //                switch (Path.GetExtension(arg).ToLower())
            //                {
            //                    case ".gsc":
            //                    case ".csc":
            //                    case ".gscc":
            //                    case ".cscc":
            //                        {
            //                            ProcessScript(arg);
            //                            break;
            //                        }
            //                }

            //                Console.WriteLine(": Processed {0} successfully.", Path.GetFileName(arg));
            //            }
            //        }
            //    }
            //    catch (Exception e)
            //    {
            //        Console.WriteLine(": An error has occured while processing {0}: {1}", Path.GetFileName(arg), e.Message);
            //        PrintVerbose(e);
            //    }
            //}

            foreach (var arg in files)
            {
                try
                {
                    if (AcceptedExtensions.Contains(Path.GetExtension(arg).ToLower()))
                    {
                        if (File.Exists(arg))
                        {
                            filesProcessed++;
                            Console.WriteLine(": Processing {0}...", Path.GetFileName(arg));

                            switch (Path.GetExtension(arg).ToLower())
                            {
                                case ".gsic":
                                case ".gsc":
                                case ".csc":
                                case ".gscc":
                                case ".cscc":
                                    {
                                        ProcessScript(arg);
                                        break;
                                    }
                                case ".ff":
                                    {
                                        PrintVerbose(": Decompressing and Processing Fast File.....");

                                        // Skip ZM Temple, it causes issues due to a weird large number
                                        // of blocks
                                        if (Path.GetFileName(arg) != "zm_temple")
                                        {
                                            try
                                            {
                                                var _files = FastFile.Decompress(arg, arg + ".output");

                                                if (Options.Verbose)
                                                {
                                                    foreach (var file in _files)
                                                    {
                                                        Console.WriteLine(": Found {0}", file);
                                                    }
                                                }

                                            }
                                            catch (Exception e)
                                            {
                                                PrintVerbose(e);
                                                throw e;
                                            }
                                            finally
                                            {
#if !DEBUG
                                                            File.Delete(arg + ".output");
#endif
                                            }
                                        }

                                        break;
                                    }
                            }

                            Console.WriteLine(": Processed {0} successfully.", Path.GetFileName(arg));
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(": An error has occured while processing {0}: {1}", Path.GetFileName(arg), e.ToString());
                    PrintVerbose(e);
                }
            }

            if (Options.Help || filesProcessed <= 0)
            {
                PrintHelp(cliOptions);
            }

            GC.Collect();

            if (Options.Close == false)
            {
                EmitExportsCollection();
                Console.WriteLine(": Execution completed successfully, press Enter to exit.");
                Console.ReadLine();
            }
        }
    }
}