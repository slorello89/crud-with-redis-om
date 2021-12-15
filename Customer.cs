using Redis.OM.Modeling;

namespace RedisOmComparison;

[Document]
public class Customer
{
    [Indexed(Sortable = true)] public string FirstName { get; set; }
    [Indexed(Sortable = true)] public string LastName { get; set; }
    [Indexed(Sortable = true)] public string Email { get; set; }
    [Indexed(Sortable = true)] public int Age { get; set; }
}