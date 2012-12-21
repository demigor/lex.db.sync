using System;
using System.Collections.Generic;
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

    internal abstract object CreateContext();
    internal abstract void DisposeContext(object ctx);
  }

  /// <summary>
  /// Database Synchronization Manager
  /// </summary>
  /// <typeparam name="T">Type of the shared context for queries</typeparam>
  public class DbSync<T> : DbSync
  {
    readonly Func<T> _ctor;
    readonly bool _disposeContext;

    public DbSync(DbInstance db)
      : this(db, Ctor<T>.New, true)
    {
    }

    public DbSync(DbInstance db, Func<T> ctor, bool disposeContext = true)
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

      DoInitialize();

      _sealed = true;
    }

    protected virtual void DoInitialize()
    {
      /*       var types = from p in typeof(T).GetPublicInstanceProperties()
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
       */
    }

    public void Register<TEntity>(Func<T, DateTime?, IEnumerable<Task<IEnumerable<TEntity>>>> queryCtor, Func<T, DateTime, IEnumerable<Task<IEnumerable<DeletedObject>>>> deletedQueryCtor, params string[] tags) where TEntity : class, ITimestamp
    {
      CheckNotSealed();

      var info = new SyncInfo<TEntity>(this, (i, ts) => queryCtor((T)i, ts), (i, ts) => deletedQueryCtor((T)i, ts));

      AddSync<TEntity>(info, tags);
    }

    public void RegisterDeletable<TEntity>(Func<T, DateTime?, IEnumerable<Task<IEnumerable<TEntity>>>> queryCtor, params string[] tags) where TEntity : class, IDeletable
    {
      CheckNotSealed();

      var info = new SyncInfoDeletable<TEntity>(this, (i, ts) => queryCtor((T)i, ts));

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
        try
        {
#if TPL4
          var results = await TaskEx.WhenAll(from s in syncs select s.Sync(ctx));
#else
        var results = await Task.WhenAll(from s in syncs select s.Sync(ctx));
#endif
          return results.Sum();
        }
        finally
        {
          DisposeContext(ctx);
        }
      }
      finally
      {
        Unlock<T>();
      }
    }

    internal override object CreateContext()
    {
      return _ctor();
    }

    internal override void DisposeContext(object ctx)
    {
      if (_disposeContext)
      {
        var d = ctx as IDisposable;
        if (d != null)
          d.Dispose();
      }
    }
  }
}
