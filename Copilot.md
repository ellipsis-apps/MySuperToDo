# CLAUDE.md

## Overview
[Project name] - Modular Monolith Web API

## Tech Stack
- .NET 10, ASP.NET Core Minimal APIs
- EF Core 10 with PostgreSQL (snake_case naming convention)
- FluentValidation for request validation
- Result<T> pattern for error handling (no exceptions for business logic)
- JWT Authentication with Refresh Tokens
- OpenTelemetry for tracing and metrics
- Serilog for structured logging
- Swagger/OpenAPI for API documentation
- Docker for containerization

## Architecture
- Modular Monolith with Vertical Slice Architecture
- Clean Architecture: domain is isolated from infrastructure
- Each module: Domain, Core, Infrastructure, PublicApi projects
- CQRS with manual handlers (no MediatR, no interfaces)
- Manual mapping with extension methods

## Project Structure
- src/Modules.[Name].Domain/ - Entities, value objects, enums
- src/Modules.[Name].Core/ - Endpoints, handlers, validators
- src/Modules.[Name].Infrastructure/ - DbContext, EF config, migrations
- src/Modules.[Name].PublicApi/ - Cross-module contracts
- src/Modules.Common.API/ - Shared endpoint abstractions, error handling
- src/Modules.Common.Domain/ - Result<T>, IHandler, IEvent interfaces
- src/ModularMonolith.Host/ - Program.cs, DI composition

## File Naming
- Features/[UseCaseName]/[UseCaseName].Endpoint.cs
- Features/[UseCaseName]/[UseCaseName].Handler.cs
- Features/[UseCaseName]/[UseCaseName].Validator.cs
- Features/[UseCaseName]/[UseCaseName].Mapping.cs (if 2+ mappings)
- Features/Shared/Routes/RouteConsts.cs
- Features/Shared/Errors/[Module]Errors.cs

## Code Conventions
- Positional records for request/response DTOs
- File-scoped namespaces
- Primary constructors for dependency injection
- Sealed classes for implementations
- Internal by default, public only for contracts

## Patterns We Use
- Result<T> pattern for all handler return types
- IHandler marker interface for auto-registration
- IApiEndpoint for endpoint registration
- RouteConsts for centralized route definitions
- FluentValidation validators per use case
- Bogus for test data seeding
- EF Core DbContext directly in handlers

## Patterns We Do NOT Use
- Repository pattern
- AutoMapper or any mapping library
- MediatR or any mediator library
- Exceptions for business logic flow
- [FromServices] attribute

## Testing
- xUnit for test framework
- Moq for mocking
- Testcontainers for integration tests (PostgreSQL)
- Respawn for database cleanup between tests
- NetArchTest.Rules for architecture tests
- Test naming: [Method]_[Scenario]_[ExpectedResult]

## DI Registration
- Each module: Add[Module]Module(services, configuration)
- Auto-scan handlers: RegisterHandlersFromAssemblyContaining
- Auto-scan validators: AddValidatorsFromAssembly
- Auto-scan endpoints: RegisterApiEndpointsFromAssemblyContaining
