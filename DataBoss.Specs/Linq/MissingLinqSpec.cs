using Cone;
using DataBoss.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DataBoss.Specs.Linq
{
	[Describe(typeof(MissingLinq))]
	public class MissingLinqSpec
	{
		public void Batch() {
			var batchSize = 3;
			var items = new[] { 1, 2, 3, 4, 5 };
			Check.With(() => items.Batch(batchSize).ToList())
			.That(
				xs => xs[0].Count() == batchSize,
				xs => xs[0].SequenceEqual(items.Take(batchSize)),
				xs => xs[1].Count() == items.Length - batchSize,
				xs => xs[1].SequenceEqual(items.Skip(batchSize)));
		}

		public void Batch_with_own_bucket() {
			var items = new[] { "A", "B", "C", "D" };
			var myBucket = new string[2];

			Check.With(() => items.Batch(() => myBucket))
			.That(
				xs => xs.First().Count() == myBucket.Length,
				xs => xs.First().SequenceEqual(items.Take(myBucket.Length)),
				xs => myBucket.SequenceEqual(items.Take(myBucket.Length)));
		}

		public void ChunkBy() {
			var items = new [] { 
				new { id = 1, value = "A" },
				new { id = 1, value = "B" },
				new { id = 2, value = "C" },
				new { id = 1, value = "D" },
			};

			Check.With(() => items.ChunkBy(x => x.id).ToList()).That(
				xs => xs.Count == 3,
				xs => xs[0].Count() == 2,
				xs => xs[0].Key == 1,
				xs => xs[0].ElementAt(0) == items[0],
				xs => xs[0].ElementAt(1) == items[1],
				xs => xs[1].Key == 2,
				xs => xs[1].Single() == items[2],
				xs => xs[2].Key == 1,
				xs => xs[2].Single() == items[3]);
		}

		public void ChunkBy_with_element_selector() { 
			var items = new [] { 
				new { id = 1 },
				new { id = 2 },
			};

			Check.With(() => items.ChunkBy(x => x.id, x => x.id.ToString()).ToList()).That(
				xs => xs.Count == 2,
				xs => xs[0].Key == 1,
				xs => xs[0].ElementAt(0) == items[0].id.ToString());
			}
	
		public void ChunkBy_grouping_is_collection() =>
			Check.With(() => new[] { 1, }.ChunkBy(x => x)).That(chunks => chunks.First() is ICollection<int>);

		public void Inspect() { 
			var items = new [] { "First", "Second", "Third" };
			var seen = new List<string>();

			items.Inspect(seen.Add).Consume();
			Check.That(() => seen.SequenceEqual(items));
		}

		public void SingleOrDefault_with_selector_no_element() =>
			Check.Exception<InvalidOperationException>(() => new[] { 1, 2, 3}.SingleOrDefault(x => x > 1, x => x.ToString()));

		public void SingleOrDefault_with_selector() =>
			Check.That(() => new[] { "A", "BB", "CCC"}.SingleOrDefault(x => x.StartsWith("C"), x => x.Length) == 3);
	}
}
