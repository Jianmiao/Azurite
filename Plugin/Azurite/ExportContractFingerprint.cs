using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Azurite;

internal static class ExportContractFingerprint
{
	private static readonly HashSet<string> VerifiedDigests = new HashSet<string>(StringComparer.Ordinal) { "92F4FEAB5721A30CB87F76C0D636CEEA803351D8F25B9348B811D41B3A71854F", "17CAD28FF57DD269FEA5AFE68EDED77D10E971C7F5CDB690FC2317E96CDBFFB3" };

	private const string Prefix = "AAVideoExport.Plugin.";

	private static readonly (string Type, string Method)[] RequiredMethods = new(string, string)[14]
	{
		("ExportHost", "Prepare"),
		("ExportHost", "Begin"),
		("ExportHost", "get_Capturing"),
		("ExportHost", "get_Busy"),
		("NativeExportPanel", "get_Visible"),
		("NativeExportPanel", "set_Visible"),
		("NativeExportPanel", "ApplyIdleBudget"),
		("NativeExportPanel", "ReleaseIdleBudget"),
		("NativeExportClock", "get_IsActive"),
		("NativeExportClock", "Begin"),
		("NativeExportClock", "Restore"),
		("NativeCaptureScope", ".ctor"),
		("NativeCaptureScope", "Dispose"),
		("NativeCaptureScope", "Restore")
	};

	private static readonly HashSet<string> HostState = new HashSet<string>(StringComparer.Ordinal) { "Current", "_panel", "_native", "_prepare", "_finalize", "_cleanup", "_armed", "_nativeLoadPending", "_returningToCatalog" };

	public static bool TryVerify(string path, Guid loadedMvid, out string digest)
	{
		digest = string.Empty;
		try
		{
			using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(path, new ReaderParameters
			{
				ReadingMode = ReadingMode.Immediate,
				ReadSymbols = false
			});
			if (assemblyDefinition.Name.Name != "AAVideoExport" || assemblyDefinition.MainModule.Mvid != loadedMvid)
			{
				return false;
			}
			digest = Compute(assemblyDefinition);
			return VerifiedDigests.Contains(digest);
		}
		catch
		{
			return false;
		}
	}

	internal static string Compute(AssemblyDefinition assembly)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Describe(assembly))));
	}

	internal static string Describe(AssemblyDefinition assembly)
	{
		TypeDefinition[] types = AllTypes(assembly.MainModule.Types).ToArray();
		MethodDefinition[] array = types.SelectMany((TypeDefinition t) => t.Methods).ToArray();
		Dictionary<string, MethodDefinition> dictionary = array.ToDictionary<MethodDefinition, string>((MethodDefinition m) => m.FullName, StringComparer.Ordinal);
		HashSet<MethodDefinition> hashSet = new HashSet<MethodDefinition>();
		List<FieldDefinition> fields = new List<FieldDefinition>();
		RequireField("ExportHost", "Current");
		RequireField("ExportHost", "_panel");
		RequireField("NativeExportPanel", "_idleBudget");
		RequireField("NativeExportPanel", "_idleFps");
		RequireField("NativeExportPanel", "_idleVsync");
		RequireField("NativeCaptureScope", "_fps");
		RequireField("NativeCaptureScope", "_vsync");
		(string, string)[] requiredMethods = RequiredMethods;
		for (int num = 0; num < requiredMethods.Length; num++)
		{
			(string, string) tuple = requiredMethods[num];
			string typeName = tuple.Item1;
			string methodName = tuple.Item2;
			TypeDefinition typeDefinition = types.Single((TypeDefinition t) => t.FullName == "AAVideoExport.Plugin." + typeName);
			hashSet.Add(typeDefinition.Methods.Single((MethodDefinition m) => m.Name == methodName));
		}
		MethodDefinition[] array2 = array;
		foreach (MethodDefinition methodDefinition in array2)
		{
			if (methodDefinition.HasBody && methodDefinition.Body.Instructions.Any(IsOwnershipWrite))
			{
				hashSet.Add(methodDefinition);
			}
		}
		Queue<MethodDefinition> queue = new Queue<MethodDefinition>(hashSet);
		while (queue.Count > 0)
		{
			MethodDefinition methodDefinition2 = queue.Dequeue();
			if (!methodDefinition2.HasBody)
			{
				throw new InvalidDataException("Ownership method has no IL body.");
			}
			foreach (Instruction instruction in methodDefinition2.Body.Instructions)
			{
				if (instruction.Operand is MethodReference methodReference && dictionary.TryGetValue(methodReference.FullName, out var value) && (value.Name.StartsWith("<", StringComparison.Ordinal) || value.DeclaringType.Name.StartsWith("<", StringComparison.Ordinal) || instruction.OpCode.Code == Code.Ldftn || instruction.OpCode.Code == Code.Ldvirtftn) && hashSet.Add(value))
				{
					queue.Enqueue(value);
				}
			}
			if (hashSet.Count > 256)
			{
				throw new InvalidDataException("Export ownership graph exceeds verified bounds.");
			}
		}
		StringBuilder stringBuilder = new StringBuilder("Azurite.ExportOwnership.v1\n");
		foreach (FieldDefinition item in fields.OrderBy<FieldDefinition, string>((FieldDefinition f) => f.FullName, StringComparer.Ordinal))
		{
			stringBuilder.Append("FIELD|").Append(item.FullName).Append('|')
				.Append((int)item.Attributes)
				.Append('\n');
		}
		foreach (MethodDefinition item2 in hashSet.OrderBy<MethodDefinition, string>((MethodDefinition m) => m.FullName, StringComparer.Ordinal))
		{
			stringBuilder.Append("METHOD|").Append(item2.FullName).Append('|')
				.Append((int)item2.Attributes)
				.Append('|')
				.Append((int)item2.ImplAttributes)
				.Append('|')
				.Append(item2.Body.InitLocals)
				.Append('\n');
			foreach (VariableDefinition variable in item2.Body.Variables)
			{
				stringBuilder.Append("LOCAL|").Append(variable.Index).Append('|')
					.Append(variable.VariableType.FullName)
					.Append('\n');
			}
			Collection<Instruction> instructions = item2.Body.Instructions;
			foreach (Instruction item3 in instructions)
			{
				stringBuilder.Append(item3.OpCode.Name).Append('|').Append(Operand(item3.Operand, instructions))
					.Append('\n');
			}
			foreach (ExceptionHandler exceptionHandler in item2.Body.ExceptionHandlers)
			{
				stringBuilder.Append("EH|").Append(exceptionHandler.HandlerType).Append('|')
					.Append(exceptionHandler.CatchType?.FullName)
					.Append('|')
					.Append(Index(exceptionHandler.TryStart, instructions))
					.Append('|')
					.Append(Index(exceptionHandler.TryEnd, instructions))
					.Append('|')
					.Append(Index(exceptionHandler.HandlerStart, instructions))
					.Append('|')
					.Append(Index(exceptionHandler.HandlerEnd, instructions))
					.Append('|')
					.Append(Index(exceptionHandler.FilterStart, instructions))
					.Append('\n');
			}
		}
		return stringBuilder.ToString();
		void RequireField(string text, string name)
		{
			TypeDefinition typeDefinition2 = types.Single((TypeDefinition t) => t.FullName == "AAVideoExport.Plugin." + text);
			fields.Add(typeDefinition2.Fields.Single((FieldDefinition f) => f.Name == name));
		}
	}

	private static bool IsOwnershipWrite(Instruction instruction)
	{
		FieldReference fieldReference = instruction.Operand as FieldReference;
		bool flag = fieldReference != null;
		bool flag2;
		if (flag)
		{
			Code code = instruction.OpCode.Code;
			flag2 = (((uint)(code - 121) <= 1u || (uint)(code - 124) <= 1u) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			string fullName = fieldReference.DeclaringType.FullName;
			if (fullName == "AAVideoExport.Plugin.ExportHost" && HostState.Contains(fieldReference.Name))
			{
				return true;
			}
			flag = fullName == "AAVideoExport.Plugin.NativeExportPanel";
			if (flag)
			{
				flag2 = fieldReference.Name.StartsWith("_idle", StringComparison.Ordinal);
				if (!flag2)
				{
					string name = fieldReference.Name;
					bool flag3 = ((name == "_visible" || name == "_busy") ? true : false);
					flag2 = flag3;
				}
				flag = flag2;
			}
			if (flag)
			{
				return true;
			}
			if (fullName == "AAVideoExport.Plugin.NativeExportClock" && fieldReference.Name == "<IsActive>k__BackingField")
			{
				return true;
			}
		}
		if (!(instruction.Operand is MethodReference methodReference))
		{
			return false;
		}
		string fullName2 = methodReference.DeclaringType.FullName;
		flag = fullName2 == "UnityEngine.Application";
		if (flag)
		{
			string name = methodReference.Name;
			bool flag3 = ((name == "set_targetFrameRate" || name == "set_runInBackground") ? true : false);
			flag = flag3;
		}
		flag2 = flag || (fullName2 == "UnityEngine.QualitySettings" && methodReference.Name == "set_vSyncCount") || (fullName2 == "UnityEngine.Rendering.OnDemandRendering" && methodReference.Name == "set_renderFrameInterval") || (fullName2 == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset" && methodReference.Name == "set_renderScale");
		if (!flag2)
		{
			bool flag3 = fullName2 == "UnityEngine.Time";
			if (flag3)
			{
				bool flag4;
				switch (methodReference.Name)
				{
				case "set_timeScale":
				case "set_captureDeltaTime":
				case "set_captureFramerate":
				case "set_fixedDeltaTime":
					flag4 = true;
					break;
				default:
					flag4 = false;
					break;
				}
				flag3 = flag4;
			}
			flag2 = flag3;
		}
		return flag2;
	}

	private static string Operand(object? operand, Collection<Instruction> instructions)
	{
		if (operand != null)
		{
			if (!(operand is Instruction target))
			{
				if (!(operand is Instruction[] source))
				{
					if (!(operand is MemberReference { FullName: var fullName }))
					{
						if (!(operand is VariableDefinition variableDefinition))
						{
							if (!(operand is ParameterDefinition parameterDefinition))
							{
								if (!(operand is CallSite { FullName: var fullName2 }))
								{
									if (!(operand is string s))
									{
										if (operand is IFormattable formattable)
										{
											return formattable.ToString(null, CultureInfo.InvariantCulture);
										}
										throw new InvalidDataException("Unsupported IL operand: " + operand.GetType().FullName);
									}
									return "string:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
								}
								return fullName2;
							}
							return "arg:" + parameterDefinition.Index + ":" + parameterDefinition.ParameterType.FullName;
						}
						return "local:" + variableDefinition.Index;
					}
					return fullName;
				}
				return "switch:" + string.Join(",", source.Select((Instruction t) => Index(t, instructions)));
			}
			return "branch:" + Index(target, instructions);
		}
		return string.Empty;
	}

	private static int Index(Instruction? target, Collection<Instruction> instructions)
	{
		if (target != null)
		{
			return instructions.IndexOf(target);
		}
		return -1;
	}

	private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
	{
		foreach (TypeDefinition type in types)
		{
			yield return type;
			foreach (TypeDefinition item in AllTypes(type.NestedTypes))
			{
				yield return item;
			}
		}
	}
}
