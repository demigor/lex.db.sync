using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Linq;
using System.Threading.Tasks;

namespace Lex.Db
{
  abstract class SyncInfo
  {
    protected readonly DbSync _sync;

    protected SyncInfo(DbSync sync)
    {
      _sync = sync;
    }

    public abstract Task<int> Sync(DataServiceContext ctx = null);
  }

  class SyncInfo<TEntity> : SyncInfo
    where TEntity : class, ITimestamp
  {
    readonly Func<DataServiceContext, DataServiceQuery<TEntity>> _queryCtor;
    readonly Func<DataServiceContext, DateTime, DataServiceQuery<DeletedObject>> _deletedQueryCtor;

    public SyncInfo(DbSync sync, Func<DataServiceContext, DataServiceQuery<TEntity>> queryCtor,
      Func<DataServiceContext, DateTime, DataServiceQuery<DeletedObject>> deletedQueryCtor)
      : base(sync)
    {
      _queryCtor = queryCtor;
      _deletedQueryCtor = deletedQueryCtor;
    }

    public async override Task<int> Sync(DataServiceContext ctx = null)
    {
      if (!_sync.TryLock<TEntity>())
        return 0;

      try
      {
        if (ctx == null)
          ctx = _sync.CreateContext();

        var table = _sync._db.Table<TEntity>();
        var lastTs = table.GetLastTs();

        var deletedQuery = default(DataServiceQuery<DeletedObject>);
        var query = _queryCtor(ctx);
        if (lastTs != null)
        {
          var ts = lastTs.Value;
          query = (DataServiceQuery<TEntity>)query.Where(i => i.Ts > ts);
          deletedQuery = _deletedQueryCtor(ctx, ts);
        }
        var updateResult = new UpdateResult<TEntity>(ctx, query, deletedQuery);
        var result = await updateResult.GetChanges();

        if (result > 0)
          _sync._db.BulkWrite(() =>
          {
            lastTs = lastTs.Max(updateResult.ApplyChanges(table));
            table[DbSync.TsMetadata] = lastTs.TsToString();
          });

        return result;
      }
      finally
      {
        _sync.Unlock<TEntity>();
      }
    }
  }

  interface IUpdateResult<TEntity> where TEntity : class, ITimestamp
  {
    Task<int> GetChanges();
    DateTime? ApplyChanges(DbTable<TEntity> table);
  }

  abstract class UpdateResultBase<TEntity> where TEntity : class, ITimestamp
  {
    protected readonly DataServiceQuery<TEntity> _query;
    protected readonly List<TEntity> _updates = new List<TEntity>();
    protected readonly DataServiceContext _ctx;

    protected UpdateResultBase(DataServiceContext ctx, DataServiceQuery<TEntity> query)
    {
      _ctx = ctx;
      _query = query;
    }
  }

  class UpdateResult<TEntity> : UpdateResultBase<TEntity>, IUpdateResult<TEntity> where TEntity : class, ITimestamp
  {
    readonly List<DeletedObject> _deletes = new List<DeletedObject>();
    DataServiceQuery<DeletedObject> _deletedQuery;

    public UpdateResult(DataServiceContext ctx, DataServiceQuery<TEntity> query, DataServiceQuery<DeletedObject> deletedQuery)
      : base(ctx, query)
    {
      _deletedQuery = deletedQuery;
    }

    public async Task<int> GetChanges()
    {
      if (_deletedQuery != null)
        await LoadDeletes();

      await LoadUpdates();

      return _updates.Count + _deletes.Count;
    }

    async Task<int> LoadUpdates()
    {
      var response = (QueryOperationResponse<TEntity>)await _query.ExecuteAsync();

      _updates.AddRange(response);

      for (var continuation = response.GetContinuation(); continuation != null; continuation = response.GetContinuation())
      {
        response = (QueryOperationResponse<TEntity>)await _ctx.ExecuteAsync(continuation);
        _updates.AddRange(response);
      }
      return _updates.Count;
    }

    async Task<int> LoadDeletes()
    {
      var response = (QueryOperationResponse<DeletedObject>)await _deletedQuery.ExecuteAsync();

      _deletes.AddRange(response);

      for (var continuation = response.GetContinuation(); continuation != null; continuation = response.GetContinuation())
      {
        response = (QueryOperationResponse<DeletedObject>)await _ctx.ExecuteAsync(continuation);
        _deletes.AddRange(response);
      }
      return _deletes.Count;
    }

    public DateTime? ApplyChanges(DbTable<TEntity> table)
    {
      var result = default(DateTime?);

      if (_deletes.Count > 0)
      {
        table.DeleteByKeys(_deletes.Select(i => i.Key));
        result = _deletes.Max(i => i.Ts);
      }

      if (_updates.Count > 0)
      {
        table.Save(_updates);
        result = result.Max(_updates.Max(i => i.Ts));
      }

      return result;
    }
  }
}
