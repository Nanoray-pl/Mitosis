using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Nanoray.Mitosis;

/// <summary>
/// An <see cref="ICloneEngine"/> which utilizes reflection and emitted IL code to perform deep cloning.
/// It also passes all cloned reference types to the registered <see cref="ICloneListener"/>s.
/// </summary>
/// <remarks>
/// Immutable and value types are passed through with no cloning.<br/>
/// Repeats of the same mutable reference types will also return the same cloned reference, keeping the object hierarchy in-tact.
/// </remarks>
public sealed class DefaultCloneEngine : ICloneEngine
{
	private delegate T CloneDelegate<T>(DefaultCloneEngine engine, T value);

	private static readonly FieldInfo TrackedCopiesField = typeof(DefaultCloneEngine).GetField(nameof(TrackedCopies), BindingFlags.Instance | BindingFlags.NonPublic)!;
	private static readonly FieldInfo CloneListenersField = typeof(DefaultCloneEngine).GetField(nameof(CloneListeners), BindingFlags.Instance | BindingFlags.NonPublic)!;
	private static readonly FieldInfo ReferenceCloneListenersField = typeof(DefaultCloneEngine).GetField(nameof(ReferenceCloneListeners), BindingFlags.Instance | BindingFlags.NonPublic)!;
	private static readonly MethodInfo GetTypeFromHandleMethod = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
	private static readonly MethodInfo GetUninitializedObjectMethod = typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.GetUninitializedObject))!;
	private static readonly MethodInfo ObjectObjectDictionaryTryGetValueMethod = typeof(Dictionary<object, object>).GetMethod(nameof(Dictionary<object, object>.TryGetValue))!;
	private static readonly MethodInfo ObjectObjectDictionarySetItemMethod = typeof(Dictionary<object, object>).GetMethod("set_Item")!;
	private static readonly MethodInfo CloneDecoratorListGetItemMethod = typeof(List<ICloneListener>).GetMethod("get_Item")!;
	private static readonly MethodInfo ReferenceCloneDecoratorListGetItemMethod = typeof(List<IReferenceCloneListener>).GetMethod("get_Item")!;
	
	private readonly List<ICloneListener> CloneListeners = [];
	private readonly List<IReferenceCloneListener> ReferenceCloneListeners = [];
	private readonly Dictionary<Type, Delegate> CompiledCloneDelegates = []; // delegate is CloneDelegate<T>
	private readonly Dictionary<(Type Supertype, Type Subtype), Delegate> CorrectedTypeCloneDelegates = []; // delegate is CloneDelegate<T>
	private readonly Dictionary<Type, bool> IsImmutableMap = [];
	private readonly HashSet<Type> IsImmutableInProgress = [];
	private Dictionary<object, object>? TrackedCopies;

	/// <summary>
	/// Registers an <see cref="IReferenceCloneListener"/> which gets passed all cloned reference types.
	/// </summary>
	/// <param name="listener">The listener.</param>
	public void RegisterCloneListener(IReferenceCloneListener listener)
	{
		this.ReferenceCloneListeners.Add(listener);
		if (listener is ICloneListener anyListener)
			this.CloneListeners.Add(anyListener);
		
		this.CompiledCloneDelegates.Clear();
	}
	
	/// <inheritdoc/>
	public T Clone<T>(T value)
	{
		if (value is null)
			return default!;

		var type = value.GetType();
		if (this.IsImmutable(type))
			return value;

		return ((CloneDelegate<T>)this.ObtainCorrectedTypeCloneDelegate<T>(value)).Invoke(this, value);
	}

	private T CorrectedTypeClone<T>(T value)
	{
		if (value is null)
			return default!;

		var type = value.GetType();
		if (this.IsImmutable(type))
			return value;

		var isRootCall = this.TrackedCopies is null;
		if (isRootCall)
			this.TrackedCopies = [];

		try
		{
			return ((CloneDelegate<T>)this.ObtainCompiledCloneDelegate(type)).Invoke(this, value);
		}
		finally
		{
			if (isRootCall)
				this.TrackedCopies = null;
		}
	}

	private Delegate ObtainCompiledCloneDelegate(Type type)
	{
		if (!this.CompiledCloneDelegates.TryGetValue(type, out var @delegate))
		{
			@delegate = this.CreateDelegate(type);
			this.CompiledCloneDelegates[type] = @delegate;
		}
		return @delegate;
	}

	private Delegate ObtainCorrectedTypeCloneDelegate<T>(T value)
	{
		var supertype = typeof(T);
		var subtype = value!.GetType();
		var key = (Supertype: supertype, Subtype: subtype);
		
		if (!this.CorrectedTypeCloneDelegates.TryGetValue(key, out var @delegate))
		{
			var method = new DynamicMethod("CorrectedTypeClone", supertype, [typeof(DefaultCloneEngine), supertype]);
			var il = method.GetILGenerator();

			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.CorrectedTypeClone), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(subtype));
			if (supertype == typeof(object) && subtype.IsValueType)
				il.Emit(OpCodes.Box, subtype);
			il.Emit(OpCodes.Ret);
			
			@delegate = method.CreateDelegate<CloneDelegate<T>>();
			this.CorrectedTypeCloneDelegates[key] = @delegate;
		}
		return @delegate;
	}

	private bool IsImmutable(Type type)
	{
		if (!this.IsImmutableMap.TryGetValue(type, out var isImmutable))
		{
			isImmutable = this.ComputeIsImmutable(type);
			this.IsImmutableMap[type] = isImmutable;
		}
		return isImmutable;
	}

	private bool ComputeIsImmutable(Type type)
	{
		if (type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(string))
			return true;
		if (!type.IsValueType && type.GetMethod("<Clone>$") is null)
			return false;

		if (!this.IsImmutableInProgress.Add(type))
			return true;

		try
		{
			var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (fields.Any(field => !this.IsImmutable(field.FieldType)))
				return false;
			if (!type.IsValueType && fields.Any(field => !field.IsInitOnly))
				return false;
			return true;
		}
		finally
		{
			this.IsImmutableInProgress.Remove(type);
		}
	}

	// delegate is CloneDelegate<T>
	private Delegate CreateDelegate(Type type)
	{
		var method = new DynamicMethod($"Clone{type.FullName}", type, [typeof(DefaultCloneEngine), type]);
		var il = method.GetILGenerator();

		if (!type.IsValueType)
		{
			var noRecordedCopyLabel = il.DefineLabel();
			var recordedCopyLocal = il.DeclareLocal(type);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, TrackedCopiesField);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldloca, recordedCopyLocal);
			il.Emit(OpCodes.Call, ObjectObjectDictionaryTryGetValueMethod);
			il.Emit(OpCodes.Brfalse, noRecordedCopyLabel);
			il.Emit(OpCodes.Ldloc, recordedCopyLocal);
			il.Emit(OpCodes.Ret);
			il.MarkLabel(noRecordedCopyLabel);
		}

		var copyLocal = il.DeclareLocal(type);

		#region Initialize copy
		if (type.IsArray)
		{
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			
			var elementType = type.GetElementType()!;
			switch (type.GetArrayRank())
			{
				case 1:
					il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.CloneArray1D), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(elementType));
					break;
				case 2:
					il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.CloneArray2D), BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(elementType));
					break;
				default:
					throw new ArgumentException($"Unsupported type `{type.FullName}`");
			}
			
			il.Emit(OpCodes.Stloc, copyLocal);
		}
		else if (type.IsValueType)
		{
			il.Emit(OpCodes.Ldloca, copyLocal);
			il.Emit(OpCodes.Initobj, type);
		}
		else
		{
			if (type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic, []) is { } ctor)
			{
				il.Emit(OpCodes.Newobj, ctor);
			}
			else
			{
				il.Emit(OpCodes.Ldtoken, type);
				il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
				il.Emit(OpCodes.Call, GetUninitializedObjectMethod);
				il.Emit(OpCodes.Castclass, type);
			}
			
			il.Emit(OpCodes.Stloc, copyLocal);
		}
		#endregion
		
		#region Track reference type copy (arrays do it in their respective methods)
		if (type is { IsValueType: false, IsArray: false })
		{
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, TrackedCopiesField);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldloc, copyLocal);
			il.Emit(OpCodes.Call, ObjectObjectDictionarySetItemMethod);
		}
		#endregion
		
		#region Copy fields
		foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldfld, field);
			il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.Clone))!.MakeGenericMethod(field.FieldType));
			il.Emit(OpCodes.Stfld, field);
		}
		#endregion
		
		#region Call listeners
		if (type.IsValueType)
		{
			for (var i = 0; i < this.CloneListeners.Count; i++)
			{
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, CloneListenersField);
				il.Emit(OpCodes.Ldc_I4, i);
				il.Emit(OpCodes.Call, CloneDecoratorListGetItemMethod);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldloca, copyLocal);
				il.Emit(OpCodes.Callvirt, typeof(ICloneListener).GetMethod(nameof(ICloneListener.OnClone))!.MakeGenericMethod(type));
			}
		}
		else
		{
			for (var i = 0; i < this.ReferenceCloneListeners.Count; i++)
			{
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldfld, ReferenceCloneListenersField);
				il.Emit(OpCodes.Ldc_I4, i);
				il.Emit(OpCodes.Call, ReferenceCloneDecoratorListGetItemMethod);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldloc, copyLocal);
				il.Emit(OpCodes.Callvirt, typeof(IReferenceCloneListener).GetMethod(nameof(IReferenceCloneListener.OnClone))!.MakeGenericMethod(type));
			}
		}
		#endregion

		il.Emit(OpCodes.Ldloc, copyLocal);
		il.Emit(OpCodes.Ret);
		return method.CreateDelegate(typeof(CloneDelegate<>).MakeGenericType(type));
	}

	private T[] CloneArray1D<T>(T[] original)
	{
		var copy = new T[original.Length];
		this.TrackedCopies![original] = copy;
		for (var i = 0; i < original.Length; i++)
			copy[i] = this.Clone(original[i]);
		return copy;
	}

	private T[,] CloneArray2D<T>(T[,] original)
	{
		var firstLength = original.GetLength(0);
		var secondLength = original.GetLength(1);
		
		var copy = new T[firstLength, secondLength];
		this.TrackedCopies![original] = copy;
		for (var i = 0; i < firstLength; i++)
			for (var j = 0; j < secondLength; j++)
				copy[i, j] = this.Clone(original[i, j]);
		return copy;
	}
}
