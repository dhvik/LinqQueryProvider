using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using NUnit.Framework;

namespace LinqQueryProvider {

	[TestFixture]
	public class Class1 {
		[Test]
		public void LambdaTest() {
			var list = new List<int> { 1, 2, 3, 4, 5, 6 };
			Func<int, bool> f = x => x < 3;
			var enumerable = list.Where(f);
			var ints = enumerable.ToList();
			Assert.AreEqual(2, ints.Count, "Mismatch in ints.Count");
		}
		[Test]
		public void LinqToEf() {
			var entities = new Database1Entities();
			var queryable = entities.Person.Where(x => x.Id < 3);
			Console.WriteLine(queryable.ToString());
		}
		[Test]
		public void TestMyLinqProvider() {
			var client = new MyClient();
			var persons = client.Query<Person>().ToList();
			Assert.AreEqual(1, persons.Count);

			persons = client.Query<Person>().Where(x => x.Name == "Dan").ToList();
			Assert.AreEqual(1, persons.Count);
			Assert.AreEqual("Dan", persons[0].Name);
			persons = client.Query<Person>().Where(x => x.Name == "Dan").Where(x => x.Age > 10).ToList();
			Assert.AreEqual(1, persons.Count);
			//Assert.AreEqual("Dan", persons[0].Name);
		}
		public class MyClient {
			public IQueryable<T> Query<T>() {
				return new MyQueryable<T>();
			}
		}
		public class Person {
			public string Name { get; set; }
			public int Age { get; set; }
		}
		public class MyQueryable<T> : IQueryable<T> {
			public Expression Expression { get; private set; }
			public Type ElementType { get { return typeof(T); } }
			public IQueryProvider Provider { get; private set; }
			public MyQueryable() {
				Provider = new MyQueryProvider<T>();
				Expression = Expression.Constant(this);
			}
			public MyQueryable(MyQueryProvider<T> provider, Expression expression) {
				Provider = provider;
				Expression = expression;
			}
			public IEnumerator<T> GetEnumerator() {
				return (Provider.Execute<IEnumerable<T>>(Expression)).GetEnumerator();
			}
			IEnumerator IEnumerable.GetEnumerator() {
				return (Provider.Execute<IEnumerable>(Expression)).GetEnumerator();
			}
		}
		public class MyQueryProvider<T> : IQueryProvider {
			public IQueryable CreateQuery(Expression expression) {
				return (IQueryable)Activator.CreateInstance(typeof(MyQueryable<T>), new object[] { this, expression });
			}
			public IQueryable<TElement> CreateQuery<TElement>(Expression expression) {
				if (!typeof(TElement).IsAssignableFrom(typeof(T)))
					throw new NotSupportedException(string.Format("Cannot create a query for element {0}. This class is not the same or a base as the provider supported type {1}", typeof(TElement).FullName, typeof(T).FullName));
				return (IQueryable<TElement>)new MyQueryable<T>(this, expression);
			}
			public object Execute(Expression expression) {
				return ExecuteInner(expression);
			}
			public TResult Execute<TResult>(Expression expression) {
				return (TResult)ExecuteInner(expression);
			}
			private object ExecuteInner(Expression expression) {
				var parser = new MyPersonParser();
				var newExpression = parser.Visit(expression);
				var data = parser.GetServerData();
				var replacer = new MyConstantReplacer(typeof(T), data);
				var replacedTree = replacer.Visit(newExpression);
				var isQueryable = typeof(IQueryable).IsAssignableFrom(replacedTree.Type);
				if (isQueryable) {
					return data.Provider.CreateQuery(replacedTree);
				}
				return data.Provider.Execute(replacedTree);
			}
		}
		public class MyPersonParser : ExpressionVisitor {
			private string _byName;

			protected override Expression VisitMethodCall(MethodCallExpression node) {

				if (node.Method.Name == "Where") {
					var source = Visit(node.Arguments[0]);
					var xp = (UnaryExpression)node.Arguments[1];
					var lambda = (LambdaExpression)xp.Operand;
					var op = Visit(lambda);
					if (op == null)
						return source;
					return node;
				}
				return base.VisitMethodCall(node);
			}
			protected override Expression VisitBinary(BinaryExpression node) {
				if (node.NodeType == ExpressionType.Equal) {
					var l = Visit(node.Left) as MemberExpression;
					if (l != null && l.Member.Name == "Name") {
						var r = Visit(node.Right) as ConstantExpression;
						if (r != null) {
							_byName = r.Value as string;
							return null;
						}

					}
				}
				return base.VisitBinary(node);
			}
			protected override Expression VisitLambda<T>(Expression<T> lambda) {
				var body = Visit(lambda.Body);
				if (body == null)
					return null;
				if (body != lambda.Body) {
					return Expression.Lambda(lambda.Type, body, lambda.Parameters);
				}
				return lambda;
			}
			public IQueryable GetServerData() {
				return new List<Person> { new Person { Name = _byName,Age=123} }.AsQueryable();
			}
		}
		public class MyConstantReplacer : ExpressionVisitor {
			private readonly IQueryable _items;
			readonly Type _type;
			public MyConstantReplacer(Type type, IQueryable items) {
				_type = type;
				_items = items;
			}
			protected override Expression VisitConstant(ConstantExpression node) {
				if (IsMatchingType(node.Type)) {
					return Expression.Constant(_items);
				}
				return base.VisitConstant(node);
			}
			private bool IsMatchingType(Type type) {
				if (type == null)
					return false;
				if (type == _type)
					return true;
				if (!type.IsGenericType)
					return false;
				if (type.GetGenericTypeDefinition() == typeof(MyQueryable<>)) {
					var genericArgument = type.GetGenericArguments()[0];
					return genericArgument == _type;
				}
				return false;
			}
		}
	}
}
