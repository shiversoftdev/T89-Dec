﻿using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using Cerberus.Logic;
using Microsoft.Win32;
using System.Globalization;
using System.Threading;
using System.Diagnostics;
using ICSharpCode.AvalonEdit.Search;

namespace Cerberus.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Loaded Script Files
        /// </summary>
        private readonly List<ScriptBase> ScriptFiles = new List<ScriptBase>();

        /// <summary>
        /// Gets or Sets the Active Script
        /// </summary>
        private ScriptBase ActiveScript { get; set; }

        /// <summary>
        /// Supported Hash Tables
        /// </summary>
        private readonly Dictionary<string, Dictionary<uint, string>> HashTables = new Dictionary<string, Dictionary<uint, string>>()
        {
            { "BlackOps2", new Dictionary<uint, string>() },
            { "BlackOps3", new Dictionary<uint, string>() },
            { "t8", new Dictionary<uint, string>() },
        };

        private Thread ActiveThread { get; set; }
        /// <summary>
        /// Whether or not to end the current thread
        /// </summary>
        private bool KeepThreadAlive = true;

        /// <summary>
        /// Main Entry Point
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Load the syntax highlighting from our resource
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cerberus.UI.Resources.GSC.xshd"))
            {
                using (XmlTextReader reader = new XmlTextReader(stream))
                {
                    Decompiler.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    Disassembly.SyntaxHighlighting = Decompiler.SyntaxHighlighting;
                    Disassembly.IsReadOnly = true;
                    Decompiler.IsReadOnly = true;
                    SearchPanel.Install(Decompiler);
                    SearchPanel.Install(Disassembly);
                    SearchPanel.Install(Log);
                }
            }

            //// Check for Updates
            //new Thread(() =>
            //{
            //    if (HydraUpdater.CheckForUpdates(Assembly.GetExecutingAssembly().GetName().Version))
            //    {
            //        var result = MessageBox.Show("A new version of Cerberus is available, do you want to download it now?", "Cerberus | Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            //        if (result == MessageBoxResult.Yes)
            //            Process.Start("https://github.com/Scobalula/Cerberus-Repo/releases");
            //    }
            //}).Start();

            ScriptList.ItemsSource = ScriptFiles;
            //LoadHashTables();
        }

        /// <summary>
        /// Disassembles/Decompiles a script if item is a script, otherwise jumps to line for functions
        /// </summary>
        public void ListViewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if(sender is ListViewItem item)
            {
                if (item.Content is ScriptBase script)
                {
                    LogIt("Parsing " + script.FileName);
                    script.VLog = LogIt;

                    Disassembly.ScrollToLine(0);
                    Decompiler.ScrollToLine(0);

                    FunctionList.ItemsSource = script.Exports;
                    StringList.ItemsSource   = script.Strings;
                    ImportList.ItemsSource   = script.Imports;
                    IncludeList.ItemsSource  = script.Includes;
                    AnimTreeList.ItemsSource = script.AnimTrees.Values;

                    try
                    {
                        Disassembly.Text = script.Disassemble();
                        Decompiler.Text  = script.Decompile();
                        HexView.Stream = (MemoryStream)script.Reader.BaseStream;
                    }
                    catch(Exception ex)
                    {
                        Disassembly.Text = "";
                        Decompiler.Text = "";
                        MessageBox.Show(
                            string.Format("Failed to load disassemble/decompile script:\n\n{0}", ex),
                            "Cerberus", MessageBoxButton.OK, MessageBoxImage.Error);
                        LogIt(ex);
                        return;
                    }
                }
                else if(item.Content is ScriptExport export)
                {
                    LogIt("Jumping to " + export.Name);
                    Disassembly.ScrollToLine(export.DisassemblyLine);
                    Decompiler.ScrollToLine(export.DecompilerLine);
                }
            }
        }

        /// <summary>
        /// Loads in required hash tables
        /// </summary>
        private void LoadHashTables()
        {
            LogIt("Loading hash tables");
            foreach (var hashTable in HashTables)
            {
                try
                {
                    string[] lines = File.ReadAllLines(hashTable.Key + ".txt");
                    LogIt("Loading " + hashTable.Key + ".txt");
                    foreach (string line in lines)
                    {
                        try
                        {
                            string lineTrim = line.Trim();

                            // Ignore comment lines
                            if (!lineTrim.StartsWith("#"))
                            {
                                string[] lineSplit = lineTrim.Split(',');

                                if (lineSplit.Length > 1)
                                {
                                    // Parse as hex, without 0x
                                    if (uint.TryParse(lineSplit[0].TrimStart('0', 'x'), NumberStyles.HexNumber, CultureInfo.CurrentCulture, out var hash))
                                    {
                                        hashTable.Value[hash] = lineSplit[1];
                                    }
                                }
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        /// <summary>
        /// Saves scripts to disassembly/decompiled files
        /// </summary>
        public void SaveScriptFiles(ScriptBase[] scripts)
        {
            SetProgressCount(scripts.Length);

            foreach (var script in scripts)
            {
                SetProgressMessage("Saving " + script.FileName);
                LogIt("Saving " + script.FileName);

                try
                {
                    var outputPath = System.IO.Path.Combine("ProcessedScripts", script.Game, script.FilePath);
                    if (System.IO.Path.GetExtension(outputPath) == "") outputPath += ".gsc";
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));

                    // Dump it
                    LogIt("Disassembling script..");
                    File.WriteAllText(outputPath + ".gasm", script.Disassemble());
                    LogIt("Decompiling script..");
                    File.WriteAllText(outputPath, script.Decompile());
                }
                catch (Exception e)
                {
                    LogIt("Failed to save " + script.FileName);
                    LogIt(e);
                    continue;
                }

                LogIt("Saved " + script.FileName);

                if (!IncrementProgress())
                {
                    // Ensure we clear whatever we have loaded
                    LogIt("Operation cancelled");
                    return;
                }
            }
        }

        /// <summary>
        /// Handles loading the script files
        /// </summary>
        private void LoadScriptFiles(string[] files)
        {
            ClearScripts();
            SetProgressCount(files.Length);

            foreach (var file in files)
            {
                SetProgressMessage("Loading " + file);
             
                LogIt("Loading " + file);
                // Wrap in a Try/Catch because we need control of it after
                var reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(file)));

#if DEBUG
                var script = ScriptBase.LoadScript(reader, HashTables);
                script.LoadHeader();

                bool skip = false;
                foreach (var sf in ScriptFiles)
                {
                    if (sf.FilePath == script.FilePath)
                    {
                        LogIt($"Skipping '{script.FilePath}' because it is already loaded");
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;
                ScriptFiles.Add(script);
#else
                try
                {
                    ScriptFiles.Add(ScriptBase.LoadScript(reader, HashTables));
                }
                catch(Exception e)
                {
                    reader?.Dispose();
                    LogIt("Failed to load " + file);
                    LogIt(e);
                    continue;
                }
#endif
                if (!IncrementProgress())
                {
                    // Ensure we clear whatever we have loaded
                    LogIt("Operation cancelled");
                    ClearScripts();
                    return;
                }
            }

            // Set the item source for UI
            Dispatcher.BeginInvoke(new Action(() => ScriptList.ItemsSource = ScriptFiles));
        }

        /// <summary>
        /// Prints a message to the log
        /// </summary>
        private void LogIt(object value)
        {
            // Invoke dispatcher to update UI
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Log.Text += value.ToString() + "\n";
            }));
        }

        /// <summary>
        /// Sets the progress count
        /// </summary>
        private void SetProgressCount(double value)
        {
            // Invoke dispatcher to update UI
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Progress.Maximum = value;
                Progress.Value = 0;
            }));
        }

        /// <summary>
        /// Sets the progress message
        /// </summary>
        private void SetProgressMessage(string value)
        {
            // Invoke dispatcher to update UI
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ProgressLabel.Content = value;
            }));
        }

        /// <summary>
        /// Clears loaded scripts
        /// </summary>
        private void ClearScripts()
        {
            // Ensure we clear whatever we have loaded
            LogIt("Clearing Scripts");

            //ActiveScript?.Dispose();
            //// Manually dispose
            //foreach (var script in ScriptFiles)
            //{
            //    script?.Dispose();
            //}

            //ScriptFiles.Clear();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScriptList.ItemsSource = null;
                FunctionList.ItemsSource = null;
                StringList.ItemsSource = null;
                ImportList.ItemsSource = null;
                IncludeList.ItemsSource = null;
                Disassembly.Text = "";
                Decompiler.Text = "";
            }));
            LogIt("Scripts cleared");
        }

        /// <summary>
        /// Update Progress and returns if we need to exit the thread
        /// </summary>
        private bool IncrementProgress()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Progress.Value++;
            }));

            return KeepThreadAlive;
        }

        /// <summary>
        /// Resets the Progress Bar
        /// </summary>
        private void ResetProgress()
        {
            SetProgressMessage("Idle");
            // Clear Progress
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Progress.Maximum = 100;
                Progress.Value = 0;
            }));
        }

        /// <summary>
        /// Handles Abort Click
        /// </summary>
        private void AbortClick(object sender, RoutedEventArgs e)
        {
            KeepThreadAlive = false;
        }

        /// <summary>
        /// Handles Abort Double Click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AbortDoubleClick(object sender, MouseButtonEventArgs e)
        {
            KeepThreadAlive = false;
            ActiveThread.Join();
            ClearScripts();
            ResetProgress();
        }

        /// <summary>
        /// Handles About Button Click
        /// </summary>
        private void AboutButtonClick(object sender, RoutedEventArgs e)
        {
            new AboutWindow()
            {
                Owner = this
            }.ShowDialog();
        }

        /// <summary>
        /// Handles Button Click
        /// </summary>
        private void LoadClick(object sender, RoutedEventArgs e)
        {
            // Check if the thread is still like
            // if it is we need to wait
            if (ActiveThread?.IsAlive == true)
            {
                MessageBox.Show("Please wait for the current task to finish.", "Cerberus", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new OpenFileDialog()
            {
                Title = "Open Script File/s",
                CheckFileExists = true,
                Filter = "Script Files (*.gsc,*.csc,*.gscc,*.cscc)|*.gsc;*.csc;*.gscc;*.cscc",
                Multiselect = true
            };

            // Validate the dialog
            if (dialog.ShowDialog() == true)
            {
                // Stick loader on a separate thread
                // to avoid halting UI
                ActiveThread = new Thread(() =>
                {
                    KeepThreadAlive = true;
                    LoadScriptFiles(dialog.FileNames);
                    ResetProgress();
                });
                ActiveThread.Start();
            }
        }

            
        /// <summary>
        /// Handles the window being closed
        /// </summary>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Shut down the thread
            KeepThreadAlive = false;
        }

        /// <summary>
        /// Handles dropping files onto the script list
        /// </summary>
        private void ScriptListDrop(object sender, DragEventArgs e)
        {
            // Check if the thread is still like
            // if it is we need to wait
            if (ActiveThread?.IsAlive == true)
            {
                MessageBox.Show("Please wait for the current task to finish.", "Cerberus", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] data = (string[])e.Data.GetData(DataFormats.FileDrop);

                // Stick loader on a separate thread
                // to avoid halting UI
                ActiveThread = new Thread(() =>
                {
                    KeepThreadAlive = true;
                    LoadScriptFiles(data);
                    ResetProgress();
                });
                ActiveThread.Start();
            }
        }

        /// <summary>
        /// Opens Cerberus' page
        /// </summary>
        private void CerberusClick(object sender, RoutedEventArgs e)
        {
        }

        /// <summary>
        /// Saves scripts on click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveClick(object sender, RoutedEventArgs e)
        {
            var scripts = ScriptList.Items.Cast<ScriptBase>().ToList().ToArray();

            if(scripts.Length <= 0)
            {
                MessageBox.Show("Please select script/s to export first.", "Cerberus | Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Stick loader on a separate thread
            // to avoid halting UI
            ActiveThread = new Thread(() =>
            {
                KeepThreadAlive = true;
                SaveScriptFiles(scripts);
                ResetProgress();
            });
            ActiveThread.Start();
        }

        private void SaveOpsClick(object sender, RoutedEventArgs e)
        {
            List<byte> tdata = new List<byte>();
            byte[] VM_36_PC = Logic.BlackOps4Script.GetTableData(false);
            byte[] VM_36_PS4 = Logic.BlackOps4Script.GetTableData(true);
            byte[] VM_37 = Logic.T9_VM37AScript.GetTableData();
            byte[] VM_38 = Logic.T9_VM38Script.GetTableData();
            byte[] VM_1C_PS4 = Logic.T7VM1CScript.GetTableData(true);
            byte[] VM_1C_PC = Logic.T7VM1CScript.GetTableData(false);

            tdata.Add(0x1C);
            tdata.Add(0x00); // pc
            tdata.AddRange(BitConverter.GetBytes((ushort)VM_1C_PC.Length));
            tdata.AddRange(VM_1C_PC);

            tdata.Add(0x1C);
            tdata.Add(0x01); // ps4
            tdata.AddRange(BitConverter.GetBytes((ushort)VM_1C_PS4.Length));
            tdata.AddRange(VM_1C_PS4);

            // only give enough opcodes to compile
            //HashSet<byte> codes = new HashSet<byte>();
            //for(int i = 0; i < VM_36_PC.Length; i++)
            //{
            //    if(codes.Contains(VM_36_PC[i]))
            //    {
            //        VM_36_PC[i] = 0xFF;
            //        continue;
            //    }
            //    codes.Add(VM_36_PC[i]);
            //}

            tdata.Add(0x36);
            tdata.Add(0x00);
            tdata.AddRange(BitConverter.GetBytes((ushort)VM_36_PC.Length));
            tdata.AddRange(VM_36_PC);

            //tdata.Add(0x36);
            //tdata.Add(0x01);
            //tdata.AddRange(BitConverter.GetBytes((ushort)VM_36_PC.Length));
            //tdata.AddRange(VM_36_PS4);

            //tdata.Add(0x37);
            //tdata.Add(0x00);
            //tdata.AddRange(BitConverter.GetBytes((ushort)VM_37.Length));
            //tdata.AddRange(VM_37);

            //tdata.Add(0x38);
            //tdata.Add(0x00);
            //tdata.AddRange(BitConverter.GetBytes((ushort)VM_38.Length));
            //tdata.AddRange(VM_38);
            File.WriteAllBytes("vm_codes.db2", tdata.ToArray());
        }
    }
}
