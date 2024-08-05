namespace Nanoray.Mitosis;

/// <summary>
/// Defines a type which gets passed all values cloned by an <see cref="ICloneEngine"/> (for example <see cref="DefaultCloneEngine"/>).
/// </summary>
public interface ICloneListener : IReferenceCloneListener
{
	/// <summary>
	/// Called when any value gets cloned by an <see cref="ICloneEngine"/>.
	/// </summary>
	/// <param name="engine">The clone engine.</param>
	/// <param name="source">The original value.</param>
	/// <param name="destination">The cloned value.</param>
	/// <typeparam name="T">The type of cloned value.</typeparam>
	void Decorate<T>(ICloneEngine engine, T source, ref T destination);

	void IReferenceCloneListener.Decorate<T>(ICloneEngine engine, T source, T destination) where T : class
		=> this.Decorate(engine, source, ref destination);
}
