using System;

namespace DataBoss.Data
{
	static class Lambdas
	{
		public static Action<T> CreateDelegate<T>(string methodName) =>
			Create<Action<T>>(typeof(T), methodName);

		public static Func<T, TResult> CreateDelegate<T, TResult>(string methodName) => 
			Create<Func<T,TResult>>(typeof(T), methodName);

		public static Func<T, TArg, TResult> CreateDelegate<T, TArg, TResult>(string methodName) =>
			Create<Func<T, TArg, TResult>>(typeof(T), methodName, typeof(TArg));

		static T Create<T>(Type type, string methodName, params Type[] types) =>
			(T)(object)System.Delegate.CreateDelegate(typeof(T), type.GetMethod(methodName, types));

		public static T Id<T>(T id) => id;
		public static T Default<T>() => default(T);
	}
}
