// **One container at a time.**
//
// Every Docker container this assembly starts competes for the same daemon, the same CPU, and the
// same memory. Run them in parallel — with each other, and with the Testcontainers Postgres the rest
// of the suite spins up — and a short wall clock stops measuring the learner's code and starts
// measuring how busy the host was. So the whole assembly is serial: slower, and honest.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
