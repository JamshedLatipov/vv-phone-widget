using Xunit;

// xUnit runs test collections in parallel by default, and this suite cannot
// take that. `HttpErrorNotifier.ErrorOccurred` is a static event: every service
// in the widget publishes to it from its HTTP error paths, and the tests that
// assert «this call published nothing» subscribe to that same global.
//
// The failure that motivated this: PrimaryLinkedIdClientTests asserted a
// ScriptService lookup stayed silent and got «TaskService: HTTP 400 BadRequest»
// instead — a notification raised by TaskServiceTests running concurrently in
// another collection. It reproduced on roughly two runs in three and passed in
// isolation every time, so it read as a Transfer regression rather than as the
// scheduling accident it was.
//
// Grouping the two subscribing classes into one collection would not have been
// enough: the publisher does not have to be a subscriber, so ANY concurrent test
// touching an HTTP error path can bleed into an assertion. Serialising the whole
// assembly closes the category. The suite runs in about three seconds, so the
// parallelism was buying nothing worth this.
//
// This is a trade, not a permanent fact: if the suite ever grows slow enough
// that serialising it starts costing more than it saves, the fix is to stop
// `HttpErrorNotifier` being a bare static event that every service publishes
// to — not to re-enable parallelism over that same shared global.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
