# Script rules

PowerShell scripts must be deterministic, non-interactive by default, explicit about writes, and safe for public CI. Do not modify global registry/settings, write secrets, or delete broad paths. Machine-specific paths belong in user-local generated files and must be ignored by Git.
