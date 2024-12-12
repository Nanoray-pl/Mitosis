using System.Diagnostics.CodeAnalysis;

namespace Nanoray.Mitosis;

/// <summary>
/// Defines a type capable of deep cloning arbitrary values.
/// </summary>
public interface ICloneEngine
{
	/// <summary>
	/// Attempts to clone the given value.
	/// </summary>
	/// <param name="original">The value to clone.</param>
	/// <param name="clone">The cloned value, if succeeded.</param>
	/// <typeparam name="T">The type of value to clone.</typeparam>
	/// <returns>Whether cloning was successful.</returns>
	bool TryClone<T>(T original, [MaybeNullWhen(false)] out T clone)
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
	
	/// <summary>
	/// Clones the given value.
	/// </summary>
	/// <param name="original">The value to clone.</param>
	/// <typeparam name="T">The type of value to clone.</typeparam>
	/// <returns>The cloned value.</returns>
	T Clone<T>(T original);
}
