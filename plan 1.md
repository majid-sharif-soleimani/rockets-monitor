1. In this project, you need to use ASP.NET and ASP.NET Dependency Injection.
2. We need to separate the server in which we receive messages from the rest of the implementation. The separation is implemented using an interface and a concrete implementation (let's call it the listener). The purpose is to be able to implement another substitute for the listener later.
3. When a message is received, it must be stored inside a channel. We follow a producer-consumer pattern. The basic operations of the channel must be laid out in an interface, and a class that uses the .NET Channel behind the scenes must be the default implementation. In this way, we are able to use another implementation, such as Kafka, in future development.
4. Since we don't know how fast the sender (rocket) is, we need to support rate limiting (HTTP error code 429), an exception when the channel is exhausted (HTTP error code 503), and a `Retry-After` HTTP header as well. In addition, the channel must have a backpressure configuration where we define the capacity.
5. As the consumer, we need to have a class that wraps a proper queue structure (let's call it a rocket monitor). The queue must be sorted. We need to always save and update the latest status of a rocket to prevent recalculating the status. This consumer also needs to have a blueprint and a concrete implementation.
6. As another entity, we need a class that keeps track of all rocket monitors and provides us with all needed reports and functionalities related to the rocket collection.
7. All the reports must be created using REST services. You need to more carefully design this part to better separate the concerns. For example, the collection report and an individual rocket report must be separate.
8. Use the latest .NET version.
9. Use controllers.
10. Use xUnit. You need to take a TDD approach. You must write sufficient tests, but do not write excessive tests. You only need to write tests for the base functionalities.
11. We need to take a domain-driven, single-project-for-each-purpose approach.
12. We need a Swagger UI for this.
13. We need a Git repository for this.
14. Logs must be stored in a rolling file. Use the standard Microsoft logging or suggest something to use.
15. First, you need to analyze the README and my notes and come back to me with any design choices or design suggestions. Then you need to create a plan. I might read and change the plan. After a few iterations, if everything goes fine, I will tell you to implement the project.
