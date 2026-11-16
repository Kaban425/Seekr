using Xunit;

// WPF resources, Application.Current and shared UI services are process-global.
// STA fixture threads must not mutate them concurrently across test collections.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
