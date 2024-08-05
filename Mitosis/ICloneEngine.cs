namespace Nanoray.Mitosis;

/// <summary>
/// Defines a type capable of deep cloning arbitrary values.
/// </summary>
public interface ICloneEngine
{
	/// <summary>
	/// Clones the given value recursively.
	/// </summary>
	/// <param name="value">The value to clone.</param>
	/// <typeparam name="T">The type of value to clone.</typeparam>
	/// <returns>The cloned value.</returns>
	T Clone<T>(T value);
}
