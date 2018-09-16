﻿using System;

namespace DataBoss.Specs.Data
{
	public struct IdOf<T> : IEquatable<IdOf<T>>
	{
		readonly int id;

		public IdOf(int id) { this.id = id; }

		public override string ToString() => id.ToString();
		public override int GetHashCode() => id.GetHashCode();

		public override bool Equals(object obj) => obj is IdOf<T> other && this == other;
		public bool Equals(IdOf<T> other) => other.id == this.id;

		public static bool operator==(IdOf<T> x, IdOf<T> y) => x.id == y.id;
		public static bool operator!=(IdOf<T> x, IdOf<T> y) => x.id != y.id;
		public static explicit operator IdOf<T>(int id) => new IdOf<T>(id);
		public static explicit operator int(IdOf<T> id) => id.id;
	}
}
