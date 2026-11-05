using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;

namespace TaskFlow.Application.UnitTests;

/// <summary>An in-memory <see cref="DbSet{TEntity}"/> that supports the async LINQ operators handlers use.</summary>
internal static class FakeDbSet
{
    public static DbSet<T> Of<T>(params T[] items)
        where T : class
    {
        IQueryable<T> query = new AsyncEnumerable<T>(items);
        var set = Substitute.For<DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>>();
        var queryable = (IQueryable<T>)set;
        queryable.Provider.Returns(query.Provider);
        queryable.Expression.Returns(query.Expression);
        queryable.ElementType.Returns(query.ElementType);
        queryable.GetEnumerator().Returns(_ => query.GetEnumerator());
        ((IAsyncEnumerable<T>)set).GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(call => ((IAsyncEnumerable<T>)query).GetAsyncEnumerator(call.Arg<CancellationToken>()));
        return set;
    }

    private sealed class AsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public AsyncEnumerable(IEnumerable<T> items)
            : base(items)
        {
        }

        public AsyncEnumerable(Expression expression)
            : base(expression)
        {
        }

        IQueryProvider IQueryable.Provider => new AsyncProvider((IQueryProvider)this);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncEnumerator(((IEnumerable<T>)this).GetEnumerator());

        private sealed class AsyncEnumerator(IEnumerator<T> inner) : IAsyncEnumerator<T>
        {
            public T Current => inner.Current;

            public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());

            public ValueTask DisposeAsync()
            {
                inner.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class AsyncProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => inner.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new AsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression) => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var result = typeof(IQueryProvider).GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!
                .MakeGenericMethod(resultType).Invoke(inner, [expression]);
            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType).Invoke(null, [result])!;
        }
    }
}
