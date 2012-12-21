using System;
using System.Data.Services.Client;

namespace Lex.Db.WebEntities
{
  public partial class Person : ITimestamp
  {
  }

  public partial class Company : ITimestamp
  {
  }

  public partial class SampleDbContext
  {
    public SampleDbContext()
      : base(new Uri("http://lexx/lex.db.sync/DataAccess.svc"))
    {
      MergeOption = MergeOption.NoTracking;
#if SILVERLIGHT && !WINDOWS_PHONE
      HttpStack = HttpStack.ClientHttp;
#endif
    }
  }
}
