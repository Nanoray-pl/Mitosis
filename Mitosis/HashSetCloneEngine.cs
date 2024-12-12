using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Nanoray.Mitosis;

/// <summary>
/// A specialized <see cref="ICloneEngine"/> for cloning <see cref="HashSet{T}"/>s.
/// </summary>
/// <param name="valueEngine">A clone engine to use for cloning values.</param>
public sealed class HashSetCloneEngine(ICloneEngine valueEngine) : ICloneEngine
{
	private static readonly MethodInfo GenericHashSetCloneMethod = typeof(HashSetCloneEngine).GetMethod(nameof(CloneHashSet), BindingFlags.Instance | BindingFlags.NonPublic)!;
	private readonly Dictionary<Type, Delegate> DelegateCache = []; // delegate is Func<HashSetCloneEngine, T, T>
	
	/// <inheritdoc/>
	public bool TryClone<T>(T original, [MaybeNullWhen(false)] out T clone)
	{
		if (original is null)
		{
			clone = default;
			return false;
		}
		
		var originalType = original.GetType();
		if (!originalType.IsConstructedGenericType || originalType.GetGenericTypeDefinition() != typeof(HashSet<>))
		{
			clone = default;
			return false;
		}
		
		var elementType = originalType.GetGenericArguments()[0];
		if (!this.DelegateCache.TryGetValue(elementType, out var @delegate))
		{
			var method = GenericHashSetCloneMethod.MakeGenericMethod(elementType);
			@delegate = method.CreateDelegate<Func<HashSetCloneEngine, T, T>>();
			this.DelegateCache[elementType] = @delegate;
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
