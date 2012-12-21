using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Lex.Db
{
  class SyncInfoDeletable<TEntity> : SyncInfo
    where TEntity : class, IDeletable
  {
    readonly Func<object, DateTime?, IEnumerable<Task<IEnumerable<TEntity>>>> _queryCtor;

    public SyncInfoDeletable(DbSync sync, Func<object, DateTime?, IEnumerable<Task<IEnumerable<TEntity>>>> queryCtor)
      : base(sync)
    {
      _queryCtor = queryCtor;
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
          var updateResult = new UpdateResultDeletable<TEntity>(query);
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

  class UpdateResultDeletable<TEntity> : UpdateResultBase<TEntity>, IUpdateResult<TEntity> where TEntity : class, IDeletable
  {
    readonly List<TEntity> _deletes = new List<TEntity>();

    public UpdateResultDeletable(IEnumerable<Task<IEnumerable<TEntity>>> query) : base(query) { }

    public async Task<int> GetChanges()
    {
      foreach (var task in _query)
        AddRange(task.Result);

      return _updates.Count + _deletes.Count;
    }

    void AddRange(IEnumerable<TEntity> response)
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
