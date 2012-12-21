using System;
using System.Linq;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Threading.Tasks;
#if NETFX_CORE
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
#else
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if SILVERLIGHT
#if WINDOWS_PHONE
using Microsoft.Phone.Testing;
#else
using Microsoft.Silverlight.Testing;
#endif
#endif
#endif

/// IMPORTANT!!! Please fix the URI to the AppServer in Entities.partial.cs
/// Don't use localhost, because of WP8 emulator. Use external name of the server.
/// For WinRT tests, Loopback on localhost for Lex.Db.Sync.Tests must be enabled.
/// CheckNetIsolation.exe LoopbackExempt –a –p=S-1-15-2-2433079423-632309225-905577170-3433315063-38136667-2702292642-2889839298

namespace Lex.Db
{
  using WebEntities;
  using System.Collections.Generic;

  [TestClass]
  public class DbSyncTests
#if SILVERLIGHT
 : WorkItemTest
  {
#else
  {
    static void TestComplete() { }
    class AsynchronousAttribute : Attribute { }
#endif

    static DbInstance _db;
    static DbSync<SampleDbContext> _dbSync;

    static DbSyncTests()
    {
      _db = new DbInstance("DbSync");
      _db.Map<Person>().
        Key(i => i.Id).
        Map(i => i.Ts).
        Map(i => i.Id).
        Map(i => i.FirstName).
        Map(i => i.LastName);//.Ref(i => i.CompanyId, i => i.Company);

      _db.Map<Company>().
        Key(i => i.Id).
        Map(i => i.Ts).
        Map(i => i.Name);

      _db.Initialize();
      _db.Purge();

      _dbSync = new DbSync<SampleDbContext>(_db);
      _dbSync.RegisterOData(i => i.People, (q, ts) => q.Where(j => j.Ts > ts), (ctx, ts) => from i in ctx.DeletedItems where i.EntitySet == "People" && i.Ts > ts select new DeletedObject(i.Id, i.Ts));
      _dbSync.RegisterOData(i => i.Companies, (q, ts) => q.Where(j => j.Ts > ts), (ctx, ts) => from i in ctx.DeletedItems where i.EntitySet == "Companies" && i.Ts > ts select new DeletedObject(i.Id, i.Ts));
      _dbSync.Initialize();
    }

    [TestMethod, Asynchronous]
#if NET40 
    public void SyncTest1()
    {
      SyncTest1Async().Wait();
    }
#endif
    public async Task SyncTest1Async()
    {
      var swatch = Stopwatch.StartNew();

      var count = await _dbSync.SyncAsync<Person>();

      swatch.Stop();

      var ts = _db.Table<Person>()[DbSync.TsMetadata];
      if (count > 0)
        Assert.IsNotNull(ts);

      Debug.WriteLine("Elapsed time " + swatch.ElapsedMilliseconds + " ms. Sync " + count);
      TestComplete();
    }

    [TestMethod, Asynchronous]
#if NET40 
    public void SyncTest2()
    {
      SyncTest2Async().Wait();
    }
#endif
    public async Task SyncTest2Async()
    {
      var swatch = Stopwatch.StartNew();

      var count = await _dbSync.SyncAsync<Person>();

      swatch.Stop();

      var ts = _db.Table<Person>()[DbSync.TsMetadata];
      if (count > 0)
        Assert.IsNotNull(ts);

      Debug.WriteLine("Elapsed time " + swatch.ElapsedMilliseconds + " ms. Sync " + count);

      TestComplete();
    }
  }
}
