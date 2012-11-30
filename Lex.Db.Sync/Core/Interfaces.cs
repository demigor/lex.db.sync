using System;

namespace Lex.Db
{
  public interface ITimestamp
  {
    DateTime Ts { get; }
  }

  public class DeletedObject
  {
    public DateTime Ts;
    public object Key;
  }

  public interface IDeletable : ITimestamp
  {
    bool IsDeleted { get; }
  }
}
