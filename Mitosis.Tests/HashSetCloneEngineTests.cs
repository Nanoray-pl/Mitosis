using NUnit.Framework;

namespace Nanoray.Mitosis.Tests;

[TestFixture]
public sealed class HashSetCloneEngineTests
{
	[Test]
	public void TestInts()
	{
		var valueEngine = new DefaultCloneEngine();
		var engine = new HashSetCloneEngine(valueEngine);
		
		var obj = new HashSet<int> { 1, 2, 3 };
		var copy = engine.Clone(obj);

		Assert.AreNotSame(obj, copy);
		Assert.IsTrue(obj.SetEquals(copy));
	}
}
