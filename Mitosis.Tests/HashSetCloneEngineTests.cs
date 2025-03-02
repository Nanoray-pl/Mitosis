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
	
	[Test]
	public void TestIntsWithInterface()
	{
		var valueEngine = new DefaultCloneEngine();
		var engine = new HashSetCloneEngine(valueEngine);
		
		ISet<int> obj = new HashSet<int> { 1, 2, 3 };
		var copy = engine.Clone(obj);

		Assert.AreNotSame(obj, copy);
		Assert.IsTrue(obj.SetEquals(copy));
	}
	
	[Test]
	public void TestIntsWithInterfaceAndNot()
	{
		var valueEngine = new DefaultCloneEngine();
		var engine = new HashSetCloneEngine(valueEngine);
		
		ISet<int> obj1 = new HashSet<int> { 1, 2, 3 };
		var copy1 = engine.Clone(obj1);

		Assert.AreNotSame(obj1, copy1);
		Assert.IsTrue(obj1.SetEquals(copy1));
		
		var obj2 = new HashSet<int> { 1, 2, 3 };
		var copy2 = engine.Clone(obj2);

		Assert.AreNotSame(obj2, copy2);
		Assert.IsTrue(obj2.SetEquals(copy2));
	}
}
