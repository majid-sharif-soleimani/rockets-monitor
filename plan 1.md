1- In this project, you need to use Asp.Net and Asp.Net dependency injection.
2- We need to separate the server in which we recieve messages from the rest of implementation. The separation is implemented using an interface and a concrete implementation (let's call it the listener). The purpuse for this is being able to implement another substitute for the listener later.
3- When a message is received, it must be stored inside a channel. We follow a producer concumer pattern. the basic operations of the channel must be layouted in an interface and a class that uses the dotnet channel behind the sciene must be the fefault implementation. In this way, we are able to use another implementation like Kafka for the future development.
4- Since we don't know how fast is the sender (rocket), we need to support rate limiting, (http error coe 429), an exception when the channel is exhausted (http error code 503) and a Retry-After http header as well. In addition, the channel must have a backpressur config where we define the capacity.
5- As the consumer, we need to have a class that wraps a proper queue structure (Let's call it a rocket monitor). The queue must be sorted. We need to always save and update the latest status of a rocket to prevent re-calculating the statuse. This consumer also needs to have a blue print and a concrete implementation.
6- As another entity, we need a class that keeps track of all rocket monitors and provides us with all needed reports and functionalities related to the rockets collection.
7- All the reports must be created using some rest services. You need to more carfully design this part to better separate the concerns. For example, the collection report and an indivisual rocket report must be separate.
8- Use the latest dot net version.
9- Use controllers.
10- Use xUnit. You need to take a TDD approach. You must write sofficient tests but do not excess in writing tests. You only need to write the tests for the base functionalities.
11- We need to take a domain derriven, single project for each purpose approach.
12- We need a Swagger UI for this.
13- We need a git repository for this.
14- Logs must be stored in a rolling file. Use the standard microsoft logging or suggest something to use.
15- First, you need to analyze the readme and my notest and come back to me with any design choices or design suggestions. Then you need to create a plan. I might read and change the plan. After a few iteration, if everything goes fine, I will tell you to implement the project.
