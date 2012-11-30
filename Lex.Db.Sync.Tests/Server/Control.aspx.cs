using System;

namespace Lex.Db.Server
{
  public partial class Control : System.Web.UI.Page
  {
    protected void Button1_Click(object sender, EventArgs e)
    {
      using (var ctx = new SampleDbContext())
      {
        for (int i = 0; i < 10000; i++)
          ctx.AddToPeople(new Person { FirstName = "Name " + i, LastName = "Family " + i, Ts = DateTime.Now });

        ctx.SaveChanges();
      }
    }

    protected void Button2_Click(object sender, EventArgs e)
    {
      using (var ctx = new SampleDbContext())
      {
        for (int i = 0; i < 100; i++)
          ctx.AddToPeople(new Person { FirstName = "Name " + i, LastName = "Family " + i, Ts = DateTime.Now });

        ctx.SaveChanges();
      }
    }
  }
}