
using Redis.OM;
using RedisOmComparison;
using StackExchange.Redis;

// Standard Redis
var muxer = await ConnectionMultiplexer.ConnectAsync("localhost");
var db = muxer.GetDatabase();

// Redis OM
var provider = new RedisConnectionProvider("redis://localhost:6379"); 

// C - Create
var bob = new Customer {Age = 35, Email = "foo@bar.com", FirstName = "Bob", LastName = "Smith"};
// With Redis OM
provider.Connection.CreateIndex(typeof(Customer));

var customers = provider.RedisCollection<Customer>();
var customerId = await customers.InsertAsync(bob);

// Without Redis OM

var keyName = $"Customer:{Guid.NewGuid()}";

await db.HashSetAsync(keyName, new HashEntry[]
{
    new HashEntry(nameof(Customer.FirstName), bob.FirstName),
    new HashEntry(nameof(Customer.LastName), bob.LastName),
    new HashEntry(nameof(Customer.Email), bob.Email),
    new HashEntry(nameof(Customer.Age), bob.Age)
});

await db.SortedSetAddAsync($"Customer:FirstName:{bob.FirstName}", keyName, 0);
await db.SortedSetAddAsync($"Customer:LastName:{bob.LastName}", keyName, 0);
await db.SortedSetAddAsync($"Customer:Email:{bob.Email}", keyName, 0);
await db.SortedSetAddAsync($"Customer:Age", keyName, bob.Age);

//Read with ID

// with Redis OM
var alsoBob = await customers.FindByIdAsync(customerId);

// without Redis OM

var bobHash = await db.HashGetAllAsync(keyName);
var manuallyBuiltBob = new Customer
{
    Age = (int)bobHash.FirstOrDefault(x=>x.Name == "Age").Value,
    FirstName = bobHash.FirstOrDefault(x=>x.Name == "FirstName").Value,
    LastName = bobHash.FirstOrDefault(x=>x.Name == "LastName").Value,
    Email = bobHash.FirstOrDefault(x=>x.Name == "Email").Value,
};

//Read with First Name

//Redis OM
var bobsRedisOm = customers.Where(x => x.FirstName == "Bob");

//Without Redis OM

var bobIds = await db.SortedSetRangeByRankAsync($"Customer:FirstName:Bob");
var bobsWithoutOm = new List<Customer>();

foreach (var id in bobIds)
{
    var hash = await db.HashGetAllAsync(id.ToString());
    bobsWithoutOm.Add(new Customer
    {
        Age = (int)hash.FirstOrDefault(x=>x.Name == "Age").Value,
        FirstName = hash.FirstOrDefault(x=>x.Name == "FirstName").Value,
        LastName = hash.FirstOrDefault(x=>x.Name == "LastName").Value,
        Email = hash.FirstOrDefault(x=>x.Name == "Email").Value,
    });
}

//Read with Age

//Redis OM
var under65RedisOm = customers.Where(x=>x.Age<65);

//Without Redis OM
var under65IdsWithoutRedisOm = db.SortedSetRangeByScore($"Customer:Age", 0, 65);
var under65WithoutRedisOm = new List<Customer>();

foreach (var id in under65IdsWithoutRedisOm)
{
    var hash = await db.HashGetAllAsync(id.ToString());
    under65WithoutRedisOm.Add(new Customer
    {
        Age = (int)hash.FirstOrDefault(x=>x.Name == "Age").Value,
        FirstName = hash.FirstOrDefault(x=>x.Name == "FirstName").Value,
        LastName = hash.FirstOrDefault(x=>x.Name == "LastName").Value,
        Email = hash.FirstOrDefault(x=>x.Name == "Email").Value,
    });
}

//Update

//Redis OM

foreach (var customer in customers)
{
    customer.Age += 1;
}
await customers.SaveAsync();

// without Redis OM

await db.HashSetAsync(keyName, new HashEntry[]{new ("Age", bob.Age + 1), new("Email", "bar@foo.com")});

await db.SortedSetRemoveAsync($"Customer:Email:{bob.Email}", keyName);
await db.SortedSetAddAsync($"Customer:Email:@bar@foo.com", keyName, 0);

await db.SortedSetAddAsync($"Customer:Age", keyName, bob.Age + 1);

//Deleting
// With Redis OM
provider.Connection.Unlink(customerId);

// Without Redis OM

await db.KeyDeleteAsync(keyName);
await db.SortedSetRemoveAsync($"Customer:Email:{bob.Email}", keyName);
await db.SortedSetRemoveAsync($"Customer:FirstName:{bob.FirstName}", keyName);
await db.SortedSetRemoveAsync($"Customer:LastName:{bob.LastName}", keyName);
await db.SortedSetRemoveAsync($"Customer:Age", keyName);