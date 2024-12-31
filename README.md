# Aspire Initialisers

This is a proof of concept for doing initialisers in Aspire.
This is very rough and ready so is proof of concepty right now.

Initialisers run after all the health checks for a resource has passed, but before dependencies using `WaitFor` will start.
This makes it perfect for any kind of initialisation logic such as
- Schema Migrations
- Sample Data Population

This is somewhat a follow on from https://github.com/dotnet/aspire/discussions/6117 following David Fowler's suggestion of using Health Checks.
Whilst using Health Checks works for these initialisers, the UX is a bit funky in places, including
- Resources show as "Unhealthy" whilst they are being initialised
- Resources do not go healthy immediately after all initialisers are complete - needs to wait for the next health check poll interval.

[Operators](https://github.com/dotnet/aspire/issues/6040) could be a better way to plumb this in over the longer term.

## Examples

Basic example:

```cs
var sql = builder.AddSqlServer("sql");
var database = sql.AddDatabase("database");

database.WithInitialiser(async ctx =>
{
    var connectionString = await database.Resource.ConnectionStringExpression.GetValueAsync(ctx.CancellationToken);
    //Do something with the connection string
    await Task.Delay(TimeSpan.FromSeconds(5));
});


builder
    .WithInitialisationHealthChecks() // THIS CALL MUST BE LAST - see Known Issues
    .Build()
    .Run();
```

Rather than custom code, you can wait for completion of any other resource

```cs
var sql = builder.AddSqlServer("sql");
var database = sql.AddDatabase("database");

database.WithInitialiser(async ctx =>
{
    var connectionString = await database.Resource.ConnectionStringExpression.GetValueAsync(ctx.CancellationToken);
    //Do something with the connection string
    await Task.Delay(TimeSpan.FromSeconds(5));
});

// Or use another resource:
var migrator = builder.AddExecutable("db-migrations", "dotnet-ef", "..")
    .WithArgs("database",
        "update",
        "--connection", database.Resource.ConnectionStringExpression,
        "--project", new Projects.AspireInitialisation_DbMigrations().ProjectPath,
        "--no-build",
        "-v");

database.AddInitialiser("migrator", migrator);

builder
    .WithInitialisationHealthChecks() // THIS CALL MUST BE LAST - see Known Issues
    .Build()
    .Run();
```


# Known Issues

## WithInitialisationHealthChecks

NOTE, in the current implementation you MUST include a call to `WithInitialisationHealthChecks` AND it must the be the last thing before you start your app host.
I hope to find a way around this, but for now it's the state of things.

```
builder
    .WithInitialisationHealthChecks()
    .Build()
    .Run();
```

## Eventing

I've broken something in eventing, most likely I'm blocking somewhere I shouldn't be, but I haven't tracked down where.

- `AfterResourcesCreatedEvent` event either doesn't fire, or fires far far later than I'd expect it to (after many resources have started)
- Health Checks don't show any results in the UI until the initialisers have started.  (Non initialiser health checks seem to be blocked too)