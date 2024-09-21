using NUnit.Framework;

namespace Nanoray.Mitosis.Tests;

[TestFixture]
internal sealed class DefaultCloneEngineFieldFilterTests
{
	private sealed class TestClass
	{
		private static int NextId = 1;

		public int Id = NextId++;
		public TestClass? Child;
		public TestData? ToIgnore = new();
	}

	private sealed class TestData;
	
	[Test]
	public void TestCopyValue()
	{
		var engine = new DefaultCloneEngine();
		engine.RegisterFieldFilter(f => f.Name == "Id" ? DefaultCloneEngineFieldFilterBehavior.DoNotInitialize : DefaultCloneEngineFieldFilterBehavior.Clone);
		engine.RegisterFieldFilter(f => f.Name == "ToIgnore" ? DefaultCloneEngineFieldFilterBehavior.CopyValue : DefaultCloneEngineFieldFilterBehavior.Clone);

		var original = new TestClass { Child = new(), ToIgnore = new() };

		var copy = engine.Clone(original);
		Assert.IsNotNull(copy.Child);
		Assert.IsNotNull(copy.ToIgnore);
		Assert.AreNotSame(original.Child, copy.Child);
		Assert.AreSame(original.ToIgnore, copy.ToIgnore);
		Assert.AreNotEqual(original.Id, copy.Id);
		Assert.AreNotEqual(original.Child.Id, copy.Child!.Id);
	}
	
	[Test]
	public void TestDoNotInitialize()
	{
		var engine = new DefaultCloneEngine();
		engine.RegisterFieldFilter(f => f.Name == "Id" ? DefaultCloneEngineFieldFilterBehavior.DoNotInitialize : DefaultCloneEngineFieldFilterBehavior.Clone);
		engine.RegisterFieldFilter(f => f.Name == "ToIgnore" ? DefaultCloneEngineFieldFilterBehavior.DoNotInitialize : DefaultCloneEngineFieldFilterBehavior.Clone);

		var original = new TestClass { Child = new(), ToIgnore = new() };

		var copy = engine.Clone(original);
		Assert.IsNotNull(copy.Child);
		Assert.IsNotNull(copy.ToIgnore);
		Assert.AreNotSame(original.Child, copy.Child);
		Assert.AreNotSame(original.ToIgnore, copy.ToIgnore);
		Assert.AreNotEqual(original.Id, copy.Id);
		Assert.AreNotEqual(original.Child.Id, copy.Child!.Id);
	}
	
	[Test]
	public void TestAssignDefault()
	{
		var engine = new DefaultCloneEngine();
		engine.RegisterFieldFilter(f => f.Name == "Id" ? DefaultCloneEngineFieldFilterBehavior.DoNotInitialize : DefaultCloneEngineFieldFilterBehavior.Clone);
		engine.RegisterFieldFilter(f => f.Name == "ToIgnore" ? DefaultCloneEngineFieldFilterBehavior.AssignDefault : DefaultCloneEngineFieldFilterBehavior.Clone);

		var original = new TestClass { Child = new(), ToIgnore = new() };

		var copy = engine.Clone(original);
		Assert.IsNotNull(copy.Child);
		Assert.IsNull(copy.ToIgnore);
		Assert.AreNotSame(original.Child, copy.Child);
		Assert.AreNotSame(original.ToIgnore, copy.ToIgnore);
		Assert.AreNotEqual(original.Id, copy.Id);
		Assert.AreNotEqual(original.Child.Id, copy.Child!.Id);
	}
}
