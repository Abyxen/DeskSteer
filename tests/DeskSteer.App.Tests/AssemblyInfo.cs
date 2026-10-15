using Xunit;
// Window fixtures share one STA Application; do not interleave graphics sessions or closing events.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
