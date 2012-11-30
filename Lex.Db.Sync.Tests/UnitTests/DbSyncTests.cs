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

namespace Lex.Db
{
  using WebEntities;

  [TestClass]
  public class DbSyncTests
#if SILVERLIGHT
    : WorkItemTest
#endif
  {
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
      _dbSync.Register(i => i.People, (i, ts) => i.DeletedItems.Where(j => j.EntitySet == "People" && j.Ts > ts).Select(j => new DeletedObject { Key = j.Id, Ts = j.Ts }));
      _dbSync.Register(i => i.Companies, (i, ts) => i.DeletedItems.Where(j => j.EntitySet == "Companies" && j.Ts > ts).Select(j => new DeletedObject { Key = j.Id, Ts = j.Ts }));
      _dbSync.Initialize();
    }

    [TestMethod]

#if NET40 
    public void SyncTest1()
    {
      SyncTest1Async().Wait();
    }
#else
#if SILVERLIGHT //&& !WINDOWS_PHONE
    [Asynchronous]
    public async void SyncTest1() 
    {
      await SyncTest1Async();
      TestComplete();
    }
#endif
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
    }

    [TestMethod]

#if NET40 
    public void SyncTest2()
    {
      SyncTest2Async().Wait();
    }
#else
#if SILVERLIGHT //&& !WINDOWS_PHONE
    [Asynchronous]
    public async void SyncTest2() 
    {
      await SyncTest2Async();
      TestComplete();
    }
#endif
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
    }
  }
}
