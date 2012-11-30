using System.Collections;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Threading.Tasks;

namespace Lex.Db
{
  public static class AsyncExtensions
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
  }
}
