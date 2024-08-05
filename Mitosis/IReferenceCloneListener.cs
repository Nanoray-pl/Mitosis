namespace Nanoray.Mitosis;

/// <summary>
/// Defines a type which gets passed all reference types cloned by an <see cref="ICloneEngine"/> (for example <see cref="DefaultCloneEngine"/>).
/// </summary>
public interface IReferenceCloneListener
{
	/// <summary>
	/// Called when any reference type gets cloned by an <see cref="ICloneEngine"/>.
	/// </summary>
	/// <param name="engine">The clone engine.</param>
	/// <param name="source">The original value.</param>
	/// <param name="destination">The cloned value.</param>
	/// <typeparam name="T">The type of cloned value.</typeparam>
	void OnClone<T>(ICloneEngine engine, T source, T destination) where T : class;
}
