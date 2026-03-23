# Workflows Documentation

This system uses a code-based workflow engine (`WorkflowEngine.cs`) to route issues through five distinct phases or "Flows". These flows govern the lifecycle of an issue from creation to completion. Each state requires a specific AI Role (Agent) to handle the tasks.

## 1. Triage Flow
**Role:** Productmanager
**Start State:** `requested`
**Description:** New requests start here. The Product Manager checks to see if the request is valid and prioritizes it.

**Transitions:**
- **Reject or Duplicate?** → Goes to `invalid` (Closes issue, removes `release` labels).
- **Must-have?** → Goes to `planned` (Applies `release: release-current`).
- **Should-have?** → Goes to `planned` (Applies `release: release-next`).
- **Other / Default** → Goes to `planned` (Applies `release: planned`).

## 2. Plan Flow
**Role:** Architect
**Start State:** `planned`
**Description:** The Architect claims the ticket, outlines a possible technical solution, and documents their findings in the wiki and the issue comments.

**Transitions:**
- **Needs more input/help?** → Goes to `waiting customer` (Return to Product Manager for clarification).
- **Solution Planned successfully** → Goes to `work`.

## 3. Dev Flow
**Role:** Developer
**Start State:** `work`
**Description:** The Developer implements the issue by interpreting the architectural solution, making code changes, tests, and documents their technical solution.

**Transitions:**
- **Needs more input/help?** → Goes to `waiting customer`.
- **Implementation completed** → Goes to `review`.

## 4. QA Flow
**Role:** QA
**Start State:** `review`
**Description:** The QA Engineer reviews and tests the created solution. They execute automated tools and code reviews but do not have write access to system files or code (`write_file`, `mkdir`, etc. are restricted).

**Transitions:**
- **Should the solution be rejected?** → Goes back to `work` (Sends it to the developer).
- **Solution Accepted** → Goes to `check`.

## 5. Release Flow
**Role:** Productmanager
**Start State:** `check`
**Description:** The final validation phase. The Product Manager adds the necessary final documentation to the wiki and ensures the ticket corresponds to release expectations.

**Transitions:**
- **Documentation and Release steps finished** → Goes to `done` (Closes the issue).

---

### Special States
- **`waiting customer`**: A generic holding state. Feedback returns the issue either directly to `planned` or `work` depending on the instruction.
- **`invalid` or `done`**: Terminal states. Reaching these steps will automatically close the issue.
