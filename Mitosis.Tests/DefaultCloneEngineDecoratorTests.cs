using NUnit.Framework;
using System.Runtime.CompilerServices;

namespace Nanoray.Mitosis.Tests;

[TestFixture]
internal sealed class DefaultCloneEngineDecoratorTests
{
	private class MutableClass { public int Number; }

	private sealed class ConditionalWeakTableListener(ConditionalWeakTable<object, Dictionary<string, object>> table) : IReferenceCloneListener
	{
		public void OnClone<T>(ICloneEngine engine, T source, T destination) where T : class
		{
			if (!table.TryGetValue(source, out var sourceDictionary))
				return;
			table.AddOrUpdate(destination, engine.Clone(sourceDictionary));
		}
	}

	[Test]
	public void TestConditionalWeakTableDecorator()
	{
		var table = new ConditionalWeakTable<object, Dictionary<string, object>>();
		var engine = new DefaultCloneEngine();
		engine.RegisterCloneListener(new ConditionalWeakTableListener(table));
		
		var original = new MutableClass { Number = 123 };
		table.AddOrUpdate(original, new Dictionary<string, object> { { "StringKey", 123 } });

		var copy = engine.Clone(original);
		Assert.IsTrue(table.TryGetValue(original, out var originalDictionary));
		Assert.IsTrue(table.TryGetValue(copy, out var copyDictionary));
		Assert.AreNotSame(originalDictionary, copyDictionary);
		Assert.AreEqual(1, originalDictionary!.Count);
		Assert.AreEqual(1, copyDictionary!.Count);
		Assert.AreEqual("StringKey", originalDictionary.Keys.First());
		Assert.AreEqual("StringKey", copyDictionary.Keys.First());
		Assert.AreEqual(123, originalDictionary.Values.First());
		Assert.AreEqual(123, copyDictionary.Values.First());
	}
}
