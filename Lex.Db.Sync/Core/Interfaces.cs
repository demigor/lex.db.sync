using System;

namespace Lex.Db
{
  public interface ITimestamp
  {
    DateTime Ts { get; }
  }

  public class DeletedObject
  {
    public DeletedObject(object key, DateTime ts)
    {
      Ts = ts;
      Key = key;
    }

    public object Key;
    public DateTime Ts;
  }

  public interface IDeletable : ITimestamp
  {
    bool IsDeleted { get; }
  }
}
