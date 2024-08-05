using NUnit.Framework;

namespace Nanoray.Mitosis.Tests;

[TestFixture]
internal sealed class DefaultCloneEngineTests
{
	private enum TestEnum { C = 2 }

	private record ImmutableRecordClass(int Number, string? Text);
	private record struct ImmutableRecordStruct(int Number, string? Text);
	private class MutableClass { public int Number; }
	private class SelfReferencing { public SelfReferencing? Reference; }
	
	private static void TestNotSame<T>(DefaultCloneEngine engine, T original)
		where T : class
	{
		var copy = engine.Clone(original);
		Assert.AreNotSame(original, copy);
	}
	
	private static void TestSame<T>(DefaultCloneEngine engine, T original)
	{
		var copy = engine.Clone(original);
		
		Assert.AreEqual(original, copy);
		if (!typeof(T).IsValueType)
			Assert.AreSame(original, copy);
	}

	[Test]
	public void TestPrimitives()
	{
		var engine = new DefaultCloneEngine();

		TestSame<object?>(engine, null);
		TestSame(engine, true);
		TestSame(engine, 123);
		TestSame(engine, 234L);
		TestSame(engine, 345f);
		TestSame(engine, 456.0);
		TestSame(engine, "asdf");
		TestSame(engine, TestEnum.C);
	}
	
	[Test]
	public void TestImmutable()
	{
		var engine = new DefaultCloneEngine();
		
		TestSame(engine, new ImmutableRecordClass(123, "asdf"));
		TestSame(engine, new ImmutableRecordStruct(123, "asdf"));
	}
	
	[Test]
	public void TestMutable()
	{
		var engine = new DefaultCloneEngine();
		
		TestNotSame(engine, new object());

		int[] intArray = [1, 2, 3];
		var intArrayCopy = engine.Clone(intArray);
		Assert.AreNotSame(intArray, intArrayCopy);
		Assert.AreEqual(intArray.Length, intArrayCopy.Length);
		Assert.IsTrue(intArray.SequenceEqual(intArrayCopy));

		List<int> intList = [1, 2, 3];
		var intListCopy = engine.Clone(intList);
		Assert.AreNotSame(intList, intListCopy);
		Assert.AreEqual(intList.Count, intListCopy.Count);
		Assert.IsTrue(intList.SequenceEqual(intListCopy));
		
		List<MutableClass> objectList = [new() { Number = 1 }, new() { Number = 2 }, new() { Number = 3 }];
		var objectListCopy = engine.Clone(objectList);
		Assert.AreNotSame(objectList, objectListCopy);
		Assert.AreEqual(objectList.Count, objectListCopy.Count);
		Assert.IsTrue(objectList.Select(o => o.Number).SequenceEqual(objectListCopy.Select(o => o.Number)));
	}

	[Test]
	public void TestMultipleReferences()
	{
		var engine = new DefaultCloneEngine();
		
		var obj = new MutableClass { Number = 123 };
		List<MutableClass> list = [obj, obj, obj];
		var listCopy = engine.Clone(list);
		
		Assert.AreNotSame(list, listCopy);
		Assert.AreEqual(list.Count, listCopy.Count);
		Assert.AreSame(listCopy[0], listCopy[1]);
		Assert.AreSame(listCopy[0], listCopy[2]);
	}

	[Test]
	public void TestReferenceCycle()
	{
		var engine = new DefaultCloneEngine();

		var obj = new SelfReferencing();
		obj.Reference = obj;
		var copy = engine.Clone(obj);
		
		Assert.AreNotSame(obj, copy);
		Assert.AreSame(copy.Reference, copy);
	}
}
