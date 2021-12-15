# CRUD with Redis OM .NET - C# Advent

Redis is a NoSQL database that's loved for its simplicity and speed. Those two virtues make it the [most loved database](https://insights.stackoverflow.com/survey/2021#technology-most-loved-dreaded-and-wanted) by developers. But there's a problem inherent in Redis if you want to store more complicated objects. Redis is a key-value data structure store. This means that if you're going to perform CRUD(Create Read Update and Delete) operations on your data, and the only way you care to access the data is by your key, you'll have no problem. But what if we wanted to look up items by value? This is where things can get tricky in Redis, and that's also where the story of [Redis OM](https://github.com/redis/redis-om-dotnet) begins. Querying items by their values in Redis requires a fair amount of legwork on the developers' part to manually build and maintain secondary indexes for those objects. After that, you need to execute several commands to perform your query. With [Redis OM](https://github.com/redis/redis-om-dotnet) and [RedisJSON](https://oss.redis.com/redisjson/), you can build your model ahead of time and query them with a LINQ interface.

## Our Model

We're going to use this `Customer` object as our model throughout our examples:

```csharp
public class Customer
{
   public string FirstName { get; set; }
   public string LastName { get; set; }
   public string Email { get; set; }
   public int Age { get; set; }
}
```

We will assume that we want to search on any field in Customer. With that in mind, we'll go through what we would have to do in Redis OM and what we would have had to do in prior iterations of Redis for the basic operations of persistent storage - Create Read Update and Delete (CRUD).

## Setting up

### Start up Redis

First, we'll set up our environment. Then, for development purposes, we'll use Docker to run Redis:

```bash
docker run -p 6379:6379 redislabs/redismod
```

### Set up our project

Next, we'll set up our project. Then, finally, we'll just run this all as a console app:

```bash
dotnet new console -n RedisOmComparison
```

Cd into the `RedisOmComparison` directory, and run `dotnet add package Redis.OM` to install Redis OM.

### Initialize connection objects

Now we'll initialize our two connection objects. The ConnectionMultiplexer for our standard redis Setup and the RedisConnectionProvider for the Redis OM setup. In program.cs run the following:

```csharp
// Standard Redis
var muxer = await ConnectionMultiplexer.ConnectAsync("localhost");
var db = muxer.GetDatabase();

// Redis OM
var provider = new RedisConnectionProvider("redis://localhost:6379");
```

### Initialize Bob

So we are consistent, we'll use the same customer object across both examples, Bob:

```csharp
var bob = new Customer {Age = 35, Email = "foo@bar.com", FirstName = "Bob", LastName = "Smith"};
```

## Create indexed objects in Redis with Redis OM

To create an object in Redis with Redis OM, we'll start with our initial model but add some attributes to it. First, we'll decorate the class itself with the `Document` Attribute, and then we'll decorate each of the properties with the `Indexed` attribute. After this is done, our Customer object should look like this:

```csharp
[Document]
public class Customer
{
   [Indexed(Sortable = true)] public string FirstName { get; set; }
   [Indexed(Sortable = true)] public string LastName { get; set; }
   [Indexed(Sortable = true)] public string Email { get; set; }
   [Indexed(Sortable = true)] public int Age { get; set; }
}
```

This should be stored in a new file - `Customer.cs`

### Create the index

Now that you've declared the Customer with indexing in mind, you'll just create the index. This only has to be done once per index. In Program.cs add:

```csharp
provider.Connection.CreateIndex(typeof(Customer));
```

### Add a customer

All that's left to do is add a customer, create a `RedisCollection<Customer>`, and simply insert a new customer into Redis.

```csharp
var customers = provider.RedisCollection<Customer>();
var customerId = await customers.InsertAsync(bob);
```

## Create indexed objects in Redis without Redis OM

Creating indexed objects without Redis OM will be a bit more complicated. We're going to have to go through three stages, which will need to run each time we add something.

1. Create a key name
2. Map the object to a hash and create the hash
3. Update the indexes associated with each item

### Key name

We'll use `Customer:AGuid` as our key name to keep it simple.

```csharp
var keyName = $"Customer:{Guid.NewGuid()}";
```

### Create the object's hash

The natural data structure to use when storing indexable objects in Redis (without a module) is a Redis Hash. To store an object in a Redis hash, we'll need to break it down into a set of field-value pairs that we can send off to our Redis along with our Redis key. Once this is done, the object is actually stored in redis. You can do that by calling the `HashSetAsync` method on the `DB` object and passing in the properties/values of `Bob` as field value pairs:

```csharp
await db.HashSetAsync(keyName, new HashEntry[]
{
    new HashEntry(nameof(Customer.FirstName), bob.FirstName),
    new HashEntry(nameof(Customer.LastName), bob.LastName),
    new HashEntry(nameof(Customer.Email), bob.Email),
    new HashEntry(nameof(Customer.Age), bob.Age)
});
```

### Set up the index

Unlike Redis OM, the indexes are not set & forgotten in standard redis. Each indexed field needs to have its own separate index maintained. For this purpose, for each field, we'll keep different sorted sets. For each string field name, this will be a sorted set for each value, and forage will be a single sorted set. Mind you, later, when we update items in Redis, these will also need to be updated. To create the indexes, run the following code:

```csharp
await db.SortedSetAddAsync($"Customer:FirstName:{bob.FirstName}", keyName, 0);
await db.SortedSetAddAsync($"Customer:LastName:{bob.LastName}", keyName, 0);
await db.SortedSetAddAsync($"Customer:Email:{bob.Email}", keyName, 0);
await db.SortedSetAddAsync($"Customer:Age", keyName, bob.Age);
```

## Comparison

So after the preliminary steps to initialize the index for redis OM (decorating the class and calling `CreateIndex` once), creating a customer object is very straightforward:

```csharp
var customerId = await customers.InsertAsync(bob);
```

Versus doing so without Redis OM:

```csharp
var keyName = $"Customer:{Guid.NewGuid()}";

await db.HashSetAsync(keyName, new HashEntry[]
{
    new HashEntry(nameof(Customer.FirstName), bob.FirstName),
    new HashEntry(nameof(Customer.LastName), bob.LastName),
    new HashEntry(nameof(Customer.Email), bob.Email),
    new HashEntry(nameof(Customer.Age), bob.Age)
});

await db.SetAddAsync($"Customer:FirstName:{bob.FirstName}", keyName);
await db.SetAddAsync($"Customer:LastName:{bob.LastName}", keyName);
await db.SetAddAsync($"Customer:Email:{bob.Email}", keyName);
await db.SortedSetAddAsync($"Customer:Age", keyName, bob.Age);
```

Naturally, the former is much more straightforward, and it only requires a single round trip to Redis to accomplish!

## Reading data out of Redis with an Id

Now that we've inserted our data into Redis, how can we read it? Well, there are two dimensions to think about 1: How are you querying objects? 2: How are you marshaling your objects?

There are two types of ways to query your objects 1: by key 2: by values. In the former case, querying is straightforward in both cases:

### With Redis OM

To read an Item out of Redis with Redis OM, just use the generic `FindById` command:

```csharp
var alsoBob = await customers.FindByIdAsync(customerId);
```

### Without Redis OM

Without Redis OM, you'll need to call `HGETALL` and then build a new instance of the object from the hash, which takes a bit more effort:

```csharp
var bobHash = await db.HashGetAllAsync(keyName);
var manuallyBuiltBob = new Customer
{
    Age = (int)bobHash.FirstOrDefault(x=>x.Name == "Age").Value,
    FirstName = bobHash.FirstOrDefault(x=>x.Name == "FirstName").Value,
    LastName = bobHash.FirstOrDefault(x=>x.Name == "LastName").Value,
    Email = bobHash.FirstOrDefault(x=>x.Name == "Email").Value,
};
```

## Reading data out of Redis by Value

This is where things start to get interesting. Many database use cases require the ability to look items up by values. For Example, if we wanted to find all the customers named "Bob" in a traditional SQL database, we'd just run `SELECT * FROM Customers WHERE FirstName = 'Bob'`. However, by default, Redis lacks the concept of a table scan to look up records by a given value. That's why earlier, we constructed secondary indexes for both types. So now, let's look at querying items by their values.

### Query by FirstName Redis OM

To query by the `FirstName` property in Redis OM, all you need is a simple LINQ statement:

```csharp
var bobsRedisOm = customers.Where(x => x.FirstName == "Bob");
```

Then when that collection enumerates, all of the Bobs currently in Redis will be populated as Customers.

### Querying by FirstName without Redis OM

Querying by First Name without Redis OM is more complicated, as has been typical so far. This time, you need to read the Set containing all the Bobs, and then you need to query each of those Ids individually:

```csharp
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
```

### Query by Age Redis OM

To query by Age in Redis OM, we can use the typical operators we would use for numerics `>=,<=,>,<,==`

```csharp
var under65RedisOm = customers.Where(x=>x.Age < 65);
```

### Query by Age without Redis OM

Querying by Age without Redis OM is similar to how querying strings would work. Except for this time, you would just send a range query to the sorted Set:


```csharp
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
```

## Updating objects in Redis

The mechanics of updating objects in Redis is pretty straightforward. For a hash, you simply call `HSET` and pass in the key and the field/value pairs you'd like to update. However, when you're indexing items, you need to coordinate the indexes as well, at least when you're not using Redis OM:

### Updating an item in Redis with Redis OM

To update an item in Redis using Redis OM, simply change an item in an enumerated collection and call `Save` on the collection:

```csharp
foreach (var customer in customers)
{
    customer.Age += 1;
}
await customers.SaveAsync();
```

This will be the same regardless of what you update in the item.

### Updating an item in Redis without Redis OM

To update an item in Redis without Redis OM, you'll need to first make the call to update the item, and then for each field, you update you need to adjust its index. So let's go ahead and see how we would update the Email and age attributes without Redis OM.

#### Updating the data

Updating the data is fairly straightforward - call `HashSet` on the key and each of the fields within it you want to update.

```csharp
await db.HashSetAsync(keyName, new HashEntry[]{new ("Age", bob.Age + 1), new("Email", "bar@foo.com")});
```

#### Updating the indexes

With the data updated, we now have to go in and update the indexes as well. For our Email, this will involve deleting the record from the previous Email's Set and then adding it to the new Email's Set. For Age, this just means updating the members score in the sorted Set:

```csharp
await db.SortedSetRemoveAsync($"Customer:Email:{bob.Email}", keyName);
await db.SortedSetAddAsync($"Customer:Email:@bar@foo.com", keyName, 0);

await db.SortedSetAddAsync($"Customer:Age", keyName, bob.Age + 1);
```

## Deleting indexed items

### With Redis OM
When deleting an indexed item in Redis with Redis OM, it's as easy as calling `Unlink` on the item's key:

```csharp
provider.Connection.Unlink(customerId);
```

### Without Redis OM

Without Redis OM, you will, in addition to having to delete the key, have to go into all the sets for the accompanying indexed fields and remove the key from there too:

```csharp
await db.KeyDeleteAsync(keyName);
await db.SortedSetRemoveAsync($"Customer:Email:{bob.Email}", keyName);
await db.SortedSetRemoveAsync($"Customer:FirstName:{bob.FirstName}", keyName);
await db.SortedSetRemoveAsync($"Customer:LastName:{bob.LastName}", keyName);
await db.SortedSetRemoveAsync($"Customer:Age", keyName);
```

## Summing up

As we've seen throughout this article, Redis OM is a real boon when you are performing all of the CRUD operations with Redis, as it vastly decreases the complexity of each type of operation. Not only that, but because you can complete everything in Redis OM with a single command, you remove any concerns of conflicting updates and any cross-shard complexity associated with updating stuff in Redis.