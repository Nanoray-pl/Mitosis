using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace Nanoray.Mitosis;

/// <summary>
/// A specialized <see cref="ICloneEngine"/> for cloning <see cref="HashSet{T}"/>s.
/// </summary>
/// <param name="valueEngine">A clone engine to use for cloning values.</param>
public sealed class HashSetCloneEngine(ICloneEngine valueEngine) : ICloneEngine
{
	private static readonly MethodInfo GenericHashSetCloneMethod = typeof(HashSetCloneEngine).GetMethod(nameof(CloneHashSet), BindingFlags.Instance | BindingFlags.NonPublic)!;
	private readonly Dictionary<(Type VariableType, Type RealType), Delegate> DelegateCache = []; // delegate is Func<HashSetCloneEngine, T, T>
	
	/// <inheritdoc/>
	public bool TryClone<T>(T original, [MaybeNullWhen(false)] out T clone)
	{
		if (original is null)
		{
			clone = default;
			return false;
		}
		
		var variableType = typeof(T);
		var realType = original.GetType();
		if (!realType.IsConstructedGenericType || realType.GetGenericTypeDefinition() != typeof(HashSet<>))
		{
			clone = default;
			return false;
		}
		
		var elementType = realType.GetGenericArguments()[0];
		if (!this.DelegateCache.TryGetValue((variableType, realType), out var @delegate))
		{
			var cloneMethod = GenericHashSetCloneMethod.MakeGenericMethod(elementType);
			if (typeof(T) == cloneMethod.ReturnType)
			{
				@delegate = cloneMethod.CreateDelegate<Func<HashSetCloneEngine, T, T>>();
			}
			else
			{
				var dynamicMethod = new DynamicMethod($"CloneHashSet{elementType}", typeof(T), [typeof(HashSetCloneEngine), typeof(T)]);
				var il = dynamicMethod.GetILGenerator();

				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Castclass, realType);
				il.Emit(OpCodes.Call, cloneMethod);
				il.Emit(OpCodes.Castclass, typeof(T));
				il.Emit(OpCodes.Ret);
				
				@delegate = dynamicMethod.CreateDelegate<Func<HashSetCloneEngine, T, T>>();
			}
			this.DelegateCache[(variableType, realType)] = @delegate;
		}

		clone = ((Func<HashSetCloneEngine, T, T>)@delegate).Invoke(this, original);
		return true;
	}
	
	/// <inheritdoc/>
	public T Clone<T>(T original)
	{
		if (this.TryClone(original, out var clone))
			return clone;
		throw new ArgumentException($"Unsupported cloned value `{original}`");
	}

	private HashSet<T> CloneHashSet<T>(HashSet<T> original)
		=> new(original.Select(valueEngine.Clone), original.Comparer);
}
