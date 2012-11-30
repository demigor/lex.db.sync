using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Lex.Db
{
  public abstract class DbSync
  {
    public static readonly string TsMetadata = "Sync.Ts";

    internal readonly DbInstance _db;

    protected DbSync(DbInstance db)
    {
      if (db == null)
        throw new ArgumentNullException("db");

      _db = db;
    }

    internal static string GetEntitySetName<E>()
    {
      var type = typeof(E);
      var x = type.GetCustomAttribute<EntitySetAttribute>();
      if (x == null)
        throw new ArgumentException("No EntitySet attribute defined on type " + type.Name);

      return x.EntitySet;
    }

    readonly HashSet<Type> _locks = new HashSet<Type>();

    internal bool TryLock<TEntity>()
    {
      lock (_locks)
      {
        if (_locks.Contains(typeof(TEntity)))
          return false;

        _locks.Add(typeof(TEntity));
      }
      return true;
    }

    internal void Unlock<TEntity>()
    {
      lock (_locks)
        _locks.Remove(typeof(TEntity));
    }

    internal abstract DataServiceContext CreateContext();
  }

  public class DbSync<T> : DbSync
    where T : DataServiceContext
  {
    readonly Func<T> _ctor;

    public DbSync(DbInstance db)
      : this(db, Ctor<T>.New)
    {
    }

    public DbSync(DbInstance db, Func<T> ctor)
      : base(db)
    {
      if (ctor == null)
        throw new ArgumentNullException("ctor");

      _ctor = ctor;
    }

    bool _sealed;
    readonly Dictionary<string, List<SyncInfo>> _tags = new Dictionary<string, List<SyncInfo>>();
    readonly Dictionary<Type, SyncInfo> _syncs = new Dictionary<Type, SyncInfo>();

    public void Initialize()
    {
      CheckNotSealed();

      var types = from p in typeof(T).GetPublicInstanceProperties()
                  let type = p.PropertyType
#if NETFX_CORE
                  where type.GetGenericTypeDefinition() == typeof(DataServiceQuery<>)
#else
                  where type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DataServiceQuery<>)
#endif
                  let result = type.GetGenericArguments()[0]
                  where typeof(ITimestamp).IsAssignableFrom(result)
                  select result;

      foreach (var type in types)
        if (!_syncs.ContainsKey(type))
          _syncs[type] = CreateSync(type);

      _sealed = true;
    }

    #region Generic SyncInfo ctor logic

    static MethodInfo _createSyncCore = typeof(DbSync<T>).GetPrivateInstanceMethod("CreateSyncCore");
    static MethodInfo _createSyncDeletableCore = typeof(DbSync<T>).GetPrivateInstanceMethod("CreateSyncDeletableCore");

    SyncInfo CreateSyncCore<TEntity>() where TEntity : class, ITimestamp
    {
      return new SyncInfo<TEntity>(this, CreateBaseQuery<TEntity>, CreateDeletedQuery);
    }

    DataServiceQuery<DeletedObject> CreateDeletedQuery(DataServiceContext ctx, DateTime lastTs)
    {
      throw new NotImplementedException();
    }

    SyncInfo CreateSyncDeletableCore<TEntity>() where TEntity : class, IDeletable
    {
      return new SyncInfoDeletable<TEntity>(this, CreateBaseQuery<TEntity>);
    }

    SyncInfo CreateSync(Type type)
    {
      if (typeof(IDeletable).IsAssignableFrom(type))
        return (SyncInfo)_createSyncDeletableCore.MakeGenericMethod(type).Invoke(this, null);

      return (SyncInfo)_createSyncCore.MakeGenericMethod(type).Invoke(this, null);
    }

    #endregion

    public void Register<TEntity>(Func<T, DataServiceQuery<TEntity>> queryCtor, Func<T, DateTime, IQueryable<DeletedObject>> deletedQueryCtor, params string[] tags) where TEntity : class, ITimestamp
    {
      CheckNotSealed();

      var info = new SyncInfo<TEntity>(this, i => queryCtor((T)i), (i, ts) => (DataServiceQuery<DeletedObject>)deletedQueryCtor((T)i, ts));

      AddSync<TEntity>(info, tags);
    }

    public void RegisterDeletable<TEntity>(Func<T, DataServiceQuery<TEntity>> queryCtor, params string[] tags) where TEntity : class, IDeletable
    {
      CheckNotSealed();

      var info = new SyncInfoDeletable<TEntity>(this, i => queryCtor((T)i));

      AddSync<TEntity>(info, tags);
    }

    void AddSync<TEntity>(SyncInfo info, params string[] tags) where TEntity : class, ITimestamp
    {
      if (tags == null || tags.Length == 0 || tags.Contains(null))
        _syncs[typeof(TEntity)] = info;

      foreach (var tag in tags)
        if (tag != null)
        {
          List<SyncInfo> set;

          if (!_tags.TryGetValue(tag, out set))
            _tags[tag] = new List<SyncInfo> { info };
          else
            set.Add(info);
        }
    }

    void CheckNotSealed()
    {
      if (_sealed)
        throw new InvalidOperationException("DbSync is already initialized");
    }

    void CheckSealed()
    {
      if (!_sealed)
        throw new InvalidOperationException("DbSync is not initialized");
    }

    static DataServiceQuery<TEntity> CreateBaseQuery<TEntity>(DataServiceContext ctx) where TEntity : class, ITimestamp
    {
      return ctx.CreateQuery<TEntity>(GetEntitySetName<TEntity>());
    }

    public Task<int> SyncAsync<TEntity>() where TEntity : class, ITimestamp
    {
      CheckSealed();

      var sync = _syncs[typeof(TEntity)];

#if TPL4
      return TaskEx.Run(() => sync.Sync());
#else
      return Task.Run(() => sync.Sync());
#endif
    }

    public Task<int> SyncAsync(string tag = null)
    {
      CheckSealed();
#if TPL4
      return TaskEx.Run(() => SyncByTagCore(tag));
#else
      return Task.Run(() => SyncByTagCore(tag));
#endif
    }

    async Task<int> SyncByTagCore(string tag)
    {
      if (!TryLock<T>())
        return -1;

      try
      {
        IEnumerable<SyncInfo> syncs;

        if (tag == null)
          syncs = _syncs.Values;
        else
        {
          List<SyncInfo> list;
          if (!_tags.TryGetValue(tag, out list))
            return -1;

          syncs = list;
        }

        var ctx = _ctor();
#if TPL4
        var results = await TaskEx.WhenAll(from s in syncs select s.Sync(ctx));
#else
        var results = await Task.WhenAll(from s in syncs select s.Sync(ctx));
#endif
        return results.Sum();
      }
      finally
      {
        Unlock<T>();
      }
    }

    internal override DataServiceContext CreateContext()
    {
      return _ctor();
    }
  }
}
