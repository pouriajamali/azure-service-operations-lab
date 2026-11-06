# Release notes

## 0.1.0-preview.1

- Independent .NET 10 read-only synthetic service with cooperative cancellation,
  bounded concurrency, request correlation, JSON logs and separate health probes.
- Deterministic C# contract checks and actual-process HTTP/log verification.
- Separate foundation/application Bicep templates and manual deployment/rollback
  runbook, with secrets delivered by managed-identity Key Vault reference.
- Azure deployment remains pending an owner-controlled account, subscription,
  cost approval and successful cloud verification. No hosted endpoint is claimed.
