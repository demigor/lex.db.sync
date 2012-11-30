using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Linq;
using System.Threading.Tasks;

namespace Lex.Db
{
  class SyncInfoDeletable<TEntity> : SyncInfo
    where TEntity : class, IDeletable
  {
    readonly Func<DataServiceContext, DataServiceQuery<TEntity>> _queryCtor;

    public SyncInfoDeletable(DbSync sync, Func<DataServiceContext, DataServiceQuery<TEntity>> queryCtor)
      : base(sync)
    {
      _queryCtor = queryCtor;
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

        var query = _queryCtor(ctx);
        if (lastTs != null)
          query = (DataServiceQuery<TEntity>)query.Where(i => i.Ts > lastTs);

        var updateResult = new UpdateResultDeletable<TEntity>(ctx, query);
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

  class UpdateResultDeletable<TEntity> : UpdateResultBase<TEntity>, IUpdateResult<TEntity> where TEntity : class, IDeletable
  {
    readonly List<TEntity> _deletes = new List<TEntity>();

    public UpdateResultDeletable(DataServiceContext ctx, DataServiceQuery<TEntity> query)
      : base(ctx, query) { }

    public async Task<int> GetChanges()
    {
      var response = (QueryOperationResponse<TEntity>)await _query.ExecuteAsync();

      AddRange(response);

      for (var continuation = response.GetContinuation(); continuation != null; continuation = response.GetContinuation())
      {
        response = (QueryOperationResponse<TEntity>)await _ctx.ExecuteAsync(continuation);
        AddRange(response);
      }

      return _updates.Count + _deletes.Count;
    }

    void AddRange(QueryOperationResponse<TEntity> response)
    {
      foreach (var i in response)
        (i.IsDeleted ? _deletes : _updates).Add(i);
    }

    public DateTime? ApplyChanges(DbTable<TEntity> table)
    {
      var result = default(DateTime?);

      if (_deletes.Count > 0)
      {
        table.Delete(_deletes);
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
