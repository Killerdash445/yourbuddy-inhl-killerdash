# Command checks

Run `dotnet run --project tools/CommandChecks/CommandChecks.csproj -c Release` with the
repository's SDK installed. No NuGet test framework or game assemblies are required.

The project links the production dialog parser and conversation status partial. Test doubles
record dispatched orders and supply buddy state. Checks cover accidental substring commands,
negation, command precedence, group targeting, status without mutations, and context-specific
command lists. They do not validate movement, Unity integration, or actual task execution.
