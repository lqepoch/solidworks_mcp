# Test rules

Hosted-safe tests must run without SOLIDWORKS. Contract tests are shared by FakeCad and the real provider. Live tests are explicit opt-in, use an isolated temporary workspace, validate geometry/semantic evidence rather than screenshots alone, and clean or quarantine artifacts. Evidence must record environment, command, exit code, pass/fail/skip/blocked and artifact paths after redaction.
