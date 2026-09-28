# NexaConnect architecture tests

OrderPricingDependencyTests guards Order domain pricing contracts against framework and cross-context dependencies.

`AuthorizationDomainDependencyTests` protects Authorization Domain independence from framework and Infrastructure dependencies. Its focused check passed 1/1 during the policy-version-2 correction and is included in the joined portal CI job.

Cash-close dependency checks protect POS publication and Reporting snapshot Domain code from transport, persistence and shared integration-contract dependencies. POS/Reporting project-reference checks preserve bounded-context ownership. Run `dotnet test tests/Architecture/NexaConnect.ArchitectureTests/NexaConnect.ArchitectureTests.csproj`; source/dependency checks do not replace live projection acceptance. See [cash-close architecture and verification](../../../docs/API/Cash-Close-Reporting.md).
