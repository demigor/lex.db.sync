using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Linq;
using System.Threading.Tasks;

namespace Lex.Db
{
  public static class WcfDsExtensions
  {
    public static Task<IEnumerable<T>> ExecuteAsync<T>(this DataServiceQuery<T> query)
    {
      return Task.Factory.FromAsync<IEnumerable<T>>(query.BeginExecute, query.EndExecute, null);
    }

    public static Task<DataServiceResponse> ExecuteBatchAsync(this DataServiceContext ctx, params DataServiceRequest[] requests)
    {
      return Task.Factory.FromAsync<DataServiceRequest[], DataServiceResponse>((a, b, c) => ctx.BeginExecuteBatch(b, c, a), ctx.EndExecuteBatch, requests, null);
    }

    public static Task<IEnumerable<T>> ExecuteAsync<T>(this DataServiceContext ctx, DataServiceQueryContinuation<T> continuation)
    {
      return Task.Factory.FromAsync<DataServiceQueryContinuation<T>, IEnumerable<T>>(ctx.BeginExecute, ctx.EndExecute<T>, continuation, null);
    }

    public static void RegisterOData<TCtx, T>(this DbSync<TCtx> sync,
      Func<TCtx, DataServiceQuery<T>> queryCtor, Func<DataServiceQuery<T>, DateTime, IQueryable<T>> queryUpdate,
      Func<TCtx, DateTime, IQueryable<DeletedObject>> deletedQueryCtor, params string[] tags)
      where T : class, ITimestamp
      where TCtx : DataServiceContext
    {
      sync.Register<T>(CreateQuery(queryCtor, queryUpdate), CreateDeletedQuery(deletedQueryCtor), tags);
    }

    static Func<TCtx, DateTime, IEnumerable<Task<IEnumerable<DeletedObject>>>> CreateDeletedQuery<TCtx>(Func<TCtx, DateTime, IQueryable<DeletedObject>> deletedQuery)
      where TCtx : DataServiceContext
    {
      if (deletedQuery == null)
        return null;

      return (ctx, ts) => new Loader<TCtx, DeletedObject>(ctx, (DataServiceQuery<DeletedObject>)deletedQuery(ctx, ts));
    }

    static Func<TCtx, DateTime?, IEnumerable<Task<IEnumerable<T>>>> CreateQuery<TCtx, T>(Func<TCtx, DataServiceQuery<T>> queryAll, Func<DataServiceQuery<T>, DateTime, IQueryable<T>> queryUpdates)
      where TCtx : DataServiceContext
    {
      return (ctx, ts) =>
      {
        var query = queryAll(ctx);
        if (ts != null)
          query = (DataServiceQuery<T>)queryUpdates(query, ts.Value);

        return new Loader<TCtx, T>(ctx, query);
      };
    }

    class Loader<TCtx, T> : IEnumerable<Task<IEnumerable<T>>> where TCtx : DataServiceContext
    {
      readonly TCtx _ctx;
      readonly DataServiceQuery<T> _query;

      public Loader(TCtx ctx, DataServiceQuery<T> query)
      {
        _ctx = ctx;
        _query = query;
      }

      public IEnumerator<Task<IEnumerable<T>>> GetEnumerator()
      {
        var result = _query.ExecuteAsync();

        yield return result;

        var response = (QueryOperationResponse<T>)result.Result;

        for (var continuation = response.GetContinuation(); continuation != null; continuation = response.GetContinuation())
        {
          result = _ctx.ExecuteAsync(continuation);
          yield return result;
          response = (QueryOperationResponse<T>)result.Result;
        }
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
        return GetEnumerator();
      }
    }
  }
}
