using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

	private static readonly FieldInfo TrackedCopiesField = typeof(DefaultCloneEngine).GetField(nameof(TrackedCopies), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
	private static readonly MethodInfo ObtainCorrectedTypeCloneDelegateMethod = typeof(DefaultCloneEngine).GetMethod(nameof(ObtainCorrectedTypeCloneDelegate), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
	private static readonly MethodInfo CallCloneListenersMethod = typeof(DefaultCloneEngine).GetMethod(nameof(CallCloneListeners), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
	private static readonly MethodInfo CallReferenceTypeCloneListenersMethod = typeof(DefaultCloneEngine).GetMethod(nameof(CallReferenceTypeCloneListeners), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
	private static readonly MethodInfo GetTypeFromHandleMethod = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
	private static readonly MethodInfo GetUninitializedObjectMethod = typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.GetUninitializedObject))!;
	private static readonly MethodInfo ObjectObjectDictionaryTryGetValueMethod = typeof(Dictionary<object, object>).GetMethod(nameof(Dictionary<object, object>.TryGetValue))!;
	private static readonly MethodInfo ObjectObjectDictionarySetItemMethod = typeof(Dictionary<object, object>).GetMethod("set_Item")!;

	private readonly List<ICloneEngine> SpecializedEngines = [];
	private readonly List<Func<FieldInfo, DefaultCloneEngineFieldFilterBehavior>> FieldFilters = [];
	private readonly List<ICloneListener> CloneListeners = [];
	private readonly List<IReferenceCloneListener> ReferenceCloneListeners = [];
	private readonly Dictionary<Type, Lazy<Delegate>> CompiledCloneDelegates = []; // delegate is CloneDelegate<T>
	private readonly Dictionary<(Type Supertype, Type Subtype), Delegate> CorrectedTypeCloneDelegates = []; // delegate is CloneDelegate<T>
	private readonly Dictionary<Type, Delegate> CallReferenceTypeCloneListenersDelegates = []; // delegate is Func<DefaultCloneEngine, T, T, T>
	private readonly Dictionary<Type, bool> IsImmutableMap = [];
	private readonly HashSet<Type> IsImmutableInProgress = [];
	private Dictionary<object, object>? TrackedCopies;

	/// <summary>
	/// Registers an <see cref="ICloneEngine"/> that can attempt to clone the value in a specialized way. 
	/// </summary>
	/// <param name="engine">The engine.</param>
	public void RegisterSpecializedEngine(ICloneEngine engine)
		=> this.SpecializedEngines.Add(engine);

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

	/// <summary>
	/// Registers a delegate that can exclude any fields from being cloned.
	/// </summary>
	/// <param name="filter">The filter delegate.</param>
	public void RegisterFieldFilter(Func<FieldInfo, DefaultCloneEngineFieldFilterBehavior> filter)
	{
		this.FieldFilters.Add(filter);
		this.CompiledCloneDelegates.Clear();
	}

	/// <inheritdoc/>
	public bool TryClone<T>(T original, [MaybeNullWhen(false)] out T clone)
	{
		try
		{
			clone = this.Clone(original);
			return true;
		}
		catch
		{
			clone = default;
			return false;
		}
	}
	
	/// <inheritdoc/>
	public T Clone<T>(T original)
	{
		foreach (var engine in this.SpecializedEngines)
		{
			if (!engine.TryClone(original, out var specializedClone))
				continue;
			return specializedClone;
		}
		
		if (original is null)
			return default!;
		return ((CloneDelegate<T>)this.ObtainCorrectedTypeCloneDelegate(original)).Invoke(this, original);
	}

	private T CorrectedTypeClone<T>(T original)
	{
		if (original is null)
			return default!;

		var type = original.GetType();
		if (this.IsImmutable(type))
			return original;

		var isRootCall = this.TrackedCopies is null;
		if (isRootCall)
			this.TrackedCopies = [];

		try
		{
			return ((CloneDelegate<T>)this.ObtainCompiledCloneDelegate(type)).Invoke(this, original);
		}
		finally
		{
			if (isRootCall)
				this.TrackedCopies = null;
		}
	}

	private T CallCloneListeners<T>(T original, T clone)
	{
		if (typeof(T).IsValueType)
			return this.CallValueTypeCloneListeners(original, clone);
		
		if (!this.CallReferenceTypeCloneListenersDelegates.TryGetValue(typeof(T), out var rawDelegate))
		{
			rawDelegate = CallReferenceTypeCloneListenersMethod.MakeGenericMethod(typeof(T)).CreateDelegate<Func<DefaultCloneEngine, T, T, T>>();
			this.CallReferenceTypeCloneListenersDelegates[typeof(T)] = rawDelegate;
		}
		
		var @delegate = (Func<DefaultCloneEngine, T, T, T>)rawDelegate;
		return @delegate(this, original, clone);
	}

	private T CallValueTypeCloneListeners<T>(T original, T clone)
	{
		foreach (var listener in this.CloneListeners)
			listener.OnClone(this, original, ref clone);
		return clone;
	}

	private T CallReferenceTypeCloneListeners<T>(T original, T clone) where T : class
	{
		foreach (var listener in this.ReferenceCloneListeners)
			listener.OnClone(this, original, clone);
		return clone;
	}

	private Delegate ObtainCompiledCloneDelegate(Type type)
	{
		if (!this.CompiledCloneDelegates.TryGetValue(type, out var @delegate))
		{
			@delegate = new(() => this.CreateDelegate(type));
			this.CompiledCloneDelegates[type] = @delegate;
		}
		return @delegate.Value;
	}

	private Delegate ObtainCorrectedTypeCloneDelegate<T>(T? value)
	{
		var supertype = typeof(T);
		var subtype = value is null || (typeof(T).IsConstructedGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>)) ? supertype : value.GetType();
		var key = (Supertype: supertype, Subtype: subtype);
		
		if (!this.CorrectedTypeCloneDelegates.TryGetValue(key, out var @delegate))
		{
			var method = new DynamicMethod("CorrectedTypeClone", supertype, [typeof(DefaultCloneEngine), supertype]);
			var il = method.GetILGenerator();

			{
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_1);
				if (supertype == typeof(object) && subtype.IsValueType)
					il.Emit(OpCodes.Unbox_Any, subtype);
				
				{
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldarg_1);
					if (supertype == typeof(object) && subtype.IsValueType)
						il.Emit(OpCodes.Unbox_Any, subtype);
					il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.CorrectedTypeClone), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.MakeGenericMethod(subtype));
				}
				
				il.Emit(OpCodes.Call, CallCloneListenersMethod.MakeGenericMethod(subtype));
			}
			
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
		if (type.IsAssignableTo(typeof(Delegate)) || type.IsAssignableTo(typeof(ContextBoundObject)))
			return true;
		if (!type.IsValueType && type.GetMethod("<Clone>$") is null)
			return false;

		if (!this.IsImmutableInProgress.Add(type))
			return true;

		try
		{
			var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
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
					il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.CloneArray1D), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.MakeGenericMethod(elementType));
					break;
				case 2:
					il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.CloneArray2D), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.MakeGenericMethod(elementType));
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
			if (type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, []) is { } ctor)
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
		foreach (var field in GetAllFields(type))
		{
			var behavior = DefaultCloneEngineFieldFilterBehavior.Clone;
			foreach (var filter in this.FieldFilters)
			{
				var alternateBehavior = filter(field);
				if (alternateBehavior != behavior)
				{
					behavior = alternateBehavior;
					break;
				}
			}

			switch (behavior)
			{
				case DefaultCloneEngineFieldFilterBehavior.Clone:
					_ = ObtainCorrectedTypeCloneDelegateMethod.MakeGenericMethod(field.FieldType).Invoke(this, [null]);
					il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldarg_1);
					il.Emit(OpCodes.Ldfld, field);
					il.Emit(OpCodes.Call, this.GetType().GetMethod(nameof(this.Clone))!.MakeGenericMethod(field.FieldType));
					il.Emit(OpCodes.Stfld, field);
					break;
				case DefaultCloneEngineFieldFilterBehavior.CopyValue:
					il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
					il.Emit(OpCodes.Ldarg_1);
					il.Emit(OpCodes.Ldfld, field);
					il.Emit(OpCodes.Stfld, field);
					break;
				case DefaultCloneEngineFieldFilterBehavior.DoNotInitialize:
					break;
				case DefaultCloneEngineFieldFilterBehavior.AssignDefault:
					if (!field.FieldType.IsValueType)
					{
						il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
						il.Emit(OpCodes.Ldnull);
						il.Emit(OpCodes.Stfld, field);
					}
					else if (!field.FieldType.IsPrimitive)
					{
						il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
						il.Emit(OpCodes.Ldflda, field);
						il.Emit(OpCodes.Initobj, field.FieldType);
					}
					else if (field.FieldType == typeof(float))
					{
						il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
						il.Emit(OpCodes.Ldc_R4, 0f);
						il.Emit(OpCodes.Stfld, field);
					}
					else if (field.FieldType == typeof(double))
					{
						il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
						il.Emit(OpCodes.Ldc_R8, 0.0);
						il.Emit(OpCodes.Stfld, field);
					}
					else if (Marshal.SizeOf(field.FieldType) <= 4)
					{
						il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
						il.Emit(OpCodes.Ldc_I4_0);
						il.Emit(OpCodes.Stfld, field);
					}
					else if (Marshal.SizeOf(field.FieldType) == 8)
					{
						il.Emit(type.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, copyLocal);
						il.Emit(OpCodes.Conv_I8);
						il.Emit(OpCodes.Stfld, field);
					}
					else
					{
						throw new ArgumentException($"Unsupported field type {field.FieldType}");
					}
					break;
				default:
					throw new InvalidOperationException($"Invalid {nameof(DefaultCloneEngineFieldFilterBehavior)}");
			}
		}
		#endregion

		il.Emit(OpCodes.Ldloc, copyLocal);
		il.Emit(OpCodes.Ret);
		return method.CreateDelegate(typeof(CloneDelegate<>).MakeGenericType(type));

		static IEnumerable<FieldInfo> GetAllFields(Type type)
		{
			while (true)
			{
				if (type == typeof(object))
					break;
				foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					yield return field;
				if (type.BaseType is not { } baseType)
					break;
				type = baseType;
			}
		}
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
