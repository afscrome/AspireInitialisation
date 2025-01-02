# Aspire Initialisers

This is a proof of concept for doing initialisers in Aspire.

Initialisers run after all the health checks for a resource has passed, but before dependencies using `WaitFor` will start.
This makes it perfect for any kind of initialisation logic such as
- Schema Migrations
- Sample Data Population

This is somewhat a follow on from https://github.com/dotnet/aspire/discussions/6117 following David Fowler's suggestion of using Health Checks.
Whilst using Health Checks works for these initialisers, the UX is a bit funky in places, including
- Resources show as "Unhealthy" whilst they are being initialised
- Resources do not go healthy immediately after all initialisers are complete - needs to wait for the next health check poll interval (p to 5 seconds).

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
```

When using a resource as an initialiser, do not configure any `WaitFor` between the resources.

## Known Issues

- Resources show as `Unhealthy` whilst initialisers are running
- There can be up to 5 seconds delay between initialisers completing and any dependencies starting.
  This is due to Health Checks only being polled every 5 seconds.
  During this time, UX is a bit odd as the resource has progressed from `Initialising` to `Running`, but still shows as unhealthy.
  Not much can be done about this unless [Operators](https://github.com/dotnet/aspire/issues/6040) are introduced.
- Initialisers won't re-run if a resource is restarted.
