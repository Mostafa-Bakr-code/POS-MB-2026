using Xunit;

// Every test opens its own uncommitted TransactionScope against the same shared
// test database - running them in parallel (xUnit's default) causes lock
// contention/deadlocks between concurrently-open transactions. Sequential
// execution is slower but correct.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
