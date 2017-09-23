using Cone;
using DataBoss.Linq;
using System.Collections.Generic;
using System.Linq;

namespace DataBoss.Specs.Linq
{
	[Describe(typeof(MissingLinq))]
	public class MissingLinqSpec
	{
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

		public void Inspect() { 
			var items = new [] { "First", "Second", "Third" };
			var seen = new List<string>();

			items.Inspect(seen.Add).Consume();
			Check.That(() => seen.SequenceEqual(items));
		}
	}
}
