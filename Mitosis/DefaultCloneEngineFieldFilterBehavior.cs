namespace Nanoray.Mitosis;

/// <summary>
/// Describes what to do when a field is to be cloned.
/// </summary>
public enum DefaultCloneEngineFieldFilterBehavior
{
	/// <summary>Clone this field's value as usual, unless another filter says otherwise.</summary>
	Clone,
	
	/// <summary>Copy the value of the field as-is, whether it is a reference or a value type.</summary>
	CopyValue,
	
	/// <summary>Ignore the field altogether. It could have been initialized via a parameterless constructor, or kept uninitialized otherwise.</summary>
	DoNotInitialize,
	
	/// <summary>Use the default value of the field.</summary>
	AssignDefault
}
