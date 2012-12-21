using System;
using System.Collections.Generic;
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

    public abstract Task<int> Sync(object ctx = null);
  }

  class SyncInfo<TEntity> : SyncInfo
    where TEntity : class, ITimestamp
  {
    readonly Func<object, DateTime?, IEnumerable<Task<IEnumerable<TEntity>>>> _queryCtor;
    readonly Func<object, DateTime, IEnumerable<Task<IEnumerable<DeletedObject>>>> _deletedQueryCtor;

    public SyncInfo(DbSync sync, Func<object, DateTime?, IEnumerable<Task<IEnumerable<TEntity>>>> queryCtor,
      Func<object, DateTime, IEnumerable<Task<IEnumerable<DeletedObject>>>> deletedQueryCtor)
      : base(sync)
    {
      _queryCtor = queryCtor;
      _deletedQueryCtor = deletedQueryCtor;
    }

    public async override Task<int> Sync(object ctx = null)
    {
      if (!_sync.TryLock<TEntity>())
        return 0;

      try
      {
        var dispose = false;
        if (ctx == null)
        {
          dispose = true;
          ctx = _sync.CreateContext();
        }
        try
        {
          var table = _sync._db.Table<TEntity>();
          var lastTs = table.GetLastTs();

          var query = _queryCtor(ctx, lastTs);
          var deletedQuery = lastTs != null ? _deletedQueryCtor(ctx, lastTs.Value) : null;
          var updateResult = new UpdateResult<TEntity>(query, deletedQuery);
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
          if (dispose)
            _sync.DisposeContext(ctx);
        }
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
    protected readonly IEnumerable<Task<IEnumerable<TEntity>>> _query;
    protected readonly List<TEntity> _updates = new List<TEntity>();

    protected UpdateResultBase(IEnumerable<Task<IEnumerable<TEntity>>> query)
    {
      _query = query;
    }
  }

  class UpdateResult<TEntity> : UpdateResultBase<TEntity>, IUpdateResult<TEntity> where TEntity : class, ITimestamp
  {
    readonly List<DeletedObject> _deletes = new List<DeletedObject>();
    IEnumerable<Task<IEnumerable<DeletedObject>>> _deletedQuery;

    public UpdateResult(IEnumerable<Task<IEnumerable<TEntity>>> query, IEnumerable<Task<IEnumerable<DeletedObject>>> deletedQuery)
      : base(query)
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
      foreach (var task in _query)
        _updates.AddRange(await task);

      return _updates.Count;
    }

    async Task<int> LoadDeletes()
    {
      if (_deletedQuery != null)
        foreach (var task in _deletedQuery)
          _deletes.AddRange(await task);

      return _deletes.Count;
    }

    public DateTime? ApplyChanges(DbTable<TEntity> table)
    {
      var result = default(DateTime?);

      if (_deletes.Count > 0)
      {
        table.DeleteByKeys(from d in _deletes select d.Key);
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
