using Xunit;

// Aml.Engine keeps process-global state (document/registry), so loading the same
// AML document from multiple test classes in parallel races. Serialise the CAEX
// test assembly — these tests are fast and IO-light, so the cost is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
