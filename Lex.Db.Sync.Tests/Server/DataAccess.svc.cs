using System;
using System.Data.Services;
using System.Data.Services.Common;

namespace Lex.Db.Server
{
  public class DataAccess : DataService<SampleDbContext>
  {
    public static void InitializeService(DataServiceConfiguration config)
    {
      config.DataServiceBehavior.MaxProtocolVersion = DataServiceProtocolVersion.V3;
      config.SetEntitySetAccessRule("*", EntitySetRights.All);
      config.SetEntitySetPageSize("*", 1000);
      config.SetEntitySetAccessRule("DeletedItems", EntitySetRights.AllRead);
    }

    // We use change interceptor here because of SQL Server CE
    // If we use SQL Server or any DB that supports triggers, 
    // we can update timestamps & register delete operations via the trigger
    
    [ChangeInterceptor("Companies")]
    public void UpdateCompany(Company company, UpdateOperations op)
    {
      if (op.HasFlag(UpdateOperations.Delete))
        CurrentDataSource.AddToDeletedItems(new DeletedItem { Id = company.Id, EntitySet = "Companies", Ts = DateTime.Now });
      else
        company.Ts = DateTime.Now;
    }

    [ChangeInterceptor("People")]
    public void UpdatePerson(Person person, UpdateOperations op)
    {
      if (op.HasFlag(UpdateOperations.Delete))
        CurrentDataSource.AddToDeletedItems(new DeletedItem { Id = person.Id, EntitySet = "People", Ts = DateTime.Now });
      else
        person.Ts = DateTime.Now;
    }
  }
}
