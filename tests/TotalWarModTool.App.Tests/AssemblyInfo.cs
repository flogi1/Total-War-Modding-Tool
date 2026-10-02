// xUnit runs test classes in parallel, each on its own STA thread. With several classes doing their first WPF or XAML work at the same
// moment, the test host once stopped without any CPU use (none of the tests of those classes finished). WPF tests therefore run one after
// another, for the whole assembly: later tasks add more window tests, and none of them has to remember an attribute. Only this assembly is
// affected; the library test assemblies keep running in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
