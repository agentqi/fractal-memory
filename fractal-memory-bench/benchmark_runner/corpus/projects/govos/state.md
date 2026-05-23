# Current State

Current objective: finalize GovOS endpoint planning and module boundaries.

Authentication direction:

- use cookie-backed sessions for the operator UI
- use JWT service tokens for API-to-API calls
- keep auth, API, and domain modules separate

Next best action: write the module-boundary note and review the auth split.
