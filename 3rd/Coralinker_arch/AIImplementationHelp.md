### Coralinker Architecture: Implementation Notes

- Define vs Solve
  - Define stage: gather intent only. Create `Pin` requirements with optional `desiredPinName` or `Filter`. No hardware pins are assigned.
  - Solve stage: assign concrete `PinInstance`s based on constraints and preferences, then generate per-node sorting-network groupings and program connections.

- Requirements
  - Requirements are modeled as connection groups (union of pairwise `RequireConnect`).
  - Dump format: `connection group N: name(@desired) ...`
  - Verification: `LinkingRequirements.Test(Layout)` checks all intra-group pairs exist in `Layout`.

- Layout
  - Stores undirected links between pin names. Used both for preference scoring (reuse existing links) and verification.

- Nodes and Pins
  - `CoralinkerNodeDefinition` registers external pins and enables name lookup.
  - Pins can be referenced by name via `GetPin(name)` or by constraint via `Alloc(filter)`.
  - Assignment is deferred to Solve. Each `Pin` tracks its `originNode`, `desiredPinName`, and final `assignment`.

- Sorting Networks
  - A node can define one or more sorting networks with explicit ordered pin lists.
  - Solve prints per-node groupings of assigned pins under each sorting network: `nodeX.snY: (a, b), (c, d, ...)`.

- Solve strategy (current)
  - Preferences:
    1. Already wired to peers within the same group in existing `Layout`.
    2. Pin is part of any sorting network on the node.
  - Constraints enforced:
    - Avoid using pins already linked to other groups in existing layout.
    - Respect custom filters provided to `Alloc`.
  - Process:
    - Assign desired pins first if available.
    - Assign remaining pins by scoring candidates; fallback to any valid.
    - Link all pairs within each group and emit sorting-network groupings.

- Verification and Reporting
  - `DumpAssignments()` shows `name -> assignedPinName` per group.
  - Program prints: requirements, assignments, layout, and `Requirements met: true/false`.

- Next steps
  - Add hard constraints: amp limits, direction/cable type, forbidden internal wires.
  - Backtracking search when greedy scoring cannot satisfy all pins in a group.
  - Generate concrete switch matrices per node (`NodeSolution`) from sorting-network configuration.
  - Persist/ingest existing layouts (e.g., JSON), and compute diffs.
  - Reduce nullability warnings with proper annotations or initialization. 